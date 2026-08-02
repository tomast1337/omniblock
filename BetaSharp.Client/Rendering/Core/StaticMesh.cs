using Silk.NET.OpenGL;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Core;

/// <summary>
///     The interleaved vertex layout <see cref="Tessellator" /> writes.
/// </summary>
/// <remarks>
///     Shared rather than spelled out at each draw site, because a reader that binds different
///     offsets than the writer produced does not fail — it draws garbage. Anything that submits
///     Tessellator output goes through here.
/// </remarks>
internal static unsafe class TessellatorVertexLayout
{
    public const int Stride = 32;

    private const int PositionOffset = 0;
    private const int TextureOffset = 12;
    private const int ColorOffset = 20;
    private const int NormalOffset = 24;

    public static void Bind(bool hasTexture, bool hasColor, bool hasNormals)
    {
        if (hasTexture)
        {
            GLManager.GL.TexCoordPointer(2, GLEnum.Float, Stride, (void*)TextureOffset);
            GLManager.GL.EnableClientState(GLEnum.TextureCoordArray);
        }

        if (hasColor)
        {
            GLManager.GL.ColorPointer(4, ColorPointerType.UnsignedByte, Stride, (void*)ColorOffset);
            GLManager.GL.EnableClientState(GLEnum.ColorArray);
        }

        if (hasNormals)
        {
            GLManager.GL.NormalPointer(NormalPointerType.Byte, Stride, (void*)NormalOffset);
            GLManager.GL.EnableClientState(GLEnum.NormalArray);
        }

        GLManager.GL.VertexPointer(3, GLEnum.Float, Stride, (void*)PositionOffset);
        GLManager.GL.EnableClientState(GLEnum.VertexArray);
    }

    public static void Unbind(bool hasTexture, bool hasColor, bool hasNormals)
    {
        GLManager.GL.DisableClientState(GLEnum.VertexArray);

        if (hasTexture)
        {
            GLManager.GL.DisableClientState(GLEnum.TextureCoordArray);
        }

        if (hasColor)
        {
            GLManager.GL.DisableClientState(GLEnum.ColorArray);
        }

        if (hasNormals)
        {
            GLManager.GL.DisableClientState(GLEnum.NormalArray);
        }
    }
}

/// <summary>
///     Geometry built once and drawn many times, held in a buffer of its own.
/// </summary>
/// <remarks>
///     <para>
///         Replaces what a display list was used for. The two are equivalent for this purpose: a
///         list recorded the vertex submission and replayed it, and the transform was applied by
///         whoever called it rather than being captured, so holding the vertices in a buffer and
///         re-submitting them draws the same thing under the same matrices.
///     </para>
///     <para>
///         They are not equivalent in general — a list could record arbitrary state changes — but
///         nothing here recorded any. Display lists have no counterpart in GL core profiles or in
///         WebGPU, so this had to stop being the mechanism either way.
///     </para>
/// </remarks>
public sealed class StaticMesh(
    uint buffer,
    int vertexCount,
    GLEnum drawMode,
    bool hasTexture,
    bool hasColor,
    bool hasNormals) : IDisposable
{
    private uint _buffer = buffer;

    public void Draw()
    {
        if (vertexCount == 0 || _buffer == 0)
        {
            return;
        }

        GLManager.GL.BindBuffer(GLEnum.ArrayBuffer, _buffer);
        TessellatorVertexLayout.Bind(hasTexture, hasColor, hasNormals);
        GLManager.GL.DrawArrays(drawMode, 0, (uint)vertexCount);
        TessellatorVertexLayout.Unbind(hasTexture, hasColor, hasNormals);
    }

    public void Dispose()
    {
        if (_buffer == 0)
        {
            return;
        }

        GLManager.GL.DeleteBuffer(_buffer);
        _buffer = 0;
    }
}
