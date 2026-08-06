using Silk.NET.Maths;
using Silk.NET.OpenGL;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Core.OpenGL;

/// <summary>
///     The OpenGL implementation of <see cref="IGL" /> and the holder of the per-context rendering
///     state that was once the fixed-function pipeline: matrix stacks, tint, facing, fog, lights.
/// </summary>
/// <remarks>
///     <para>
///         This was <c>EmulatedGL</c>, and the name was accurate while it emulated fixed-function
///         entry points over a core context. There are none left to emulate — the state is read by
///         whichever <c>ISlotProgram</c> a draw names, which is why every draw has to name one.
///     </para>
///     <para>
///         What is left is the state itself — the matrix stacks, the tint, the facing, the fog and
///         the lights — held here because it is per-context and assigned from everywhere. A backend
///         with no fixed-function heritage would keep exactly this and drop the class name.
///     </para>
/// </remarks>
public unsafe class FixedFunctionPipeline : IGL
{
    private readonly GL _silkGL;

    public FixedFunctionPipeline(GL gl)
    {
        _silkGL = gl;
    }

    // ── Matrix stacks ──────────────────────────────────────────────────────

    /// <inheritdoc cref="GLManager.ModelView" />
    public MatrixStack ModelView { get; } = new();

    /// <inheritdoc cref="ModelView" />
    public MatrixStack Projection { get; } = new();

    /// <inheritdoc cref="ModelView" />
    public MatrixStack TextureMatrix { get; } = new();

    // ── Per-vertex defaults ─────────────────────────────────────────────────

    /// <inheritdoc cref="GLManager.Color" />
    public Vector4D<float> Color
    {
        get;
        set
        {
            field = value;
            _silkGL.VertexAttrib4(1, value.X, value.Y, value.Z, value.W);
        }
    } = Vector4D<float>.One;

    /// <inheritdoc cref="GLManager.Normal" />
    public Vector3D<float> Normal
    {
        get;
        set
        {
            field = value;
            _silkGL.VertexAttrib3(3, value.X, value.Y, value.Z);
        }
    }

    // ── Capabilities (formerly Enable/Disable of removed fixed-function caps) ──

    public bool TextureEnabled { get; set; }
    public bool LightingEnabled { get; set; }

