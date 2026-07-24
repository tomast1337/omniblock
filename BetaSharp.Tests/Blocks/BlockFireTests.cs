using BetaSharp.Blocks;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockFireTests
{
    private static OnTickEvent Tick(FakeWorldContext world, int x = 0, int y = 64, int z = 0) => new(world, x, y, z, world.Reader.GetBlockMeta(x, y, z), world.Reader.GetBlockId(x, y, z));

    // Regression: FireBehavior is wired into the Ticker, Physics, and Lifecycle slots
    // independently, and AttachBehaviors builds a separate instance per slot — cached
    // cross-block ids must be static, not instance fields set via OnInit (which only ever
    // runs on the Lifecycle-slot instance), or OnTick NREs on the Ticker-slot instance.
    [Fact]
    public void OnTick_DoesNotThrow()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("fire").id);

        BlockRegistry.Get("fire").OnTick(Tick(world));
    }

    [Fact]
    public void OnPlaced_DoesNotThrow()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("fire").id);

        BlockRegistry.Get("fire").OnPlaced(new OnPlacedEvent(world, null, Side.Up, Side.Up, 0, 64, 0));
    }
}
