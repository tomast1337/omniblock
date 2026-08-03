using Silk.NET.OpenGL;

namespace BetaSharp.Client.Rendering.Core.OpenGL;

public abstract unsafe class LegacyGL : IGL
{
    public GL SilkGL { get; }

    public LegacyGL(GL gl)
    {
        SilkGL = gl;
    }

    public void AttachShader(uint program, uint shader)
    {
        SilkGL.AttachShader(program, shader);
    }

    public void BindBuffer(GLEnum target, uint buffer)
    {
        SilkGL.BindBuffer(target.ToModern(), buffer);
    }

    /// <summary>
    /// The texture currently bound to <see cref="GLEnum.Texture2D"/> on unit 0, so a renderer that
    /// binds its own texture out-of-band can put back what the caller had.
    /// </summary>
    public uint BoundTexture2D { get; private set; }

    public void BindTexture(GLEnum target, uint texture)
    {
        if (target == GLEnum.Texture2D)
        {
            BoundTexture2D = texture;
        }

        SilkGL.BindTexture(target.ToModern(), texture);
    }

    public void BindVertexArray(uint array)
    {
        SilkGL.BindVertexArray(array);
    }

    /// <summary>
    /// Raised just before a change to state that governs how geometry rasterizes. A renderer that
    /// queues geometry instead of drawing it immediately subscribes here, so what it is holding
    /// reaches the framebuffer while the state it was queued under is still the state in force.
    /// </summary>
    public event Action? RasterStateChanging;

    protected void OnRasterStateChanging() => RasterStateChanging?.Invoke();

    /// <summary>Fires only for the caps whose toggle changes how queued geometry would rasterize.</summary>
    protected void OnRasterStateChanging(GLEnum cap)
    {
        if (cap is GLEnum.Blend or GLEnum.AlphaTest or GLEnum.DepthTest or GLEnum.Fog)
        {
            RasterStateChanging?.Invoke();
        }
    }

    /// <summary>
    /// Raised just before geometry is drawn through this interface, which is to say drawn
    /// immediately rather than queued. A renderer holding queued geometry subscribes here so what
    /// it is holding reaches the depth buffer first, in the order the caller issued it.
    /// </summary>
    /// <remarks>
    /// The batching renderers issue their own draws straight through Silk, below this, so a flush
    /// raised from here does not re-enter.
    /// </remarks>
    public event Action? ImmediateGeometryDrawing;

    protected void OnImmediateGeometryDrawing() => ImmediateGeometryDrawing?.Invoke();

    public void BlendFunc(GLEnum sfactor, GLEnum dfactor)
    {
        OnRasterStateChanging();
        SilkGL.BlendFunc(sfactor.ToModern(), dfactor.ToModern());
    }

    public void BufferData<T0>(GLEnum target, ReadOnlySpan<T0> data, GLEnum usage) where T0 : unmanaged
    {
        SilkGL.BufferData<T0>(target.ToModern(), data, usage.ToModern());
    }

    public abstract void BufferData(GLEnum target, nuint size, void* data, GLEnum usage);



    public void Clear(ClearBufferMask mask)
    {
        SilkGL.Clear(mask);
    }

    public void ClearColor(float red, float green, float blue, float alpha)
    {
        SilkGL.ClearColor(red, green, blue, alpha);
    }

    public void ClearDepth(double depth)
    {
        SilkGL.ClearDepth(depth);
    }

    public abstract void Color3(float red, float green, float blue);

    public abstract void Color3(byte red, byte green, byte blue);

    public abstract void Color4(float red, float green, float blue, float alpha);

    public void ColorMask(bool red, bool green, bool blue, bool alpha)
    {
        SilkGL.ColorMask(red, green, blue, alpha);
    }

    public abstract void ColorMaterial(GLEnum face, GLEnum mode);

    public void CompileShader(uint shader)
    {
        SilkGL.CompileShader(shader);
    }

    public uint CreateProgram()
    {
        return SilkGL.CreateProgram();
    }

    public uint CreateShader(ShaderType type)
    {
        return SilkGL.CreateShader(type);
    }

    public void CullFace(GLEnum mode)
    {
        SilkGL.CullFace(mode.ToModern());
    }

    public void DeleteBuffer(uint buffer)
    {
        SilkGL.DeleteBuffer(buffer);
    }


    public void DeleteProgram(uint program)
    {
        SilkGL.DeleteProgram(program);
    }

    public void DeleteShader(uint shader)
    {
        SilkGL.DeleteShader(shader);
    }

    public void DeleteTexture(uint texture)
    {
        SilkGL.DeleteTexture(texture);
    }