    public bool AlphaTestEnabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            GLManager.OnRasterStateChanging();
        }
    }

    public bool FogEnabled
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            GLManager.OnRasterStateChanging();
        }
    }

    public ShadeModel ShadeModel { get; set; } = ShadeModel.Smooth;
    public float AlphaThreshold { get; set; } = 0.1f;

    // ── Per-pass state ─────────────────────────────────────────────────────

    public FogState Fog { get; set; } = FogState.Default;
    public LightingState Lighting { get; set; } = LightingState.Default;
    public WorldLightState WorldLight { get; set; } = WorldLightState.Default;

    // ── IGL: buffer objects ─────────────────────────────────────────────────

    public uint GenBuffer() => _silkGL.GenBuffer();

    public void GenBuffers(uint n, Span<uint> buffers) => _silkGL.GenBuffers(n, buffers);

    public void GenBuffers(Span<uint> buffers) => _silkGL.GenBuffers(buffers);

    public void BindBuffer(GLEnum target, uint buffer) =>
        _silkGL.BindBuffer(target.ToModern(), buffer);

    public void DeleteBuffer(uint buffer) => _silkGL.DeleteBuffer(buffer);

    public void BufferData<T>(GLEnum target, ReadOnlySpan<T> data, GLEnum usage) where T : unmanaged =>
        _silkGL.BufferData(target.ToModern(), data, usage.ToModern());

    public void BufferData(GLEnum target, nuint size, void* data, GLEnum usage) =>
        _silkGL.BufferData(target.ToModern(), size, data, usage.ToModern());

    public void BufferSubData<T>(GLEnum target, nint offset, ReadOnlySpan<T> data) where T : unmanaged =>
        _silkGL.BufferSubData(target.ToModern(), offset, data);

    public void BindBufferBase(BufferTargetARB target, uint index, uint buffer) =>
        _silkGL.BindBufferBase(target, index, buffer);

    // ── IGL: vertex arrays ──────────────────────────────────────────────────

    public uint GenVertexArray() => _silkGL.GenVertexArray();

    public void BindVertexArray(uint array) => _silkGL.BindVertexArray(array);

    public void DeleteVertexArray(uint array) => _silkGL.DeleteVertexArray(array);

    public void EnableVertexAttribArray(uint index) => _silkGL.EnableVertexAttribArray(index);

    public void DisableVertexAttribArray(uint index) => _silkGL.DisableVertexAttribArray(index);

    public void VertexAttribPointer(uint index, int size, GLEnum type, bool normalized, uint stride, void* pointer) =>
        _silkGL.VertexAttribPointer(index, size, type.ToModern(), normalized, stride, pointer);

    public void VertexAttribIPointer(uint index, int size, GLEnum type, uint stride, void* pointer) =>
        _silkGL.VertexAttribIPointer(index, size, type.ToModern(), stride, pointer);

    // ── IGL: drawing ────────────────────────────────────────────────────────

    public void DrawArrays(GLEnum mode, int first, uint count)
    {
        GLManager.OnImmediateGeometryDrawing();
        _silkGL.DrawArrays(mode.ToModern(), first, count);
    }

    public void DrawArraysInstanced(GLEnum mode, int first, uint count, uint instanceCount) =>
        _silkGL.DrawArraysInstanced(mode.ToModern(), first, count, instanceCount);

    // ── IGL: shaders ────────────────────────────────────────────────────────

    public uint CreateShader(ShaderType type) => _silkGL.CreateShader(type);

    public void ShaderSource(uint shader, string source) => _silkGL.ShaderSource(shader, source);

    public void CompileShader(uint shader) => _silkGL.CompileShader(shader);

    public void GetShader(uint shader, ShaderParameterName pname, out int result) =>
        _silkGL.GetShader(shader, pname, out result);

    public string GetShaderInfoLog(uint shader) => _silkGL.GetShaderInfoLog(shader);

    public void DeleteShader(uint shader) => _silkGL.DeleteShader(shader);

    public uint CreateProgram() => _silkGL.CreateProgram();

    public void AttachShader(uint program, uint shader) => _silkGL.AttachShader(program, shader);

    public void LinkProgram(uint program) => _silkGL.LinkProgram(program);

    public void GetProgram(uint program, ProgramPropertyARB pname, out int result) =>
        _silkGL.GetProgram(program, pname, out result);

    public string GetProgramInfoLog(uint program) => _silkGL.GetProgramInfoLog(program);

    public void UseProgram(uint program) => _silkGL.UseProgram(program);

    public void DeleteProgram(uint program) => _silkGL.DeleteProgram(program);

    public int GetUniformLocation(uint program, string name) =>
        _silkGL.GetUniformLocation(program, name);

    // ── IGL: uniforms ───────────────────────────────────────────────────────

    public void Uniform1(int location, int v0) => _silkGL.Uniform1(location, v0);
    public void Uniform1(int location, float v0) => _silkGL.Uniform1(location, v0);
    public void Uniform2(int location, float v0, float v1) => _silkGL.Uniform2(location, v0, v1);
    public void Uniform3(int location, float v0, float v1, float v2) => _silkGL.Uniform3(location, v0, v1, v2);
    public void Uniform4(int location, float v0, float v1, float v2, float v3) => _silkGL.Uniform4(location, v0, v1, v2, v3);
    public void UniformMatrix3(int location, uint count, bool transpose, float* value) => _silkGL.UniformMatrix3(location, count, transpose, value);
    public void UniformMatrix4(int location, uint count, bool transpose, float* value) => _silkGL.UniformMatrix4(location, count, transpose, value);

    // ── IGL: textures ───────────────────────────────────────────────────────

    /// <summary>
    ///     The texture currently bound to <see cref="GLEnum.Texture2D" /> on unit 0, so a renderer
    ///     that binds its own texture out-of-band can put back what the caller had.
    /// </summary>
    public uint BoundTexture2D { get; private set; }

    public uint GenTexture() => _silkGL.GenTexture();

    public void GenTextures(Span<uint> textures) => _silkGL.GenTextures(textures);

    public void BindTexture(GLEnum target, uint texture)
    {
        if (target == GLEnum.Texture2D)
        {
            BoundTexture2D = texture;
        }

        _silkGL.BindTexture(target.ToModern(), texture);
    }

    public void DeleteTexture(uint texture) => _silkGL.DeleteTexture(texture);

    public void DeleteTextures(uint n, ReadOnlySpan<uint> textures) => _silkGL.DeleteTextures(n, textures);
    public void DeleteTextures(ReadOnlySpan<uint> textures) => _silkGL.DeleteTextures(textures);

    public void ActiveTexture(GLEnum texture) => _silkGL.ActiveTexture((TextureUnit)texture.ToModern());

    public void TexParameter(TextureTarget target, TextureParameterName pname, int param) =>
        _silkGL.TexParameter(target, pname, param);

    public void TexParameter(GLEnum target, GLEnum pname, int param) =>
        _silkGL.TexParameter(target.ToModern(), pname.ToModern(), param);

    public void TexParameter(GLEnum target, GLEnum pname, float param) =>
        _silkGL.TexParameter(target.ToModern(), pname.ToModern(), param);

    public void TexImage2D(TextureTarget target, int level, InternalFormat internalformat, uint width,
        uint height, int border, PixelFormat format, PixelType type, void* pixels) =>
        _silkGL.TexImage2D(target, level, internalformat, width, height, border, format, type, pixels);

    public void TexImage2D(GLEnum target, int level, int internalformat, uint width, uint height,
        int border, GLEnum format, GLEnum type, void* pixels) =>
        _silkGL.TexImage2D(target.ToModern(), level, internalformat, width, height, border,
            format.ToModern(), type.ToModern(), pixels);

    public void TexImage3D(GLEnum target, int level, int internalformat, uint width, uint height,
        uint depth, int border, GLEnum format, GLEnum type, void* pixels) =>
        _silkGL.TexImage3D(target.ToModern(), level, internalformat, width, height, depth, border,
            format.ToModern(), type.ToModern(), pixels);

    public void TexSubImage2D(GLEnum target, int level, int xoffset, int yoffset, uint width,
        uint height, GLEnum format, GLEnum type, void* pixels) =>
        _silkGL.TexSubImage2D(target.ToModern(), level, xoffset, yoffset, width, height,
            format.ToModern(), type.ToModern(), pixels);

    public void TexSubImage3D(GLEnum target, int level, int xoffset, int yoffset, int zoffset,
        uint width, uint height, uint depth, GLEnum format, GLEnum type, void* pixels) =>
        _silkGL.TexSubImage3D(target.ToModern(), level, xoffset, yoffset, zoffset, width, height,
            depth, format.ToModern(), type.ToModern(), pixels);

    public void PixelStore(PixelStoreParameter pname, int param) => _silkGL.PixelStore(pname, param);

    // ── IGL: framebuffers ───────────────────────────────────────────────────

    public uint GenFramebuffer() => _silkGL.GenFramebuffer();

    public void BindFramebuffer(FramebufferTarget target, uint framebuffer) =>
        _silkGL.BindFramebuffer(target, framebuffer);

    public void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment attachment,
        TextureTarget textarget, uint texture, int level) =>
        _silkGL.FramebufferTexture2D(target, attachment, textarget, texture, level);

    public void GenRenderbuffers(Span<uint> renderbuffers) => _silkGL.GenRenderbuffers(renderbuffers);

    public void BindRenderbuffer(RenderbufferTarget target, uint renderbuffer) =>
        _silkGL.BindRenderbuffer(target, renderbuffer);

    public void RenderbufferStorage(RenderbufferTarget target, InternalFormat internalformat,
        uint width, uint height) =>
        _silkGL.RenderbufferStorage(target, internalformat, width, height);

    public void FramebufferRenderbuffer(FramebufferTarget target, FramebufferAttachment attachment,
        RenderbufferTarget renderbuffertarget, uint renderbuffer) =>
        _silkGL.FramebufferRenderbuffer(target, attachment, renderbuffertarget, renderbuffer);

    public Silk.NET.OpenGL.GLEnum CheckFramebufferStatus(FramebufferTarget target) =>
        _silkGL.CheckFramebufferStatus(target);

    public void DeleteFramebuffer(uint framebuffer) => _silkGL.DeleteFramebuffer(framebuffer);
    public void DeleteRenderbuffer(uint renderbuffer) => _silkGL.DeleteRenderbuffer(renderbuffer);

    public void BlitFramebuffer(int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0,
        int dstX1, int dstY1, uint mask, BlitFramebufferFilter filter) =>
        _silkGL.BlitFramebuffer(srcX0, srcY0, srcX1, srcY1, dstX0, dstY0, dstX1, dstY1, mask, filter);

    // ── IGL: raster state ───────────────────────────────────────────────────

    public void BlendFunc(GLEnum sfactor, GLEnum dfactor)
    {
        GLManager.OnRasterStateChanging();
        _silkGL.BlendFunc(sfactor.ToModern(), dfactor.ToModern());
    }

    public void CullFace(GLEnum mode) => _silkGL.CullFace(mode.ToModern());

    public void DepthFunc(GLEnum func)
    {
        GLManager.OnRasterStateChanging();
        _silkGL.DepthFunc(func.ToModern());
    }

    public void DepthMask(bool flag)
    {
        GLManager.OnRasterStateChanging();
        _silkGL.DepthMask(flag);
    }

    public void ColorMask(bool red, bool green, bool blue, bool alpha) =>
        _silkGL.ColorMask(red, green, blue, alpha);

    public void Enable(GLEnum cap)
    {
        if (cap is GLEnum.Blend or GLEnum.AlphaTest or GLEnum.DepthTest or GLEnum.Fog)
        {
            GLManager.OnRasterStateChanging();
        }

        _silkGL.Enable(cap.ToModern());
    }

    public void Disable(EnableCap cap)
    {
        GLEnum glCap = (GLEnum)cap;
        if (glCap is GLEnum.Blend or GLEnum.AlphaTest or GLEnum.DepthTest or GLEnum.Fog)
        {
            GLManager.OnRasterStateChanging();
        }

        _silkGL.Disable(cap);
    }

    public void Disable(GLEnum cap)
    {
        if (cap is GLEnum.Blend or GLEnum.AlphaTest or GLEnum.DepthTest or GLEnum.Fog)
        {
            GLManager.OnRasterStateChanging();
        }

        _silkGL.Disable(cap.ToModern());
    }

    public void PolygonOffset(float factor, float units) => _silkGL.PolygonOffset(factor, units);

    public void LineWidth(float width)
    {
        // > 1.0 IS DEPRECATED in core profile
        _silkGL.LineWidth(1.0f);
    }

    // ── IGL: queries / debug ────────────────────────────────────────────────

    public GLEnum GetError() => (GLEnum)_silkGL.GetError();

    public void GetFloat(GLEnum pname, Span<float> data) => _silkGL.GetFloat(pname.ToModern(), data);

    public void GetFloat(GLEnum pname, out float data)
    {
        fixed (float* ptr = &data) { _silkGL.GetFloat(pname.ToModern(), ptr); }
    }

    public void GetFloat(GLEnum pname, float* data) => _silkGL.GetFloat(pname.ToModern(), data);

    public string GetString(StringName name) => _silkGL.GetStringS(name) ?? string.Empty;

    public int GetInteger(Silk.NET.OpenGL.GLEnum pname) => _silkGL.GetInteger(pname);

    public bool IsExtensionPresent(string extension) => _silkGL.IsExtensionPresent(extension);

    // ── IGL: misc ───────────────────────────────────────────────────────────

    public void Clear(ClearBufferMask mask) => _silkGL.Clear(mask);

    public void ClearColor(float red, float green, float blue, float alpha) =>
        _silkGL.ClearColor(red, green, blue, alpha);

    public void ClearDepth(double depth) => _silkGL.ClearDepth(depth);

    public void Viewport(int x, int y, uint width, uint height) =>
        _silkGL.Viewport(x, y, width, height);

    public void Scissor(int x, int y, uint width, uint height) =>
        _silkGL.Scissor(x, y, width, height);

    public void ReadPixels(int x, int y, uint width, uint height, PixelFormat format,
        PixelType type, void* pixels) =>
        _silkGL.ReadPixels(x, y, width, height, format, type, pixels);

    public void FlushQueuedGeometry() => GLManager.OnImmediateGeometryDrawing();
}
