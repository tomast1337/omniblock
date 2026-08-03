using Silk.NET.Maths;
using Silk.NET.OpenGL;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Core.OpenGL;

public unsafe class EmulatedGL : LegacyGL
{
    private readonly MatrixStack _modelViewStack = new();
    private readonly MatrixStack _projectionStack = new();
    private readonly MatrixStack _textureStack = new();

    private readonly FixedFunctionShader _shader;
    private uint _currentProgram = 0;
    private float _alphaThreshold = 0.1f;

    private struct DirtyState
    {
        public bool StateDirty = true;

        public DirtyState()
        {
        }
    }

    private DirtyState _dirtyState = new();

    public EmulatedGL(GL gl) : base(gl)
    {
        _shader = new FixedFunctionShader(gl);
        _shader.Use();
        _shader.SetTexture0(0);
    }

    /// <summary>
    ///     The transform stacks, for callers that hold one rather than steering it through
    ///     <see cref="MatrixMode" />.
    /// </summary>
    /// <remarks>
    ///     Composing transforms on a stack is not the legacy part; hierarchical models need it and
    ///     it survives into any backend. What has to go is reaching it through a mode selector and
    ///     a global, so these are exposed and the fixed-function entry points forward to them.
    /// </remarks>
    public MatrixStack ModelView => _modelViewStack;

    /// <inheritdoc cref="ModelView" />
    public MatrixStack Projection => _projectionStack;

    /// <inheritdoc cref="ModelView" />
    public MatrixStack TextureMatrix => _textureStack;

    /// <inheritdoc cref="GLManager.Color" />
    public Vector4D<float> Color
    {
        get;
        set
        {
            field = value;
            SilkGL.VertexAttrib4(1, value.X, value.Y, value.Z, value.W);
        }
    } = Vector4D<float>.One;

    /// <inheritdoc cref="GLManager.Normal" />
    public Vector3D<float> Normal
    {
        get;
        set
        {
            field = value;
            SilkGL.VertexAttrib3(3, value.X, value.Y, value.Z);
        }
    }

    /// <inheritdoc cref="GLManager.Fog" />
    public FogState Fog { get; set; } = FogState.Default;

    /// <inheritdoc cref="GLManager.Lighting" />
    public LightingState Lighting { get; set; } = LightingState.Default;