    public void DeleteTextures(uint n, ReadOnlySpan<uint> textures)
    {
        SilkGL.DeleteTextures(n, textures);
    }

    public void DeleteTextures(ReadOnlySpan<uint> textures)
    {
        SilkGL.DeleteTextures(textures);
    }

    public void DeleteVertexArray(uint array)
    {
        SilkGL.DeleteVertexArray(array);
    }

    public void DepthFunc(GLEnum func)
    {
        OnRasterStateChanging();
        SilkGL.DepthFunc(func.ToModern());
    }

    public void DepthMask(bool flag)
    {
        OnRasterStateChanging();
        SilkGL.DepthMask(flag);
    }

    public virtual void Disable(EnableCap cap)
    {
        OnRasterStateChanging((GLEnum)cap);
        SilkGL.Disable(cap);
    }

    public virtual void Disable(GLEnum cap)
    {
        OnRasterStateChanging(cap);
        SilkGL.Disable(cap.ToModern());
    }

    public abstract void DrawArrays(GLEnum mode, int first, uint count);

    public abstract void Enable(GLEnum cap);

    public virtual void EnableVertexAttribArray(uint index)
    {
        SilkGL.EnableVertexAttribArray(index);
    }

    public uint GenBuffer()
    {
        return SilkGL.GenBuffer();
    }

    public void GenBuffers(uint n, Span<uint> buffers)
    {
        SilkGL.GenBuffers(n, buffers);
    }

    public void GenBuffers(Span<uint> buffers)
    {
        SilkGL.GenBuffers(buffers);
    }


    public uint GenTexture()
    {
        return SilkGL.GenTexture();
    }

    public void GenTextures(Span<uint> textures)
    {
        SilkGL.GenTextures(textures);
    }

    public uint GenVertexArray()
    {
        return SilkGL.GenVertexArray();
    }

    public GLEnum GetError()
    {
        return (GLEnum)SilkGL.GetError();
    }

    public virtual void GetFloat(GLEnum pname, Span<float> data)
    {
        SilkGL.GetFloat(pname.ToModern(), data);
    }

    public virtual void GetFloat(GLEnum pname, out float data)
    {
        fixed (float* ptr = &data) { SilkGL.GetFloat(pname.ToModern(), ptr); }
    }

    public virtual void GetFloat(GLEnum pname, float* data)
    {
        SilkGL.GetFloat(pname.ToModern(), data);
    }

    public void GetProgram(uint program, ProgramPropertyARB pname, out int params_)
    {
        SilkGL.GetProgram(program, pname, out params_);
    }

    public string GetProgramInfoLog(uint program)
    {
        return SilkGL.GetProgramInfoLog(program);
    }

    public void GetShader(uint shader, ShaderParameterName pname, out int params_)
    {
        SilkGL.GetShader(shader, pname, out params_);
    }

    public string GetShaderInfoLog(uint shader)
    {
        return SilkGL.GetShaderInfoLog(shader);
    }

    public int GetUniformLocation(uint program, string name)
    {
        return SilkGL.GetUniformLocation(program, name);
    }

    public bool IsExtensionPresent(string extension)
    {
        return SilkGL.IsExtensionPresent(extension);
    }

    public abstract void Light(GLEnum light, GLEnum pname, float* params_);

    public abstract void LightModel(GLEnum pname, float* params_);

    public virtual void LineWidth(float width)
    {
        SilkGL.LineWidth(width);
    }

    public void LinkProgram(uint program)
    {
        SilkGL.LinkProgram(program);
    }

    public abstract void Normal3(float nx, float ny, float nz);


    public void PixelStore(PixelStoreParameter pname, int param)
    {
        SilkGL.PixelStore(pname, param);
    }

    public void PolygonOffset(float factor, float units)
    {
        SilkGL.PolygonOffset(factor, units);
    }

    public void ReadPixels(int x, int y, uint width, uint height, PixelFormat format, PixelType type, void* pixels)
    {
        SilkGL.ReadPixels(x, y, width, height, format, type, pixels);
    }

    public abstract void ShadeModel(GLEnum mode);

    public void ShaderSource(uint shader, string string_)
    {
        SilkGL.ShaderSource(shader, string_);
    }


    public void TexImage2D(TextureTarget target, int level, InternalFormat internalformat, uint width, uint height, int border, PixelFormat format, PixelType type, void* pixels)
    {
        SilkGL.TexImage2D(target, level, internalformat, width, height, border, format, type, pixels);
    }

