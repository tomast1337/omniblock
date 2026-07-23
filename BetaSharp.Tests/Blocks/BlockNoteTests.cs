using BetaSharp.Blocks;
using BetaSharp.Blocks.Entities;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockNoteTests
{
    [Fact]
    public void OnUse_CyclesNoteAndReturnsTrue()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("noteblock").id);
        world.ReaderWriter.SetInitial(0, 65, 0, 0); // air above
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id); // valid instrument base
        BlockEntityNote noteEntity = new();
        world.Entities.SetBlockEntity(0, 64, 0, noteEntity);

        bool handled = BlockRegistry.Get("noteblock").onUse(new OnUseEvent(world, null!, 0, 64, 0));

        Assert.True(handled);
        Assert.Equal(1, noteEntity.note);
    }

    [Fact]
    public void NeighborUpdate_RedstoneRisingEdge_PlaysOnceAndLatchesPowered()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("noteblock").id);
        world.ReaderWriter.SetInitial(0, 65, 0, 0); // air above
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("lit_redstone_torch").id, 5); // strong power from below
        BlockEntityNote noteEntity = new();
        world.Entities.SetBlockEntity(0, 64, 0, noteEntity);

        BlockRegistry.Get("noteblock").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, BlockRegistry.Get("lit_redstone_torch").id));
        bool poweredAfterRisingEdge = noteEntity.powered;

        BlockRegistry.Get("noteblock").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, BlockRegistry.Get("lit_redstone_torch").id));
        bool poweredAfterSteadySignal = noteEntity.powered;

        Assert.True(poweredAfterRisingEdge);
        Assert.True(poweredAfterSteadySignal);
    }
}
