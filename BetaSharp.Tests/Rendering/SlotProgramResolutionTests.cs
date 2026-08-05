using BetaSharp.Client.Rendering.Core;

namespace BetaSharp.Tests.Rendering;

/// <summary>
///     Resolution only — no GL. <see cref="SlotPrograms.Register" /> takes any
///     <see cref="ISlotProgram" />, and these register fakes rather than the real programs, which
///     would need a context to compile.
/// </summary>
public sealed class SlotProgramResolutionTests : IDisposable
{
    private sealed class FakeProgram(VertexLayoutKind layout) : ISlotProgram
    {
        public VertexLayoutKind VertexLayout => layout;
        public void Activate() { }
        public void Deactivate() { }
    }

    public void Dispose() => SlotPrograms.Dispose();

    /// <summary>
    ///     Every block-shaped slot names terrain as its parent, and terrain's program reads packed
    ///     chunk vertices. A block entity is drawn from the Tessellator, so taking that program would
    ///     read positions out of the bytes holding colours — which is what made a moving piston
    ///     disappear rather than draw wrongly.
    /// </summary>
    [Fact]
    public void A_generic_draw_skips_past_a_chunk_only_ancestor()
    {
        FakeProgram terrain = new(VertexLayoutKind.Chunk);
        FakeProgram texturedLit = new(VertexLayoutKind.Generic);

        SlotPrograms.Register(ProgramSlot.Terrain, terrain);
        SlotPrograms.Register(ProgramSlot.TexturedLit, texturedLit);

        Assert.Same(texturedLit, SlotPrograms.Resolve(ProgramSlot.BlockEntity, VertexLayoutKind.Generic));
        Assert.Same(texturedLit, SlotPrograms.Resolve(ProgramSlot.DamagedBlock, VertexLayoutKind.Generic));
        Assert.Same(terrain, SlotPrograms.Resolve(ProgramSlot.Terrain, VertexLayoutKind.Chunk));
    }

    [Fact]
    public void A_layout_no_ancestor_can_read_throws_rather_than_drawing()
    {
        SlotPrograms.Register(ProgramSlot.Basic, new FakeProgram(VertexLayoutKind.Generic));

        Assert.Throws<InvalidOperationException>(
            () => SlotPrograms.Resolve(ProgramSlot.BlockEntity, VertexLayoutKind.Chunk));
    }
}
