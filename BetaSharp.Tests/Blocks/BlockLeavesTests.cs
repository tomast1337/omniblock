using BetaSharp.Blocks;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockLeavesTests
{
    private static OnTickEvent Tick(FakeWorldContext world, int x = 0, int y = 64, int z = 0) => new(world, x, y, z, world.Reader.GetBlockMeta(x, y, z), world.Reader.GetBlockId(x, y, z));

    // Regression: LeavesBehavior is wired into the Ticker, Lifecycle, and Visuals slots
    // independently, and AttachBehaviors builds a separate instance per slot — cached
    // cross-block ids must be static, not instance fields set via OnInit (which only ever
    // runs on the Lifecycle-slot instance). With instance fields, OnTick's decay flood-fill
    // (Ticker-slot instance) would silently compare against 0 instead of the real log/leaves
    // ids, since 0 also happens to be air's block id.
    [Fact]
    public void OnTick_DecayCheck_ComparesAgainstRealLogAndLeavesIds()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("log").id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("leaves").id, 8);

        BlockRegistry.Get("leaves").OnTick(Tick(world));

        Assert.Equal(0, world.Reader.GetBlockMeta(0, 64, 0) & 8);
    }

    [Fact]
    public void GetDroppedItemId_IsSapling()
        => Assert.Equal(BlockRegistry.Get("sapling").id, BlockRegistry.Get("leaves").GetDroppedItemId(0));
}
