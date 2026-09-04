using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockLogTests
{
    [Fact]
    public void OnBreak_DefaultConfig_MarksVanillaLeavesForDecay()
    {
        FakeWorldContext world = new();
        var logBlock = TestBlocks.Get("log");
        var leavesBlock = TestBlocks.Get("leaves");

        world.ReaderWriter.SetInitial(0, 64, 0, logBlock.Id);
        world.ReaderWriter.SetInitial(1, 64, 0, leavesBlock.Id);

        logBlock.OnBreak(new OnBreakEvent(world, null, 0, 64, 0));

        Assert.Equal(8, world.Reader.GetBlockMeta(1, 64, 0) & 8);
    }

    [Fact]
    public void OnBreak_CustomLeavesId_MarksConfiguredNeighborNotVanillaLeaves()
    {
        FakeWorldContext world = new();
        var logBlock = TestBlocks.Get("log");
        var customLeaves = TestBlocks.Get("wool");
        var vanillaLeaves = TestBlocks.Get("leaves");

        world.ReaderWriter.SetInitial(0, 64, 0, logBlock.Id);
        world.ReaderWriter.SetInitial(1, 64, 0, customLeaves.Id);
        world.ReaderWriter.SetInitial(2, 64, 0, vanillaLeaves.Id);

        LogBehavior behavior = new(customLeaves, 4, 0, [0, 0, 0, 0]);
        behavior.OnBreak(logBlock, new OnBreakEvent(world, null, 0, 64, 0));

        Assert.Equal(8, world.Reader.GetBlockMeta(1, 64, 0) & 8);
        Assert.Equal(0, world.Reader.GetBlockMeta(2, 64, 0) & 8);
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingSearchRadius_Throws()
    {
        using var json = JsonDocument.Parse("""{"Type":"log","canopy":"omniblock:leaves"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("log", json.RootElement));
    }
}
