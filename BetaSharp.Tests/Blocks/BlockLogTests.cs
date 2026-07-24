using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockLogTests
{
    [Fact]
    public void OnBreak_DefaultConfig_MarksVanillaLeavesForDecay()
    {
        FakeWorldContext world = new();
        Block logBlock = BlockRegistry.Get("log");
        Block leavesBlock = BlockRegistry.Get("leaves");

        world.ReaderWriter.SetInitial(0, 64, 0, logBlock.id);
        world.ReaderWriter.SetInitial(1, 64, 0, leavesBlock.id);

        logBlock.OnBreak(new OnBreakEvent(world, null, 0, 64, 0));

        Assert.Equal(8, world.Reader.GetBlockMeta(1, 64, 0) & 8);
    }

    // Which block counts as "attached leaves" is a required constructor param
    // (JSON-configurable per variant, no built-in vanilla fallback). Construct a differently
    // configured instance directly (bypassing BlockRegistry) to prove the override actually
    // takes effect rather than silently defaulting.
    [Fact]
    public void OnBreak_CustomLeavesId_MarksConfiguredNeighborNotVanillaLeaves()
    {
        FakeWorldContext world = new();
        Block logBlock = BlockRegistry.Get("log");
        Block customLeaves = BlockRegistry.Get("wool");
        Block vanillaLeaves = BlockRegistry.Get("leaves");

        world.ReaderWriter.SetInitial(0, 64, 0, logBlock.id);
        world.ReaderWriter.SetInitial(1, 64, 0, customLeaves.id);
        world.ReaderWriter.SetInitial(2, 64, 0, vanillaLeaves.id);

        LogBehavior behavior = new(leavesBlockId: customLeaves.id);
        behavior.OnBreak(logBlock, new OnBreakEvent(world, null, 0, 64, 0));

        Assert.Equal(8, world.Reader.GetBlockMeta(1, 64, 0) & 8);
        Assert.Equal(0, world.Reader.GetBlockMeta(2, 64, 0) & 8);
    }
}
