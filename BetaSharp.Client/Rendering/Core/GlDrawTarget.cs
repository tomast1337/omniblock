using BetaSharp.Client.Rendering.Core.OpenGL;
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
    public const int Stride = 36;

    private const int PositionOffset = 0;
    private const int TextureOffset = 12;
    private const int ColorOffset = 20;
    private const int NormalOffset = 24;
    private const int ArrayLayerOffset = 28;
    private const int LightOffset = 32;

    /// <summary>
    ///     Sets the vertex attribute pointers on the currently bound VAO and buffer.
    /// </summary>
    /// <remarks>
    ///     The caller binds its own VAO and VBO first, then calls this to configure which offsets
    ///     map to which shader inputs. The same attribute locations every slot's program declares:
    ///     0=position, 1=color, 2=texcoord, 3=normal, 4=array layer. Location 4 is bound
    ///     unconditionally, unlike the other three — the field is always present in <see cref="Vertex" />
    ///     regardless of which optional attributes a given draw used, so there is no "does this draw
    ///     have one" question to ask.
    /// </remarks>
    public static void Bind(IGL gl, VertexChannels channels)
    {
        if (channels.HasFlag(VertexChannels.Texture))
        {
            gl.VertexAttribPointer(2, 2, GLEnum.Float, false, Stride, (void*)TextureOffset);
            gl.EnableVertexAttribArray(2);
        }

        if (channels.HasFlag(VertexChannels.Color))
        {
            gl.VertexAttribPointer(1, 4, GLEnum.UnsignedByte, true, Stride, (void*)ColorOffset);
            gl.EnableVertexAttribArray(1);
        }

        if (channels.HasFlag(VertexChannels.Normal))
        {
            gl.VertexAttribPointer(3, 3, GLEnum.Byte, true, Stride, (void*)NormalOffset);
            gl.EnableVertexAttribArray(3);
        }

        gl.VertexAttribIPointer(4, 1, GLEnum.Int, Stride, (void*)ArrayLayerOffset);
        gl.EnableVertexAttribArray(4);

        // Two channels rather than one packed byte, for the same reason the chunk layout splits
        // them: a smooth-lit corner is a mean of four cells, so a nibble each cannot hold it.
        gl.VertexAttribIPointer(5, 2, GLEnum.UnsignedByte, Stride, (void*)LightOffset);
        gl.EnableVertexAttribArray(5);

        gl.VertexAttribPointer(0, 3, GLEnum.Float, false, Stride, (void*)PositionOffset);
        gl.EnableVertexAttribArray(0);
    }

    public static void Unbind(IGL gl, VertexChannels channels)
    {
        gl.DisableVertexAttribArray(0);
        gl.DisableVertexAttribArray(4);
        gl.DisableVertexAttribArray(5);

        if (channels.HasFlag(VertexChannels.Texture))
        {
            gl.DisableVertexAttribArray(2);
        }

        if (channels.HasFlag(VertexChannels.Color))
        {
            gl.DisableVertexAttribArray(1);
        }

        if (channels.HasFlag(VertexChannels.Normal))
        {
            gl.DisableVertexAttribArray(3);
        }
    }
}

/// <summary>
///     Draws <see cref="DrawCommand" />s with OpenGL.
/// </summary>
/// <remarks>
///     Owns the ring of streaming buffers a submission rotates through. Reusing one buffer would
///     stall on the driver still reading last frame's contents out of it; ten is enough that the
///     one being written is never the one in flight.
/// </remarks>
public sealed class GlDrawTarget : IDrawTarget
{
    private const int RingSize = 10;

    private readonly uint[] _vbos = new uint[RingSize];
    private readonly uint _vao;
    private int _ringIndex;

    public GlDrawTarget()
    {
        IGL gl = GLManager.GL;
        gl.GenBuffers((uint)RingSize, _vbos);
        _vao = gl.GenVertexArray();
    }

