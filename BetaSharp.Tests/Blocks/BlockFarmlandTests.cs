using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockFarmlandTests
{
    // The block it reverts to and the crop that keeps it wet are required constructor params
    // (JSON-configurable per variant, no built-in vanilla fallback). Construct a differently
    // configured instance directly (bypassing BlockRegistry) to prove the override actually
    // takes effect rather than silently defaulting.
    [Fact]
    public void GetDroppedItemId_CustomDirt_ReturnsConfiguredBlocksDrop()
    {
        Block customRevertTarget = BlockRegistry.Get("sand");
        FarmlandBehavior behavior = new(customRevertTarget, BlockRegistry.Get("wheat"), 4, 5, 4);

        Assert.Equal(customRevertTarget.GetDroppedItemId(0), behavior.GetDroppedItemId(BlockRegistry.Get("farmland"), 0, 0));
    }

    [Fact]
    public void NeighborUpdate_SolidBlockAbove_RevertsToConfiguredBlockNotVanillaDirt()
    {
        FakeWorldContext world = new();
        Block farmlandBlock = BlockRegistry.Get("farmland");
        Block customRevertTarget = BlockRegistry.Get("sand");

        world.ReaderWriter.SetInitial(0, 64, 0, farmlandBlock.id);
        world.ReaderWriter.SetInitial(0, 65, 0, BlockRegistry.Get("stone").id);

        FarmlandBehavior behavior = new(customRevertTarget, BlockRegistry.Get("wheat"), 4, 5, 4);
        behavior.NeighborUpdate(farmlandBlock, new OnTickEvent(world, 0, 64, 0, 0, farmlandBlock.id));

        Assert.Equal(customRevertTarget.id, world.Reader.GetBlockId(0, 64, 0));
    }

    // No built-in default and no null fallback: an omitted or unknown "revert_block"/"crop" in
    // JSON must throw immediately (at BehaviorRegistry.Build, i.e. server boot).
    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"farmland","revert_block":"dirt"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("farmland", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"farmland","revert_block":"not_a_real_block","crop":"wheat"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("farmland", json.RootElement));
    }

    // Numeric tuning params (trample_chance_one_in, tick_chance_one_in, water_check_radius) are
    // also required, no default: an omitted value must throw immediately at
    // BehaviorRegistry.Build.
    [Fact]
    public void BehaviorRegistry_Build_MissingTrampleChanceOneIn_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"farmland","revert_block":"dirt","crop":"wheat"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("farmland", json.RootElement));
    }
}
