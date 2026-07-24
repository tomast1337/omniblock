using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Items;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockCropTests
{
    [Fact]
    public void CanPlaceAt_CustomFarmland_MatchesConfiguredBlockNotVanillaFarmland()
    {
        FakeWorldContext world = new();
        Block customSoil = BlockRegistry.Get("sand");
        Block vanillaFarmland = BlockRegistry.Get("farmland");
        Item wheat = Item.ByName("wheat");
        Item seeds = Item.ByName("seeds");

        world.ReaderWriter.SetInitial(0, 63, 0, customSoil.id);
        CropBehavior behavior = new(customSoil, wheat, seeds, 0.7F, 15, 100);

        Assert.True(behavior.CanPlaceAt(BlockRegistry.Get("wheat"), new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));

        world.ReaderWriter.SetInitial(0, 63, 0, vanillaFarmland.id);
        Assert.False(behavior.CanPlaceAt(BlockRegistry.Get("wheat"), new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void GetDroppedItemId_CustomWheat_ReturnsConfiguredItemOnlyWhenMature()
    {
        Item apple = Item.ByName("apple");
        CropBehavior behavior = new(BlockRegistry.Get("farmland"), apple, Item.ByName("seeds"), 0.7F, 15, 100);

        Assert.Equal(apple.Id, behavior.GetDroppedItemId(BlockRegistry.Get("wheat"), 7, 0));
        Assert.Equal(-1, behavior.GetDroppedItemId(BlockRegistry.Get("wheat"), 3, 0));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"crop","mature_crop_item":"betasharp:wheat","seeds":"betasharp:seeds"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("crop", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"crop","required_soil":"not_a_real_block","mature_crop_item":"betasharp:wheat","seeds":"betasharp:seeds"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("crop", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingDropSpread_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"crop","required_soil":"betasharp:farmland","mature_crop_item":"betasharp:wheat","seeds":"betasharp:seeds"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("crop", json.RootElement));
    }
}