    public unsafe void Submit(in DrawCommand command)
    {
        if (command.VertexCount == 0)
        {
            return;
        }

        IGL gl = GLManager.GL;

        // Before anything of ours is bound, because draining binds and unbinds its own.
        gl.FlushQueuedGeometry();

        _ringIndex = (_ringIndex + 1) % RingSize;
        gl.BindBuffer(GLEnum.ArrayBuffer, _vbos[_ringIndex]);

        fixed (byte* ptr = command.Vertices)
        {
            gl.BufferData(GLEnum.ArrayBuffer, (nuint)command.Vertices.Length, ptr, GLEnum.StreamDraw);
        }

        gl.BindVertexArray(_vao);
        TessellatorVertexLayout.Bind(gl, command.Channels);

        ISlotProgram program = Resolve(command.Slot);
        program.Activate();
        gl.DrawArrays(ToDrawMode(command.Topology), 0, (uint)command.VertexCount);
        program.Deactivate();

        TessellatorVertexLayout.Unbind(gl, command.Channels);
        gl.BindVertexArray(0);
    }

    public unsafe IStaticMesh Capture(in DrawCommand command)
    {
        IGL gl = GLManager.GL;

        uint buffer = gl.GenBuffer();
        gl.BindBuffer(GLEnum.ArrayBuffer, buffer);

        fixed (byte* ptr = command.Vertices)
        {
            gl.BufferData(GLEnum.ArrayBuffer, (nuint)command.Vertices.Length, ptr, GLEnum.StaticDraw);
        }

        return new GlStaticMesh(buffer, command.VertexCount, ToDrawMode(command.Topology), command.Channels);
    }

    /// <summary>The program a command's slot names, or the caller's own if it named none.</summary>
    internal static ISlotProgram Resolve(ProgramSlot? slot) => slot is { } named
        ? SlotPrograms.Resolve(named, VertexLayoutKind.Generic)
        : SlotPrograms.CallerBound;

    internal static GLEnum ToDrawMode(DrawTopology topology) => topology switch
    {
        DrawTopology.Points => GLEnum.Points,
        DrawTopology.Lines => GLEnum.Lines,
        DrawTopology.LineStrip => GLEnum.LineStrip,
        DrawTopology.Triangles => GLEnum.Triangles,
        DrawTopology.TriangleStrip => GLEnum.TriangleStrip,
        DrawTopology.TriangleFan => GLEnum.TriangleFan,
        _ => throw new ArgumentOutOfRangeException(nameof(topology), topology, "Unhandled topology."),
    };
}

/// <inheritdoc cref="IStaticMesh" />
internal sealed class GlStaticMesh(
    uint buffer,
    int vertexCount,
    GLEnum drawMode,
    VertexChannels channels) : IStaticMesh
{
    private uint _buffer = buffer;
    private uint _vao;

    public void DrawWithBoundProgram() => Draw(null);

    public void Draw(ProgramSlot slot) => Draw((ProgramSlot?)slot);

    private void Draw(ProgramSlot? slot)
    {
        if (vertexCount == 0 || _buffer == 0)
        {
            return;
        }

        IGL gl = GLManager.GL;

        // Before the VAO below is bound, for the reason FlushQueuedGeometry gives.
        gl.FlushQueuedGeometry();

        if (_vao == 0)
        {
            _vao = gl.GenVertexArray();
            gl.BindVertexArray(_vao);
            gl.BindBuffer(GLEnum.ArrayBuffer, _buffer);
            TessellatorVertexLayout.Bind(gl, channels);
            gl.BindVertexArray(0);
        }

        gl.BindVertexArray(_vao);

        ISlotProgram program = GlDrawTarget.Resolve(slot);
        program.Activate();
        gl.DrawArrays(drawMode, 0, (uint)vertexCount);
        program.Deactivate();

        gl.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_vao != 0)
        {
            GLManager.GL.DeleteVertexArray(_vao);
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
