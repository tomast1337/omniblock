using Silk.NET.Maths;
using Silk.NET.OpenGL;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Core.OpenGL;

public unsafe class EmulatedGL : LegacyGL
{
    private readonly MatrixStack _modelViewStack = new();
    private readonly MatrixStack _projectionStack = new();
    private readonly MatrixStack _textureStack = new();

    private GLEnum _currentMatrixMode = GLEnum.Modelview;

    private readonly FixedFunctionShader _shader;
    private bool _useTexture = false;
    private uint _currentProgram = 0;
    private bool _alphaTestEnabled = false;
    private float _alphaThreshold = 0.1f;
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

    private struct FogState
    {
        public bool FogEnabled = false;
        public int FogMode = 0; // 0=linear, 1=exp
        public float FogColorR, FogColorG, FogColorB, FogColorA;
        public float FogStart = 0f;
        public float FogEnd = 1f;
        public float FogDensity = 1f;

        public FogState()
        {
        }
    }

    private struct DirtyState
    {
        public bool DirtyLighting = true;
        public bool StateDirty = true;
        public bool DirtyFog = true;

        public DirtyState()
        {
        }
    }

    private LightingState _lightingState = new();
    private FogState _fogState = new();
    private DirtyState _dirtyState = new();
    private Vector4D<float> _currentColorTint = Vector4D<float>.One;

    private readonly uint _immediateVao;


    private bool _externalShaderActive;
    private int _externalMvUniform = -1;
    private int _externalProjUniform = -1;
    private int _externalTexMatUniform = -1;

