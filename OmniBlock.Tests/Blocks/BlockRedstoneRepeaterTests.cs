using OmniBlock.Blocks;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockRedstoneRepeaterTests
{
    private static OnTickEvent RepeaterEvent(FakeWorldContext world, int x = 0, int y = 64, int z = 0)
    {
        var meta = world.Reader.GetBlockMeta(x, y, z);
        var blockId = world.Reader.GetBlockId(x, y, z);
        return new OnTickEvent(world, x, y, z, meta, blockId);
    }

    [Fact]
    public void NeighborUpdate_UnlitRepeater_WhenPowered_SchedulesTickUsingDelaySetting()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("repeater").Id, 4); // facing=0, delay index=1 => 4 ticks
        world.ReaderWriter.SetInitial(0, 64, 1, TestBlocks.Get("redstone_wire").Id, 15); // power input for facing=0

        TestBlocks.Get("repeater").NeighborUpdate(RepeaterEvent(world));

        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t is { X: 0, Y: 64, Z: 0 } && t.BlockId == TestBlocks.Get("repeater").Id && t.TickRate == 4);
    }

    [Fact]
    public void NeighborUpdate_LitRepeater_WhenUnpowered_SchedulesTickUsingDelaySetting()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("powered_repeater").Id, 8); // facing=0, delay index=2 => 6 ticks

        TestBlocks.Get("powered_repeater").NeighborUpdate(RepeaterEvent(world));

        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t is { X: 0, Y: 64, Z: 0 } && t.BlockId == TestBlocks.Get("powered_repeater").Id && t.TickRate == 6);
    }

    [Fact]
    public void NeighborUpdate_WithoutSupportingBlock_BreaksRepeaterIntoAir()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("repeater").Id);
        // No supporting block at (0,63,0) => ShouldSuffocate false.

        TestBlocks.Get("repeater").NeighborUpdate(RepeaterEvent(world));

        Assert.Equal(0, world.Reader.GetBlockId(0, 64, 0));
        Assert.Contains(world.ReaderWriter.SetBlockCalls, c => c.X == 0 && c.Y == 64 && c.Z == 0 && c.BlockId == 0);
    }

    [Fact]
    public void OnTick_UnlitRepeater_WhenPowered_TransitionsToPoweredRepeaterPreservingMeta()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("repeater").Id, 1); // facing=1
        world.ReaderWriter.SetInitial(-1, 64, 0, TestBlocks.Get("redstone_wire").Id, 7); // power input for facing=1

        TestBlocks.Get("repeater").OnTick(RepeaterEvent(world));

        Assert.Equal(TestBlocks.Get("powered_repeater").Id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(1, world.Reader.GetBlockMeta(0, 64, 0));
    }

    [Fact]
    public void OnTick_LitRepeater_WhenUnpowered_TransitionsToUnlitRepeaterPreservingMeta()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("powered_repeater").Id, 3);

        TestBlocks.Get("powered_repeater").OnTick(RepeaterEvent(world));

        Assert.Equal(TestBlocks.Get("repeater").Id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(3, world.Reader.GetBlockMeta(0, 64, 0));
    }

    [Fact]
    public void OnTick_UnlitRepeater_WhenStillUnpowered_TurnsOnThenSchedulesPoweredTick()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("repeater").Id, 12); // delay index=3 => 8 ticks

        TestBlocks.Get("repeater").OnTick(RepeaterEvent(world));

        Assert.Equal(TestBlocks.Get("powered_repeater").Id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t is { X: 0, Y: 64, Z: 0 } && t.BlockId == TestBlocks.Get("powered_repeater").Id && t.TickRate == 8);
    }
}
