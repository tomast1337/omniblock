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
    private int _shadeModel = 1;

    private struct LightingState
    {
        public bool LightingEnabled = false;
        public float Light0DirX, Light0DirY, Light0DirZ;
        public float Light0DiffR, Light0DiffG, Light0DiffB;
        public float Light1DirX, Light1DirY, Light1DirZ;
        public float Light1DiffR, Light1DiffG, Light1DiffB;
        public float AmbientR = 0.2f, AmbientG = 0.2f, AmbientB = 0.2f;

        public LightingState()
        {
        }
    }

    private struct DirtyState
    {
        public bool DirtyLighting = true;
        public bool StateDirty = true;

        public DirtyState()
        {
        }
    }

    private LightingState _lightingState = new();
    private DirtyState _dirtyState = new();
    private Vector4D<float> _currentColorTint = Vector4D<float>.One;





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

    /// <inheritdoc cref="GLManager.Fog" />
    public FogState Fog { get; set; } = FogState.Default;

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
            if (_lightingState.LightingEnabled) _dirtyState.DirtyLighting = true;
            _fogUploaded = false;
        }

        if (_uploadedProjection != _projectionStack.Version) { _shader.SetProjection(_projectionStack.Top); _uploadedProjection = _projectionStack.Version; }
        if (_uploadedTextureMatrix != _textureStack.Version) { _shader.SetTextureMatrix(_textureStack.Top); _uploadedTextureMatrix = _textureStack.Version; }

        if (_dirtyState.StateDirty)
        {
            _shader.SetUseTexture(_useTexture);
            _shader.SetAlphaThreshold(_alphaTestEnabled ? _alphaThreshold : -1.0f);
            _shader.SetEnableLighting(_lightingState.LightingEnabled);
            _shader.SetEnableFog(_fogEnabled);
            _shader.SetShadeModel(_shadeModel);
            _dirtyState.StateDirty = false;
        }

        if (_uploadedModelView != _modelViewStack.Version)
        {
            _shader.SetModelView(_modelViewStack.Top);

            if (_lightingState.LightingEnabled)
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

        if (_lightingState.LightingEnabled && _dirtyState.DirtyLighting)
        {
            _shader.SetLight0(_lightingState.Light0DirX, _lightingState.Light0DirY, _lightingState.Light0DirZ, _lightingState.Light0DiffR, _lightingState.Light0DiffG, _lightingState.Light0DiffB);
            _shader.SetLight1(_lightingState.Light1DirX, _lightingState.Light1DirY, _lightingState.Light1DirZ, _lightingState.Light1DiffR, _lightingState.Light1DiffG, _lightingState.Light1DiffB);
            _shader.SetAmbientLight(_lightingState.AmbientR, _lightingState.AmbientG, _lightingState.AmbientB);
            _dirtyState.DirtyLighting = false;
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

    public override void Color3(float red, float green, float blue)
    {
        _currentColorTint = new Vector4D<float>(red, green, blue, 1.0f);
        SilkGL.VertexAttrib4(1, red, green, blue, 1.0f);
    }

    public override void Color3(byte red, byte green, byte blue)
    {
        float r = red / 255.0f, g = green / 255.0f, b = blue / 255.0f;
        _currentColorTint = new Vector4D<float>(r, g, b, 1.0f);
        SilkGL.VertexAttrib4(1, r, g, b, 1.0f);
    }

    public override void Color4(float red, float green, float blue, float alpha)
    {
        _currentColorTint = new Vector4D<float>(red, green, blue, alpha);
        SilkGL.VertexAttrib4(1, red, green, blue, alpha);
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
                if (_lightingState.LightingEnabled) return;
                _lightingState.LightingEnabled = true; _dirtyState.StateDirty = true; _dirtyState.DirtyLighting = true;
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
                if (!_lightingState.LightingEnabled) return;
                _lightingState.LightingEnabled = false; _dirtyState.StateDirty = true;
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

    private void TransformLightPosition(float* params_, out float tx, out float ty, out float tz)
    {
        float x = params_[0], y = params_[1], z = params_[2], w = params_[3];

        Matrix4X4<float> mv = _modelViewStack.Top;
        tx = x * mv.M11 + y * mv.M21 + z * mv.M31 + w * mv.M41;
        ty = x * mv.M12 + y * mv.M22 + z * mv.M32 + w * mv.M42;
        tz = x * mv.M13 + y * mv.M23 + z * mv.M33 + w * mv.M43;

        float len = MathF.Sqrt(tx * tx + ty * ty + tz * tz);
        if (len > 0) { tx /= len; ty /= len; tz /= len; }
    }

    public override void Light(GLEnum light, GLEnum pname, float* params_)
    {
        if (pname == GLEnum.Position)
        {
            TransformLightPosition(params_, out float tx, out float ty, out float tz);

            if (light == GLEnum.Light0) { _lightingState.Light0DirX = tx; _lightingState.Light0DirY = ty; _lightingState.Light0DirZ = tz; }
            else if (light == GLEnum.Light1) { _lightingState.Light1DirX = tx; _lightingState.Light1DirY = ty; _lightingState.Light1DirZ = tz; }
            _dirtyState.DirtyLighting = true;
        }
        else if (pname == GLEnum.Diffuse)
        {
            if (light == GLEnum.Light0) { _lightingState.Light0DiffR = params_[0]; _lightingState.Light0DiffG = params_[1]; _lightingState.Light0DiffB = params_[2]; }
            else if (light == GLEnum.Light1) { _lightingState.Light1DiffR = params_[0]; _lightingState.Light1DiffG = params_[1]; _lightingState.Light1DiffB = params_[2]; }
            _dirtyState.DirtyLighting = true;
        }
    }

    public override void LightModel(GLEnum pname, float* params_)
    {
        if (pname == GLEnum.LightModelAmbient)
        {
            _lightingState.AmbientR = params_[0];
            _lightingState.AmbientG = params_[1];
            _lightingState.AmbientB = params_[2];
            _dirtyState.DirtyLighting = true;
        }
    }

    public override void ColorMaterial(GLEnum face, GLEnum mode)
    {
    }

    public override void ShadeModel(GLEnum mode)
    {
        int newModel = mode == GLEnum.Smooth ? 1 : 0;
        if (_shadeModel != newModel)
        {
            _shadeModel = newModel;
            _dirtyState.StateDirty = true;
        }
    }

    public override void Normal3(float nx, float ny, float nz)
    {
        SilkGL.VertexAttrib3(3, nx, ny, nz);
    }

    public override void DrawArrays(GLEnum mode, int first, uint count)
    {
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

    public Vector4D<float> GetCurrentColorTint() => _currentColorTint;
    public float GetCurrentAlphaThreshold() => _alphaTestEnabled ? _alphaThreshold : -1.0f;

    /// <summary>Whether <c>Texture2D</c> is enabled, i.e. whether a draw would sample its texture.</summary>
    public bool GetTextureEnabled() => _useTexture;

    /// <summary>Whether fog is applied at all, i.e. whether <see cref="Fog" /> is being used.</summary>
    public bool GetFogEnabled() => _fogEnabled;

    public EntityLightingSnapshot GetLightingState() => new(
        _lightingState.LightingEnabled,
        new Vector3D<float>(_lightingState.Light0DirX, _lightingState.Light0DirY, _lightingState.Light0DirZ),
        new Vector3D<float>(_lightingState.Light0DiffR, _lightingState.Light0DiffG, _lightingState.Light0DiffB),
        new Vector3D<float>(_lightingState.Light1DirX, _lightingState.Light1DirY, _lightingState.Light1DirZ),
        new Vector3D<float>(_lightingState.Light1DiffR, _lightingState.Light1DiffG, _lightingState.Light1DiffB),
        new Vector3D<float>(_lightingState.AmbientR, _lightingState.AmbientG, _lightingState.AmbientB));
}

public readonly record struct EntityLightingSnapshot(
    bool Enabled,
    Vector3D<float> Light0Dir,
    Vector3D<float> Light0Diffuse,
    Vector3D<float> Light1Dir,
    Vector3D<float> Light1Diffuse,
    Vector3D<float> Ambient);
