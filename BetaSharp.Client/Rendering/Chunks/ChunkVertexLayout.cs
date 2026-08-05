using BetaSharp.Client.Rendering.Core;
using GLEnum = BetaSharp.Client.Rendering.Core.OpenGL.GLEnum;

namespace BetaSharp.Client.Rendering.Chunks;

/// <summary>The interleaved <see cref="ChunkVertex" /> layout terrain meshes write.</summary>
/// <remarks>
///     Unlike the Tessellator's generic <see cref="Vertex" /> format, a chunk vertex has no optional
///     attributes — every field is always present, so there is nothing to parameterize. One source of
///     truth for the attribute pointers, the same reason <c>TessellatorVertexLayout</c> exists for the
///     generic format, replacing what used to be wired inline in <c>SubChunkRenderer.UploadMesh</c>.
/// </remarks>
internal static unsafe class ChunkVertexLayout
{
    public const uint Stride = 18;

    private const int PositionOffset = 4;
    private const int UVOffset = 10;
    private const int ColorOffset = 0;
    private const int LightOffset = 14;
    private const int ArrayLayerOffset = 16;

    /// <summary>
    ///     Sets the vertex attribute pointers on the currently bound VAO and buffer. The caller binds
    ///     both first. Locations: 0=position, 1=uv, 2=color, 3=light, 4=array layer — no shader
    ///     declares location 4 yet, so binding it is inert until task #28.
    /// </summary>
    public static void Bind()
    {
        GLManager.GL.EnableVertexAttribArray(0);
        GLManager.GL.VertexAttribPointer(0, 3, GLEnum.Short, false, Stride, (void*)PositionOffset);

        GLManager.GL.EnableVertexAttribArray(1);
        GLManager.GL.VertexAttribIPointer(1, 2, GLEnum.UnsignedShort, Stride, (void*)UVOffset);

        GLManager.GL.EnableVertexAttribArray(2);
        GLManager.GL.VertexAttribPointer(2, 4, GLEnum.UnsignedByte, true, Stride, (void*)ColorOffset);

        // Two channels rather than one packed byte: a smooth-lit corner is a mean of four cells, so a
        // nibble each cannot hold it.
        GLManager.GL.EnableVertexAttribArray(3);
        GLManager.GL.VertexAttribIPointer(3, 2, GLEnum.UnsignedByte, Stride, (void*)LightOffset);

        GLManager.GL.EnableVertexAttribArray(4);
        GLManager.GL.VertexAttribIPointer(4, 1, GLEnum.UnsignedShort, Stride, (void*)ArrayLayerOffset);
    }
}
