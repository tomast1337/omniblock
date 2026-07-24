using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Items;

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

    [Fact]
    public void OnTick_CustomTrunk_DecaysAgainstConfiguredTrunkNotVanillaLog()
    {
        FakeWorldContext world = new();
        Block leavesBlock = BlockRegistry.Get("leaves");
        Block customTrunk = BlockRegistry.Get("stone");
        Block sapling = BlockRegistry.Get("sapling");
        Item shears = Item.ByName("shears");

        world.ReaderWriter.SetInitial(0, 63, 0, customTrunk.id);
        world.ReaderWriter.SetInitial(0, 64, 0, leavesBlock.id, 8);

        LeavesBehavior behavior = new(customTrunk, sapling, shears);
        behavior.OnTick(leavesBlock, Tick(world));

        Assert.Equal(0, world.Reader.GetBlockMeta(0, 64, 0) & 8);
    }

    [Fact]
    public void GetDroppedItemId_CustomSapling_ReturnsConfiguredItem()
    {
        Block log = BlockRegistry.Get("log");
        Block sand = BlockRegistry.Get("sand");
        Item shears = Item.ByName("shears");

        LeavesBehavior behavior = new(log, sand, shears);
        Assert.Equal(sand.id, behavior.GetDroppedItemId(BlockRegistry.Get("leaves"), 0, 0));
    }

    // No built-in default and no null fallback: an omitted or unknown "trunk"/"sapling"/
    // "harvest_tool" in JSON must throw immediately (at BehaviorRegistry.Build, i.e. server
    // boot), not silently fall back to a vanilla value.
    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"leaves","sapling":"betasharp:sapling","harvest_tool":"betasharp:shears"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("leaves", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"leaves","trunk":"not_a_real_block","sapling":"betasharp:sapling","harvest_tool":"betasharp:shears"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("leaves", json.RootElement));
    }
}