    public void TexImage2D(GLEnum target, int level, int internalformat, uint width, uint height, int border, GLEnum format, GLEnum type, void* pixels)
    {
        SilkGL.TexImage2D(target.ToModern(), level, internalformat, width, height, border, format.ToModern(), type.ToModern(), pixels);
    }

    public void TexParameter(TextureTarget target, TextureParameterName pname, int param)
    {
        SilkGL.TexParameter(target, pname, param);
    }

    public void TexParameter(GLEnum target, GLEnum pname, int param)
    {
        SilkGL.TexParameter(target.ToModern(), pname.ToModern(), param);
    }

    public void TexParameter(GLEnum target, GLEnum pname, float param)
    {
        SilkGL.TexParameter(target.ToModern(), pname.ToModern(), param);
    }

    public void TexSubImage2D(GLEnum target, int level, int xoffset, int yoffset, uint width, uint height, GLEnum format, GLEnum type, void* pixels)
    {
        SilkGL.TexSubImage2D(target.ToModern(), level, xoffset, yoffset, width, height, format.ToModern(), type.ToModern(), pixels);
    }

    public void Uniform1(int location, int v0)
    {
        SilkGL.Uniform1(location, v0);
    }

    public void Uniform1(int location, float v0)
    {
        SilkGL.Uniform1(location, v0);
    }

    public void Uniform2(int location, float v0, float v1)
    {
        SilkGL.Uniform2(location, v0, v1);
    }

    public void Uniform3(int location, float v0, float v1, float v2)
    {
        SilkGL.Uniform3(location, v0, v1, v2);
    }

    public void Uniform4(int location, float v0, float v1, float v2, float v3)
    {
        SilkGL.Uniform4(location, v0, v1, v2, v3);
    }

    public void UniformMatrix4(int location, uint count, bool transpose, float* value)
    {
        SilkGL.UniformMatrix4(location, count, transpose, value);
    }

    public virtual void UseProgram(uint program)
    {
        SilkGL.UseProgram(program);
    }

    public void VertexAttribIPointer(uint index, int size, GLEnum type, uint stride, void* pointer)
    {
        SilkGL.VertexAttribIPointer(index, size, type.ToModern(), stride, pointer);
    }

    public void VertexAttribPointer(uint index, int size, GLEnum type, bool normalized, uint stride, void* pointer)
    {
        SilkGL.VertexAttribPointer(index, size, type.ToModern(), normalized, stride, pointer);
    }

    public void Viewport(int x, int y, uint width, uint height)
    {
        SilkGL.Viewport(x, y, width, height);
    }

    public void Scissor(int x, int y, uint width, uint height)
    {
        SilkGL.Scissor(x, y, width, height);
    }

    public uint GenFramebuffer() => SilkGL.GenFramebuffer();
    public void BindFramebuffer(FramebufferTarget target, uint framebuffer) => SilkGL.BindFramebuffer(target, framebuffer);
    public void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment attachment, TextureTarget textarget, uint texture, int level) => SilkGL.FramebufferTexture2D(target, attachment, textarget, texture, level);
    public void GenRenderbuffers(Span<uint> renderbuffers) => SilkGL.GenRenderbuffers(renderbuffers);
    public void BindRenderbuffer(RenderbufferTarget target, uint renderbuffer) => SilkGL.BindRenderbuffer(target, renderbuffer);
    public void RenderbufferStorage(RenderbufferTarget target, InternalFormat internalformat, uint width, uint height) => SilkGL.RenderbufferStorage(target, internalformat, width, height);
    public void FramebufferRenderbuffer(FramebufferTarget target, FramebufferAttachment attachment, RenderbufferTarget renderbuffertarget, uint renderbuffer) => SilkGL.FramebufferRenderbuffer(target, attachment, renderbuffertarget, renderbuffer);
    public Silk.NET.OpenGL.GLEnum CheckFramebufferStatus(FramebufferTarget target) => SilkGL.CheckFramebufferStatus(target);
    public void DeleteFramebuffer(uint framebuffer) => SilkGL.DeleteFramebuffer(framebuffer);
    public void DeleteRenderbuffer(uint renderbuffer) => SilkGL.DeleteRenderbuffer(renderbuffer);
    public void BlitFramebuffer(int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0, int dstX1, int dstY1, uint mask, BlitFramebufferFilter filter) => SilkGL.BlitFramebuffer(srcX0, srcY0, srcX1, srcY1, dstX0, dstY0, dstX1, dstY1, mask, filter);

    public void ActiveTexture(GLEnum texture) => SilkGL.ActiveTexture((TextureUnit)texture.ToModern());

}
