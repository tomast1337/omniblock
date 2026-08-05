using BetaSharp.Client.Rendering.Core.OpenGL;
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
    private const int ArrayLayerOffset = 28;

    /// <summary>
    ///     Sets the vertex attribute pointers on the currently bound VAO and buffer.
    /// </summary>
    /// <remarks>
    ///     The caller binds its own VAO and VBO first, then calls this to configure which offsets
    ///     map to which shader inputs. The same attribute locations every slot's program declares:
    ///     0=position, 1=color, 2=texcoord, 3=normal, 4=array layer. Location 4 is bound
    ///     unconditionally, unlike the other three — the field is always present in <see cref="Vertex" />
    ///     regardless of which optional attributes a given draw used, so there is no "does this draw
    ///     have one" question to ask. No current shader declares location 4, so this is inert until
    ///     task #28 adds it.
    /// </remarks>
    public static void Bind(GL gl, bool hasTexture, bool hasColor, bool hasNormals)
    {
        if (hasTexture)
        {
            gl.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, Stride, (void*)TextureOffset);
            gl.EnableVertexAttribArray(2);
        }

        if (hasColor)
        {
            gl.VertexAttribPointer(1, 4, VertexAttribPointerType.UnsignedByte, true, Stride, (void*)ColorOffset);
            gl.EnableVertexAttribArray(1);
        }

        if (hasNormals)
        {
            gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Byte, true, Stride, (void*)NormalOffset);
            gl.EnableVertexAttribArray(3);
        }

        gl.VertexAttribIPointer(4, 1, VertexAttribIType.Int, Stride, (void*)ArrayLayerOffset);
        gl.EnableVertexAttribArray(4);

        gl.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, Stride, (void*)PositionOffset);
        gl.EnableVertexAttribArray(0);
    }

    public static void Unbind(GL gl, bool hasTexture, bool hasColor, bool hasNormals)
    {
        gl.DisableVertexAttribArray(0);
        gl.DisableVertexAttribArray(4);

        if (hasTexture)
        {
            gl.DisableVertexAttribArray(2);
        }

        if (hasColor)
        {
            gl.DisableVertexAttribArray(1);
        }

        if (hasNormals)
        {
            gl.DisableVertexAttribArray(3);
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
    private uint _vao;

    /// <inheritdoc cref="Tessellator.drawWithBoundProgram" />
    public void DrawWithBoundProgram() => Draw(SlotPrograms.CallerBound);

    /// <inheritdoc cref="Tessellator.draw(ProgramSlot)" />
    public void Draw(ProgramSlot slot) => Draw(SlotPrograms.Resolve(slot));

    private void Draw(ISlotProgram program)
    {
        if (vertexCount == 0 || _buffer == 0)
        {
            return;
        }

        // Before the VAO below is bound, for the reason FlushQueuedGeometry gives.
        ((LegacyGL)GLManager.GL).FlushQueuedGeometry();

        GL gl = ((LegacyGL)GLManager.GL).SilkGL;

        if (_vao == 0)
        {
            _vao = gl.GenVertexArray();
            gl.BindVertexArray(_vao);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, _buffer);
            TessellatorVertexLayout.Bind(gl, hasTexture, hasColor, hasNormals);
            gl.BindVertexArray(0);
        }

        gl.BindVertexArray(_vao);
        program.Activate();
        GLManager.GL.DrawArrays(drawMode, 0, (uint)vertexCount);
        program.Deactivate();
        gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_vao != 0)
        {
            ((LegacyGL)GLManager.GL).SilkGL.DeleteVertexArray(_vao);
            _vao = 0;
        }

        if (_buffer == 0)
        {
            return;
        }

        GLManager.GL.DeleteBuffer(_buffer);
        _buffer = 0;
    }
}