    /// <summary>
    ///     The four capabilities a core context does not have, which are shader uniforms here.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Each one deduplicates, because redundant assignments are extremely common — every
    ///         entity re-asserts the alpha test on unconditionally — and two of them cost a batch
    ///         flush.
    ///     </para>
    ///     <para>
    ///         Only the alpha test and fog raise <see cref="LegacyGL.RasterStateChanging" />, which
    ///         is what drains <c>EntityBatchRenderer</c>. That batch bakes lighting into vertex
    ///         colours and tracks its own texture, so those two it already accounts for; the alpha
    ///         threshold and the fog it does not, and queued geometry would draw under the wrong
    ///         one.
    ///     </para>
    /// </remarks>
    public bool TextureEnabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _dirtyState.StateDirty = true;
        }
    }

    /// <inheritdoc cref="TextureEnabled" />
    public bool LightingEnabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _dirtyState.StateDirty = true;
        }
    }

    /// <inheritdoc cref="TextureEnabled" />
    public bool AlphaTestEnabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _dirtyState.StateDirty = true;
            OnRasterStateChanging();
        }
    }

    /// <inheritdoc cref="TextureEnabled" />
    public bool FogEnabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            _dirtyState.StateDirty = true;
            OnRasterStateChanging();
        }
    }

    /// <inheritdoc cref="GLManager.ShadeModel" />
    public ShadeModel ShadeModel
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            _dirtyState.StateDirty = true;
        }
    } = ShadeModel.Smooth;

    /// <inheritdoc cref="GLManager.AlphaThreshold" />
    public float AlphaThreshold
    {
        get => _alphaThreshold;
        set
        {
            if (_alphaThreshold == value)
            {
                return;
            }

            _alphaThreshold = value;
            _dirtyState.StateDirty = true;
        }
    }

    /// <summary>
    ///     The stack versions last written to the active program, or <see cref="Unuploaded" /> when
    ///     nothing has been.
    /// </summary>
    /// <remarks>
    ///     Compared against <see cref="MatrixStack.Version" /> rather than tracked as dirty flags,
    ///     because the stacks are reachable through <c>GLManager</c> and a caller mutating one
    ///     directly has no way to raise a flag held here.
    /// </remarks>
    private const uint Unuploaded = uint.MaxValue;

    private uint _uploadedModelView = Unuploaded;
    private uint _uploadedProjection = Unuploaded;
    private uint _uploadedTextureMatrix = Unuploaded;

    /// <summary>The fog last written to the active program, and whether any has been.</summary>
    /// <remarks>
    ///     Compared rather than tracked as a dirty flag, for the same reason the matrix stacks are:
    ///     <see cref="Fog" /> is assigned from outside and a writer there cannot raise a flag here.
    /// </remarks>
    private FogState _uploadedFog;

    private bool _fogUploaded;

    /// <inheritdoc cref="_uploadedFog" />
    private LightingState _uploadedLighting;

    private bool _lightingUploaded;

    /// <summary>Forces every matrix uniform to be written again on the next draw.</summary>
    private void InvalidateUploadedMatrices()
    {
        _uploadedModelView = Unuploaded;
        _uploadedProjection = Unuploaded;
        _uploadedTextureMatrix = Unuploaded;
    }

    internal void ActivateShader()
    {
        if (_currentProgram != _shader.Program)
        {
            SilkGL.UseProgram(_shader.Program);
            _currentProgram = _shader.Program;
            InvalidateUploadedMatrices();
            _dirtyState.StateDirty = true;
            _lightingUploaded = false;
            _fogUploaded = false;
        }

        if (_uploadedProjection != _projectionStack.Version) { _shader.SetProjection(_projectionStack.Top); _uploadedProjection = _projectionStack.Version; }
        if (_uploadedTextureMatrix != _textureStack.Version) { _shader.SetTextureMatrix(_textureStack.Top); _uploadedTextureMatrix = _textureStack.Version; }

        if (_dirtyState.StateDirty)
        {
            _shader.SetUseTexture(TextureEnabled);
            _shader.SetAlphaThreshold(AlphaTestEnabled ? _alphaThreshold : -1.0f);
            _shader.SetEnableLighting(LightingEnabled);
            _shader.SetEnableFog(FogEnabled);
            _shader.SetShadeModel((int)ShadeModel);
            _dirtyState.StateDirty = false;
        }

        if (_uploadedModelView != _modelViewStack.Version)
        {
            _shader.SetModelView(_modelViewStack.Top);

            if (LightingEnabled)
            {
                Matrix4X4<float> mv = _modelViewStack.Top;
                if (Matrix4X4.Invert(mv, out Matrix4X4<float> invMv))
                {
                    var t = Matrix4X4.Transpose(invMv);
                    var normalMatrix = new Matrix3X3<float>(
                        t.M11, t.M12, t.M13,
                        t.M21, t.M22, t.M23,
                        t.M31, t.M32, t.M33);
                    _shader.SetNormalMatrix(normalMatrix);
                }
                else
                {
                    _shader.SetNormalMatrix(Matrix3X3<float>.Identity);
                }
            }
            _uploadedModelView = _modelViewStack.Version;
        }

        if (LightingEnabled && (!_lightingUploaded || _uploadedLighting != Lighting))
        {
            LightingState lighting = Lighting;
            _shader.SetLight0(lighting.Light0Direction.X, lighting.Light0Direction.Y, lighting.Light0Direction.Z, lighting.Light0Diffuse.X, lighting.Light0Diffuse.Y, lighting.Light0Diffuse.Z);
            _shader.SetLight1(lighting.Light1Direction.X, lighting.Light1Direction.Y, lighting.Light1Direction.Z, lighting.Light1Diffuse.X, lighting.Light1Diffuse.Y, lighting.Light1Diffuse.Z);
            _shader.SetAmbientLight(lighting.Ambient.X, lighting.Ambient.Y, lighting.Ambient.Z);
            _uploadedLighting = lighting;
            _lightingUploaded = true;
        }

        if (FogEnabled && (!_fogUploaded || _uploadedFog != Fog))
        {
            FogState fog = Fog;
            _shader.SetFogMode((int)fog.Curve);
            _shader.SetFogColor(fog.Color.X, fog.Color.Y, fog.Color.Z, fog.Color.W);
            _shader.SetFogStart(fog.Start);
            _shader.SetFogEnd(fog.End);
            _shader.SetFogDensity(fog.Density);
            _uploadedFog = fog;
            _fogUploaded = true;
        }
    }

    public override void BufferData(GLEnum target, nuint size, void* data, GLEnum usage)
    {
        SilkGL.BufferData(target.ToModern(), size, data, usage.ToModern());
    }

    public override void DrawArrays(GLEnum mode, int first, uint count)
    {
        OnImmediateGeometryDrawing();

        if (_currentProgram == 0 || _currentProgram == _shader.Program)
        {
            ActivateShader();
        }

        SilkGL.DrawArrays(mode.ToModern(), first, count);
    }

    public override void UseProgram(uint program)
    {
        _currentProgram = program;
        _dirtyState.StateDirty = true;
        InvalidateUploadedMatrices();
        base.UseProgram(program);
    }

    public override void GetFloat(GLEnum pname, float* data)
    {
        if (pname == GLEnum.ModelviewMatrix)
        {
            Matrix4X4<float> m = _modelViewStack.Top;
            System.Buffer.MemoryCopy(&m, data, 64, 64);
        }
        else if (pname == GLEnum.ProjectionMatrix)
        {
            Matrix4X4<float> m = _projectionStack.Top;
            System.Buffer.MemoryCopy(&m, data, 64, 64);
        }
    }

    public override void GetFloat(GLEnum pname, Span<float> data)
    {
        if (pname == GLEnum.ModelviewMatrix)
        {
            Matrix4X4<float> m = _modelViewStack.Top;
            fixed (float* dst = data)
            {
                System.Buffer.MemoryCopy(&m, dst, 64, 64);
            }
        }
        else if (pname == GLEnum.ProjectionMatrix)
        {
            Matrix4X4<float> m = _projectionStack.Top;
            fixed (float* dst = data)
            {
                System.Buffer.MemoryCopy(&m, dst, 64, 64);
            }
        }
    }

    public override void LineWidth(float width)
    {
        // TODO: ADD A BETTER WAY TO DO LINE WIDTH
        SilkGL.LineWidth(1.0f); // > 1.0 IS DEPRECATED
    }

    public float GetCurrentAlphaThreshold() => AlphaTestEnabled ? _alphaThreshold : -1.0f;

}
