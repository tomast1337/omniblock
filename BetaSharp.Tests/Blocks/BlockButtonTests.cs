using BetaSharp.Blocks;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockButtonTests
{
    [Fact]
    public void OnUse_PressesButtonAndSchedulesTick()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(-1, 64, 0, Block.Stone.Id); // support for facing=1
        world.ReaderWriter.SetInitial(0, 64, 0, Block.Button.Id, 1);

        bool handled = Block.Button.OnUse(new OnUseEvent(world, null!, 0, 64, 0));

        Assert.True(handled);
        Assert.Equal(9, world.Reader.GetBlockMeta(0, 64, 0)); // pressed bit set
        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t.X == 0 && t is { Y: 64, Z: 0 } && t.BlockId == Block.Button.Id && t.TickRate == 20);
    }

    [Fact]
    public void OnTick_PopsPressedButtonBackOut()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(-1, 64, 0, Block.Stone.Id);
        world.ReaderWriter.SetInitial(0, 64, 0, Block.Button.Id, 9); // facing=1 pressed

        Block.Button.OnTick(new OnTickEvent(world, 0, 64, 0, 9, Block.Button.Id));

        Assert.Equal(1, world.Reader.GetBlockMeta(0, 64, 0)); // pressed bit cleared
    }
}
