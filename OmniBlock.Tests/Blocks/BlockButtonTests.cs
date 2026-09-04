using OmniBlock.Blocks;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockButtonTests
{
    [Fact]
    public void OnUse_PressesButtonAndSchedulesTick()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(-1, 64, 0, TestBlocks.Get("stone").Id); // support for facing=1
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("button").Id, 1);

        var handled = TestBlocks.Get("button").OnUse(new OnUseEvent(world, null!, 0, 64, 0));

        Assert.True(handled);
        Assert.Equal(9, world.Reader.GetBlockMeta(0, 64, 0)); // pressed bit set
        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t.X == 0 && t is { Y: 64, Z: 0 } && t.BlockId == TestBlocks.Get("button").Id && t.TickRate == 20);
    }

    [Fact]
    public void OnTick_PopsPressedButtonBackOut()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(-1, 64, 0, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("button").Id, 9); // facing=1 pressed

        TestBlocks.Get("button").OnTick(new OnTickEvent(world, 0, 64, 0, 9, TestBlocks.Get("button").Id));

        Assert.Equal(1, world.Reader.GetBlockMeta(0, 64, 0)); // pressed bit cleared
    }
}
