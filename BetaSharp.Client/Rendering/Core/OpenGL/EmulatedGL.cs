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
    private bool _useTexture = false;
    private uint _currentProgram = 0;
    private bool _alphaTestEnabled = false;
    private float _alphaThreshold = 0.1f;
    private bool _fogEnabled = false;
    private bool _lightingEnabled = false;

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
            _shader.SetUseTexture(_useTexture);
            _shader.SetAlphaThreshold(_alphaTestEnabled ? _alphaThreshold : -1.0f);
            _shader.SetEnableLighting(_lightingEnabled);
            _shader.SetEnableFog(_fogEnabled);
            _shader.SetShadeModel((int)ShadeModel);
            _dirtyState.StateDirty = false;
        }

        if (_uploadedModelView != _modelViewStack.Version)
        {
            _shader.SetModelView(_modelViewStack.Top);

            if (_lightingEnabled)
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

        if (_lightingEnabled && (!_lightingUploaded || _uploadedLighting != Lighting))
        {
            LightingState lighting = Lighting;
            _shader.SetLight0(lighting.Light0Direction.X, lighting.Light0Direction.Y, lighting.Light0Direction.Z, lighting.Light0Diffuse.X, lighting.Light0Diffuse.Y, lighting.Light0Diffuse.Z);
            _shader.SetLight1(lighting.Light1Direction.X, lighting.Light1Direction.Y, lighting.Light1Direction.Z, lighting.Light1Diffuse.X, lighting.Light1Diffuse.Y, lighting.Light1Diffuse.Z);
            _shader.SetAmbientLight(lighting.Ambient.X, lighting.Ambient.Y, lighting.Ambient.Z);
            _uploadedLighting = lighting;
            _lightingUploaded = true;
        }

        if (_fogEnabled && (!_fogUploaded || _uploadedFog != Fog))
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

    public override void Enable(GLEnum cap)
    {
        // Redundant Enable(AlphaTest)/Enable(Fog) calls are extremely common (e.g. every
        // entity re-asserts AlphaTest on unconditionally) and RasterStateChanging forces
        // batched renderers (EntityBatchRenderer) to flush, so only fire it on a real
        // state transition - otherwise every one of those calls flushes the batch for
        // nothing.
        switch (cap)
        {
            case GLEnum.Texture2D:
                if (_useTexture) return;
                _useTexture = true; _dirtyState.StateDirty = true;
                OnRasterStateChanging(cap);
                return;
            case GLEnum.AlphaTest:
                if (_alphaTestEnabled) return;
                _alphaTestEnabled = true; _dirtyState.StateDirty = true;
                OnRasterStateChanging(cap);
                return;
            case GLEnum.Lighting:
                if (_lightingEnabled) return;
                _lightingEnabled = true; _dirtyState.StateDirty = true;
                OnRasterStateChanging(cap);
                return;
            case GLEnum.Fog:
                if (_fogEnabled) return;
                _fogEnabled = true; _dirtyState.StateDirty = true;
                OnRasterStateChanging(cap);
                return;
            case GLEnum.Light0: return;
            case GLEnum.Light1: return;
            case GLEnum.ColorMaterial: return;
            case GLEnum.RescaleNormal: return;
        }
        OnRasterStateChanging(cap);
        SilkGL.Enable(cap.ToModern());
    }

    public override void Disable(GLEnum cap)
    {
        switch (cap)
        {
            case GLEnum.Texture2D:
                if (!_useTexture) return;
                _useTexture = false; _dirtyState.StateDirty = true;
                OnRasterStateChanging(cap);
                return;
            case GLEnum.AlphaTest:
                if (!_alphaTestEnabled) return;
                _alphaTestEnabled = false; _dirtyState.StateDirty = true;
                OnRasterStateChanging(cap);
                return;
            case GLEnum.Lighting:
                if (!_lightingEnabled) return;
                _lightingEnabled = false; _dirtyState.StateDirty = true;
                OnRasterStateChanging(cap);
                return;
            case GLEnum.Fog:
                if (!_fogEnabled) return;
                _fogEnabled = false; _dirtyState.StateDirty = true;
                OnRasterStateChanging(cap);
                return;
            case GLEnum.Light0: return;
            case GLEnum.Light1: return;
            case GLEnum.ColorMaterial: return;
            case GLEnum.RescaleNormal: return;
        }
        OnRasterStateChanging(cap);
        SilkGL.Disable(cap.ToModern());
    }

    public override void Disable(EnableCap cap)
    {
        Disable((GLEnum)cap);
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

    public float GetCurrentAlphaThreshold() => _alphaTestEnabled ? _alphaThreshold : -1.0f;

    /// <summary>Whether <c>Texture2D</c> is enabled, i.e. whether a draw would sample its texture.</summary>
    public bool GetTextureEnabled() => _useTexture;

    /// <summary>Whether fog is applied at all, i.e. whether <see cref="Fog" /> is being used.</summary>
    public bool GetFogEnabled() => _fogEnabled;

    /// <summary>Whether anything is lit at all, i.e. whether <see cref="Lighting" /> is being used.</summary>
    public bool GetLightingEnabled() => _lightingEnabled;
}
