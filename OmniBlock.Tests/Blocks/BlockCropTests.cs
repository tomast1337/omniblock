using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Items;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockCropTests
{
    [Fact]
    public void CanPlaceAt_CustomFarmland_MatchesConfiguredBlockNotVanillaFarmland()
    {
        FakeWorldContext world = new();
        Block customSoil = TestBlocks.Get("sand");
        Block vanillaFarmland = TestBlocks.Get("farmland");
        Item wheat = ContentRuntime.Current.Items.Get("omniblock:wheat");
        Item seeds = ContentRuntime.Current.Items.Get("omniblock:seeds");

        world.ReaderWriter.SetInitial(0, 63, 0, customSoil.Id);
        CropBehavior behavior = new(customSoil, wheat, seeds, 0.7F, 15, 100, stages: [0, 0, 0, 0, 0, 0, 0, 0]);

        Assert.True(behavior.CanPlaceAt(TestBlocks.Get("wheat"), new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));

        world.ReaderWriter.SetInitial(0, 63, 0, vanillaFarmland.Id);
        Assert.False(behavior.CanPlaceAt(TestBlocks.Get("wheat"), new CanPlaceAtContext(world, Side.Up, 0, 64, 0)));
    }

    [Fact]
    public void GetDroppedItemId_CustomWheat_ReturnsConfiguredItemOnlyWhenMature()
    {
        Item apple = ContentRuntime.Current.Items.Get("omniblock:apple");
        CropBehavior behavior = new(TestBlocks.Get("farmland"), apple, ContentRuntime.Current.Items.Get("omniblock:seeds"), 0.7F, 15, 100, stages: [0, 0, 0, 0, 0, 0, 0, 0]);

        Assert.Equal(apple.Id, behavior.GetDroppedItemId(TestBlocks.Get("wheat"), 7, 0));
        Assert.Equal(-1, behavior.GetDroppedItemId(TestBlocks.Get("wheat"), 3, 0));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"crop","mature_crop_item":"omniblock:wheat","seeds":"omniblock:seeds"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("crop", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"crop","required_soil":"not_a_real_block","mature_crop_item":"omniblock:wheat","seeds":"omniblock:seeds"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("crop", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_MissingDropSpread_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"crop","required_soil":"omniblock:farmland","mature_crop_item":"omniblock:wheat","seeds":"omniblock:seeds"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("crop", json.RootElement));
    }
}
