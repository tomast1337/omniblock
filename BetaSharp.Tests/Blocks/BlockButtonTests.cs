using BetaSharp.Blocks;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockButtonTests
{
    [Fact]
    public void OnUse_PressesButtonAndSchedulesTick()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(-1, 64, 0, BlockRegistry.Get("stone").id); // support for facing=1
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("button").id, 1);

        bool handled = BlockRegistry.Get("button").onUse(new OnUseEvent(world, null!, 0, 64, 0));

        Assert.True(handled);
        Assert.Equal(9, world.Reader.GetBlockMeta(0, 64, 0)); // pressed bit set
        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t.X == 0 && t is { Y: 64, Z: 0 } && t.BlockId == BlockRegistry.Get("button").id && t.TickRate == 20);
    }

    [Fact]
    public void OnTick_PopsPressedButtonBackOut()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(-1, 64, 0, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("button").id, 9); // facing=1 pressed

        BlockRegistry.Get("button").OnTick(new OnTickEvent(world, 0, 64, 0, 9, BlockRegistry.Get("button").id));

        Assert.Equal(1, world.Reader.GetBlockMeta(0, 64, 0)); // pressed bit cleared
    }
}
