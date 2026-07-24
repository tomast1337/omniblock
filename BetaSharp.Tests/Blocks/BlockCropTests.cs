using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Items;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockCropTests
{
    // Required soil, mature-drop item, and seed item are required constructor params
    // (JSON-configurable per variant, no built-in vanilla fallback). Construct a differently
    // configured instance directly (bypassing BlockRegistry) to prove the override actually
    // takes effect rather than silently defaulting.
    [Fact]
    public void CanPlaceAt_CustomFarmland_MatchesConfiguredBlockNotVanillaFarmland()
    {
        FakeWorldContext world = new();
        Block customSoil = BlockRegistry.Get("sand");
        Block vanillaFarmland = BlockRegistry.Get("farmland");
        Item wheat = Item.ByName("wheat");
        Item seeds = Item.ByName("seeds");

        world.ReaderWriter.SetInitial(0, 63, 0, customSoil.id);
        CropBehavior behavior = new(customSoil, wheat, seeds);

        Assert.True(behavior.CanPlaceAt(BlockRegistry.Get("wheat"), new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));

        world.ReaderWriter.SetInitial(0, 63, 0, vanillaFarmland.id);
        Assert.False(behavior.CanPlaceAt(BlockRegistry.Get("wheat"), new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void GetDroppedItemId_CustomWheat_ReturnsConfiguredItemOnlyWhenMature()
    {
        Item apple = Item.ByName("apple");
        CropBehavior behavior = new(BlockRegistry.Get("farmland"), apple, Item.ByName("seeds"));

        Assert.Equal(apple.Id, behavior.GetDroppedItemId(BlockRegistry.Get("wheat"), 7, 0));
        Assert.Equal(-1, behavior.GetDroppedItemId(BlockRegistry.Get("wheat"), 3, 0));
    }

    // No built-in default and no null fallback: an omitted or unknown "required_soil"/
    // "mature_crop_item"/"seeds" in JSON must throw immediately (at BehaviorRegistry.Build, i.e.
    // server boot).
    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"crop","mature_crop_item":"wheat","seeds":"seeds"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("crop", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"crop","required_soil":"not_a_real_block","mature_crop_item":"wheat","seeds":"seeds"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("crop", json.RootElement));
    }
}
