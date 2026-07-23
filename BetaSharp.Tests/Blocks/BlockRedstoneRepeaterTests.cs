using BetaSharp.Blocks;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockRedstoneRepeaterTests
{
    private static OnTickEvent RepeaterEvent(FakeWorldContext world, int x = 0, int y = 64, int z = 0)
    {
        int meta = world.Reader.GetBlockMeta(x, y, z);
        int blockId = world.Reader.GetBlockId(x, y, z);
        return new OnTickEvent(world, x, y, z, meta, blockId);
    }

    [Fact]
    public void NeighborUpdate_UnlitRepeater_WhenPowered_SchedulesTickUsingDelaySetting()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("repeater").id, 4); // facing=0, delay index=1 => 4 ticks
        world.ReaderWriter.SetInitial(0, 64, 1, BlockRegistry.Get("redstone_wire").id, 15); // power input for facing=0

        BlockRegistry.Get("repeater").NeighborUpdate(RepeaterEvent(world));

        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t is { X: 0, Y: 64, Z: 0 } && t.BlockId == BlockRegistry.Get("repeater").id && t.TickRate == 4);
    }

    [Fact]
    public void NeighborUpdate_LitRepeater_WhenUnpowered_SchedulesTickUsingDelaySetting()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("powered_repeater").id, 8); // facing=0, delay index=2 => 6 ticks

        BlockRegistry.Get("powered_repeater").NeighborUpdate(RepeaterEvent(world));

        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t is { X: 0, Y: 64, Z: 0 } && t.BlockId == BlockRegistry.Get("powered_repeater").id && t.TickRate == 6);
    }

    [Fact]
    public void NeighborUpdate_WithoutSupportingBlock_BreaksRepeaterIntoAir()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("repeater").id);
        // No supporting block at (0,63,0) => ShouldSuffocate false.

        BlockRegistry.Get("repeater").NeighborUpdate(RepeaterEvent(world));

        Assert.Equal(0, world.Reader.GetBlockId(0, 64, 0));
        Assert.Contains(world.ReaderWriter.SetBlockCalls, c => c.X == 0 && c.Y == 64 && c.Z == 0 && c.BlockId == 0);
    }

    [Fact]
    public void OnTick_UnlitRepeater_WhenPowered_TransitionsToPoweredRepeaterPreservingMeta()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("repeater").id, 1); // facing=1
        world.ReaderWriter.SetInitial(-1, 64, 0, BlockRegistry.Get("redstone_wire").id, 7); // power input for facing=1

        BlockRegistry.Get("repeater").OnTick(RepeaterEvent(world));

        Assert.Equal(BlockRegistry.Get("powered_repeater").id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(1, world.Reader.GetBlockMeta(0, 64, 0));
    }

    [Fact]
    public void OnTick_LitRepeater_WhenUnpowered_TransitionsToUnlitRepeaterPreservingMeta()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("powered_repeater").id, 3);

        BlockRegistry.Get("powered_repeater").OnTick(RepeaterEvent(world));

        Assert.Equal(BlockRegistry.Get("repeater").id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(3, world.Reader.GetBlockMeta(0, 64, 0));
    }

    [Fact]
    public void OnTick_UnlitRepeater_WhenStillUnpowered_TurnsOnThenSchedulesPoweredTick()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("repeater").id, 12); // delay index=3 => 8 ticks

        BlockRegistry.Get("repeater").OnTick(RepeaterEvent(world));

        Assert.Equal(BlockRegistry.Get("powered_repeater").id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t is { X: 0, Y: 64, Z: 0 } && t.BlockId == BlockRegistry.Get("powered_repeater").id && t.TickRate == 8);
    }
}
