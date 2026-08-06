using Silk.NET.OpenGL;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Core.WebGPU;

/// <summary>
///     A silent no-op IGL so startup and tick paths do not crash before the native WebGPU
///     equivalents are wired. Every method returns zero/empty/false without side effects.
/// </summary>
/// <remarks>
///     This is not a GL emulator — it exists solely so the game initialises far enough for
///     the WebGPU render pass to take over. Drawing through this does nothing visible.
/// </remarks>
internal sealed unsafe class WebGpuStubGL : IGL
{
    private static uint s_nextBuffer;
    private static uint s_nextTexture;
    private static uint s_nextVertexArray;
    private static uint s_nextFramebuffer;
    private static uint s_nextProgram;
    private static uint s_nextShader;

    public void AttachShader(uint program, uint shader) { }
    public void BindBuffer(GLEnum target, uint buffer) { }
    public void BindTexture(GLEnum target, uint texture) { }
    public void BindVertexArray(uint array) { }
    public void BlendFunc(GLEnum sfactor, GLEnum dfactor) { }
    public void BufferData<T0>(GLEnum target, ReadOnlySpan<T0> data, GLEnum usage) where T0 : unmanaged { }
    public void BufferData(GLEnum target, nuint size, void* data, GLEnum usage) { }
    public void BufferSubData<T>(GLEnum target, nint offset, ReadOnlySpan<T> data) where T : unmanaged { }
    public void BindBufferBase(BufferTargetARB target, uint index, uint buffer) { }
    public void Clear(ClearBufferMask mask) { }
    public void ClearColor(float red, float green, float blue, float alpha) { }
    public void ClearDepth(double depth) { }
    public void ColorMask(bool red, bool green, bool blue, bool alpha) { }
    public void CompileShader(uint shader) { }
    public uint CreateProgram() => ++s_nextProgram;
    public uint CreateShader(ShaderType type) => ++s_nextShader;
    public void CullFace(GLEnum mode) { }
    public void DeleteBuffer(uint buffer) { }
    public void DeleteProgram(uint program) { }
    public void DeleteShader(uint shader) { }
    public void DeleteTexture(uint texture) { }
    public void DeleteTextures(uint n, ReadOnlySpan<uint> textures) { }
    public void DeleteTextures(ReadOnlySpan<uint> textures) { }
    public void DeleteVertexArray(uint array) { }
    public void DepthFunc(GLEnum func) { }
    public void DepthMask(bool flag) { }
    public void Disable(EnableCap cap) { }
    public void Disable(GLEnum cap) { }
    public void Enable(GLEnum cap) { }
    public void DrawArrays(GLEnum mode, int first, uint count) { }
    public void DrawArraysInstanced(GLEnum mode, int first, uint count, uint instanceCount) { }
    public void EnableVertexAttribArray(uint index) { }
    public void DisableVertexAttribArray(uint index) { }
    public uint GenBuffer() => ++s_nextBuffer;
    public void GenBuffers(uint n, Span<uint> buffers)
    {
        for (int i = 0; i < n; i++) buffers[i] = ++s_nextBuffer;
    }
    public void GenBuffers(Span<uint> buffers)
    {
        for (int i = 0; i < buffers.Length; i++) buffers[i] = ++s_nextBuffer;
    }
    public uint GenTexture() => ++s_nextTexture;
    public void GenTextures(Span<uint> textures)
    {
        for (int i = 0; i < textures.Length; i++) textures[i] = ++s_nextTexture;
    }
    public uint GenVertexArray() => ++s_nextVertexArray;
    public uint GenFramebuffer() => ++s_nextFramebuffer;
    public GLEnum GetError() => 0;
    public void GetFloat(GLEnum pname, Span<float> data) { }
    public void GetFloat(GLEnum pname, out float data) { data = 0; }
    public void GetFloat(GLEnum pname, float* data) { }
    public void GetProgram(uint program, ProgramPropertyARB pname, out int params_) { params_ = 0; }
    public string GetProgramInfoLog(uint program) => string.Empty;
    public void GetShader(uint shader, ShaderParameterName pname, out int params_) { params_ = 1; }
    public string GetShaderInfoLog(uint shader) => string.Empty;
    public int GetUniformLocation(uint program, string name) => -1;
    public bool IsExtensionPresent(string extension) => false;
    public void LineWidth(float width) { }
    public void LinkProgram(uint program) { }
    public void PixelStore(PixelStoreParameter pname, int param) { }
    public void PolygonOffset(float factor, float units) { }
    public void ReadPixels(int x, int y, uint width, uint height, PixelFormat format, PixelType type, void* pixels) { }
    public void ShaderSource(uint shader, string string_) { }
    public void TexImage2D(TextureTarget target, int level, InternalFormat internalformat, uint width, uint height, int border, PixelFormat format, PixelType type, void* pixels) { }
    public void TexImage2D(GLEnum target, int level, int internalformat, uint width, uint height, int border, GLEnum format, GLEnum type, void* pixels) { }
    public void TexImage3D(GLEnum target, int level, int internalformat, uint width, uint height, uint depth, int border, GLEnum format, GLEnum type, void* pixels) { }
    public void TexParameter(TextureTarget target, TextureParameterName pname, int param) { }
    public void TexParameter(GLEnum target, GLEnum pname, int param) { }
    public void TexParameter(GLEnum target, GLEnum pname, float param) { }
    public void TexSubImage2D(GLEnum target, int level, int xoffset, int yoffset, uint width, uint height, GLEnum format, GLEnum type, void* pixels) { }
    public void TexSubImage3D(GLEnum target, int level, int xoffset, int yoffset, int zoffset, uint width, uint height, uint depth, GLEnum format, GLEnum type, void* pixels) { }
    public void Uniform1(int location, int v0) { }
    public void Uniform1(int location, float v0) { }
    public void Uniform2(int location, float v0, float v1) { }
    public void Uniform3(int location, float v0, float v1, float v2) { }
    public void Uniform4(int location, float v0, float v1, float v2, float v3) { }
    public void UniformMatrix3(int location, uint count, bool transpose, float* value) { }
    public void UniformMatrix4(int location, uint count, bool transpose, float* value) { }
    public void UseProgram(uint program) { }
    public void VertexAttribIPointer(uint index, int size, GLEnum type, uint stride, void* pointer) { }
    public void VertexAttribPointer(uint index, int size, GLEnum type, bool normalized, uint stride, void* pointer) { }
    public void Viewport(int x, int y, uint width, uint height) { }
    public void Scissor(int x, int y, uint width, uint height) { }
    public void BindFramebuffer(FramebufferTarget target, uint framebuffer) { }
    public void FramebufferTexture2D(FramebufferTarget target, FramebufferAttachment attachment, TextureTarget textarget, uint texture, int level) { }
    public void GenRenderbuffers(Span<uint> renderbuffers) { }
    public void BindRenderbuffer(RenderbufferTarget target, uint renderbuffer) { }
    public void RenderbufferStorage(RenderbufferTarget target, InternalFormat internalformat, uint width, uint height) { }
    public void FramebufferRenderbuffer(FramebufferTarget target, FramebufferAttachment attachment, RenderbufferTarget renderbuffertarget, uint renderbuffer) { }
    public Silk.NET.OpenGL.GLEnum CheckFramebufferStatus(FramebufferTarget target) => Silk.NET.OpenGL.GLEnum.FramebufferComplete;
    public void DeleteFramebuffer(uint framebuffer) { }
    public void DeleteRenderbuffer(uint renderbuffer) { }
    public void BlitFramebuffer(int srcX0, int srcY0, int srcX1, int srcY1, int dstX0, int dstY0, int dstX1, int dstY1, uint mask, BlitFramebufferFilter filter) { }
    public void ActiveTexture(GLEnum texture) { }
    public void FlushQueuedGeometry() { }
    public uint BoundTexture2D => 0;
    public string GetString(StringName name) => string.Empty;
    public int GetInteger(Silk.NET.OpenGL.GLEnum pname) => 0;
}
