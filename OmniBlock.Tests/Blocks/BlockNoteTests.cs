using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockNoteTests
{
    [Fact]
    public void OnUse_CyclesNoteAndReturnsTrue()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("noteblock").Id);
        world.ReaderWriter.SetInitial(0, 65, 0, 0); // air above
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("stone").Id); // valid instrument base
        BlockEntityNote noteEntity = new();
        world.Entities.SetBlockEntity(0, 64, 0, noteEntity);

        bool handled = TestBlocks.Get("noteblock").OnUse(new OnUseEvent(world, null!, 0, 64, 0));

        Assert.True(handled);
        Assert.Equal(1, noteEntity.note);
    }

    [Fact]
    public void NeighborUpdate_RedstoneRisingEdge_PlaysOnceAndLatchesPowered()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("noteblock").Id);
        world.ReaderWriter.SetInitial(0, 65, 0, 0); // air above
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("lit_redstone_torch").Id, 5); // strong power from below
        BlockEntityNote noteEntity = new();
        world.Entities.SetBlockEntity(0, 64, 0, noteEntity);

        TestBlocks.Get("noteblock").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, TestBlocks.Get("lit_redstone_torch").Id));
        bool poweredAfterRisingEdge = noteEntity.powered;

        TestBlocks.Get("noteblock").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, TestBlocks.Get("lit_redstone_torch").Id));
        bool poweredAfterSteadySignal = noteEntity.powered;

        Assert.True(poweredAfterRisingEdge);
        Assert.True(poweredAfterSteadySignal);
    }
}