    public EmulatedGL(GL gl) : base(gl)
    {
        _immediateVao = gl.GenVertexArray();
        gl.BindVertexArray(_immediateVao);

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

    internal MatrixStack ActiveStack => _currentMatrixMode switch
    {
        GLEnum.Modelview => _modelViewStack,
        GLEnum.Projection => _projectionStack,
        GLEnum.Texture => _textureStack,
        _ => _modelViewStack
    };

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

    /// <summary>Forces every matrix uniform to be written again on the next draw.</summary>
    private void InvalidateUploadedMatrices()
    {
        _uploadedModelView = Unuploaded;
        _uploadedProjection = Unuploaded;
        _uploadedTextureMatrix = Unuploaded;
    }

    internal void ActivateShader()
    {
        if (_externalShaderActive)
        {
            if (_uploadedModelView != _modelViewStack.Version && _externalMvUniform >= 0)
            {
                Matrix4X4<float> m = _modelViewStack.Top;
                unsafe { SilkGL.UniformMatrix4(_externalMvUniform, 1, false, (float*)&m); }
                _uploadedModelView = _modelViewStack.Version;
            }
            if (_uploadedProjection != _projectionStack.Version && _externalProjUniform >= 0)
            {
                Matrix4X4<float> m = _projectionStack.Top;
                unsafe { SilkGL.UniformMatrix4(_externalProjUniform, 1, false, (float*)&m); }
                _uploadedProjection = _projectionStack.Version;
            }
            if (_uploadedTextureMatrix != _textureStack.Version && _externalTexMatUniform >= 0)
            {
                Matrix4X4<float> m = _textureStack.Top;
                unsafe { SilkGL.UniformMatrix4(_externalTexMatUniform, 1, false, (float*)&m); }
                _uploadedTextureMatrix = _textureStack.Version;
            }
            return;
        }

        if (_currentProgram != _shader.Program)
        {
            SilkGL.UseProgram(_shader.Program);
            _currentProgram = _shader.Program;
            InvalidateUploadedMatrices();
            _dirtyState.StateDirty = true;
            if (_lightingState.LightingEnabled) _dirtyState.DirtyLighting = true;
            if (_fogState.FogEnabled) _dirtyState.DirtyFog = true;
        }

        if (_uploadedProjection != _projectionStack.Version) { _shader.SetProjection(_projectionStack.Top); _uploadedProjection = _projectionStack.Version; }
        if (_uploadedTextureMatrix != _textureStack.Version) { _shader.SetTextureMatrix(_textureStack.Top); _uploadedTextureMatrix = _textureStack.Version; }

        if (_dirtyState.StateDirty)
        {
            _shader.SetUseTexture(_useTexture);
            _shader.SetAlphaThreshold(_alphaTestEnabled ? _alphaThreshold : -1.0f);
            _shader.SetEnableLighting(_lightingState.LightingEnabled);
            _shader.SetEnableFog(_fogState.FogEnabled);
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

        if (_fogState.FogEnabled && _dirtyState.DirtyFog)
        {
            _shader.SetFogMode(_fogState.FogMode);
            _shader.SetFogColor(_fogState.FogColorR, _fogState.FogColorG, _fogState.FogColorB, _fogState.FogColorA);
            _shader.SetFogStart(_fogState.FogStart);
            _shader.SetFogEnd(_fogState.FogEnd);
            _shader.SetFogDensity(_fogState.FogDensity);
            _dirtyState.DirtyFog = false;
        }
    }

    public override void AlphaFunc(GLEnum func, float refValue)
    {
        _alphaThreshold = refValue;
        _dirtyState.StateDirty = true;
    }

    public override void BufferData(GLEnum target, nuint size, void* data, GLEnum usage)
    {
        SilkGL.BufferData(target.ToModern(), size, data, usage.ToModern());
    }

    public override void MatrixMode(GLEnum mode)
    {
        _currentMatrixMode = mode;
    }

    public override void LoadIdentity()
    {
        ActiveStack.LoadIdentity();
    }

    public override void PushMatrix()
    {
        ActiveStack.Push();
    }

    public override void PopMatrix()
    {
        ActiveStack.Pop();
    }

    public override void Translate(float x, float y, float z)
    {
        ActiveStack.Translate(x, y, z);
    }

    public override void Rotate(float angle, float x, float y, float z)
    {
        ActiveStack.Rotate(angle, x, y, z);
    }

    public override void Scale(float x, float y, float z)
    {
        ActiveStack.Scale(x, y, z);
    }

    public override void Scale(double x, double y, double z)
    {
        ActiveStack.Scale((float)x, (float)y, (float)z);
    }

    public override void Ortho(double left, double right, double bottom, double top, double zNear, double zFar)
    {
        ActiveStack.Ortho(left, right, bottom, top, zNear, zFar);
    }

    public override void Frustum(double left, double right, double bottom, double top, double zNear, double zFar)
    {
        ActiveStack.Frustum(left, right, bottom, top, zNear, zFar);
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

    public override void VertexPointer(int size, GLEnum type, uint stride, void* pointer)
    {
        SilkGL.BindVertexArray(_immediateVao);
        SilkGL.VertexAttribPointer(0, size, type.ToModern(), false, stride, pointer);
    }

    public override void ColorPointer(int size, ColorPointerType type, uint stride, void* pointer)
    {
        SilkGL.BindVertexArray(_immediateVao);
        SilkGL.VertexAttribPointer(1, size, (Silk.NET.OpenGL.GLEnum)type, true, stride, pointer);
    }

    public override void TexCoordPointer(int size, GLEnum type, uint stride, void* pointer)
    {
        SilkGL.BindVertexArray(_immediateVao);
        SilkGL.VertexAttribPointer(2, size, type.ToModern(), false, stride, pointer);
    }

    public override void NormalPointer(NormalPointerType type, uint stride, void* pointer)
    {
        SilkGL.BindVertexArray(_immediateVao);
        SilkGL.VertexAttribPointer(3, 3, (Silk.NET.OpenGL.GLEnum)type, true, stride, pointer);
    }

    public override void EnableClientState(GLEnum array)
    {
        SilkGL.BindVertexArray(_immediateVao);
        switch (array)
        {
            case GLEnum.VertexArray: SilkGL.EnableVertexAttribArray(0); break;
            case GLEnum.ColorArray: SilkGL.EnableVertexAttribArray(1); break;
            case GLEnum.TextureCoordArray: SilkGL.EnableVertexAttribArray(2); break;
            case GLEnum.NormalArray: SilkGL.EnableVertexAttribArray(3); break;
            default: break;
        }
    }

    public override void DisableClientState(GLEnum array)
    {
        SilkGL.BindVertexArray(_immediateVao);
        switch (array)
        {
            case GLEnum.VertexArray: SilkGL.DisableVertexAttribArray(0); break;
            case GLEnum.ColorArray: SilkGL.DisableVertexAttribArray(1); break;
            case GLEnum.TextureCoordArray: SilkGL.DisableVertexAttribArray(2); break;
            case GLEnum.NormalArray: SilkGL.DisableVertexAttribArray(3); break;
            default: break;
        }
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
                if (_fogState.FogEnabled) return;
                _fogState.FogEnabled = true; _dirtyState.StateDirty = true; _dirtyState.DirtyFog = true;
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
                if (!_fogState.FogEnabled) return;
                _fogState.FogEnabled = false; _dirtyState.StateDirty = true;
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

    public override void Fog(GLEnum pname, float param)
    {
        switch (pname)
        {
            case GLEnum.FogMode: _fogState.FogMode = (int)param == (int)GLEnum.Linear ? 0 : 1; break;
            case GLEnum.FogStart: _fogState.FogStart = param; break;
            case GLEnum.FogEnd: _fogState.FogEnd = param; break;
            case GLEnum.FogDensity: _fogState.FogDensity = param; break;
        }
        _dirtyState.DirtyFog = true;
    }

    public override void Fog(GLEnum pname, ReadOnlySpan<float> params_)
    {
        if (pname == GLEnum.FogColor && params_.Length >= 4)
        {
            _fogState.FogColorR = params_[0];
            _fogState.FogColorG = params_[1];
            _fogState.FogColorB = params_[2];
            _fogState.FogColorA = params_[3];
            _dirtyState.DirtyFog = true;
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
        if (_currentProgram == 0 || _currentProgram == _shader.Program || _externalShaderActive)
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

    public override void BeginExternalShader(int mvLoc, int projLoc, int texMatLoc = -1)
    {
        _externalShaderActive = true;
        _externalMvUniform = mvLoc;
        _externalProjUniform = projLoc;
        _externalTexMatUniform = texMatLoc;
        InvalidateUploadedMatrices();
    }

    public override void EndExternalShader()
    {
        _externalShaderActive = false;
    }

    public override void LineWidth(float width)
    {
        // TODO: ADD A BETTER WAY TO DO LINE WIDTH
        SilkGL.LineWidth(1.0f); // > 1.0 IS DEPRECATED
    }

    public Vector4D<float> GetCurrentColorTint() => _currentColorTint;
    public float GetCurrentAlphaThreshold() => _alphaTestEnabled ? _alphaThreshold : -1.0f;

    public EntityFogSnapshot GetFogState() => new(
        _fogState.FogEnabled,
        _fogState.FogMode,
        _fogState.FogStart,
        _fogState.FogEnd,
        _fogState.FogDensity,
        new Vector4D<float>(_fogState.FogColorR, _fogState.FogColorG, _fogState.FogColorB, _fogState.FogColorA));

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

public readonly record struct EntityFogSnapshot(
    bool Enabled,
    int Mode,
    float Start,
    float End,
    float Density,
    Vector4D<float> Color);
