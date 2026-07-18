using BetaSharp.Items;
using BetaSharp.Items.Behaviors;
using BetaSharp.Registries;

namespace BetaSharp.Tests.Items;

public sealed class ItemRegistryTests
{
    [Fact]
    public void Apple_RegistersUnderExpectedResourceLocation()
    {
        Holder<ItemDefinition>? holder = DefaultRegistries.Items.Get(ResourceLocation.Parse("betasharp:apple"));

        Assert.NotNull(holder);
        Assert.Equal(Item.ByName("apple").Id, holder.Value.ProtocolId);
        Assert.IsType<FoodBehaviorDefinition>(holder.Value.Behavior);
    }

    [Fact]
    public void DuplicateTranslationKeys_DisambiguateByProtocolId()
    {
        // Both records share the translation key "record"; the first declared keeps the plain
        // name and the second is disambiguated with its protocol ID suffix.
        Holder<ItemDefinition>? thirteen = DefaultRegistries.Items.Get(ResourceLocation.Parse("betasharp:record"));
        Holder<ItemDefinition>? cat = DefaultRegistries.Items.Get(ResourceLocation.Parse($"betasharp:record_{Item.ByName("record_2257").Id}"));

        Assert.NotNull(thirteen);
        Assert.NotNull(cat);
        Assert.Equal(Item.ByName("record").Id, thirteen.Value.ProtocolId);
        Assert.Equal(Item.ByName("record_2257").Id, cat.Value.ProtocolId);
    }

    [Fact]
    public void JsonLoadedItems_AreRegistered()
    {
        Assert.True(DefaultRegistries.Items.ContainsId(Item.ByName("shovel_iron").Id));
        Assert.True(DefaultRegistries.Items.ContainsId(Item.ByName("boots_diamond").Id));
        Assert.True(DefaultRegistries.Items.ContainsId(Item.ByName("map").Id));
    }

    [Fact]
    public void CraftingReturnItems_AreWiredAfterBoot()
    {
        Item bucket = Item.ByName("bucket");

        Assert.Same(bucket, Item.ByName("bucket_water").getContainerItem());
        Assert.Same(bucket, Item.ByName("bucket_lava").getContainerItem());
        Assert.Same(bucket, Item.ByName("milk").getContainerItem());
    }

    [Fact]
    public void ResolveCrossReferences_ResolvesReferenceToLaterCreatedItem()
    {
        // The loader enumerates definitions alphabetically by filename, so a definition can
        // reference an item whose file sorts after it. Mirror that: create the referencing
        // item first, its target second, then run the second pass.
        var referencing = new ItemDefinition
        {
            Name = "test_filled_container",
            ProtocolId = 31900,
            MaxStackSize = 1,
            CraftingReturnItemProtocolId = 31901,
        };
        var target = new ItemDefinition
        {
            Name = "test_empty_container",
            ProtocolId = 31901,
        };

        Item.ITEMS[referencing.ProtocolId] = ItemFactory.Create(referencing);
        Item.ITEMS[target.ProtocolId] = ItemFactory.Create(target);
        ItemFactory.ResolveCrossReferences(referencing);
        ItemFactory.ResolveCrossReferences(target);

        Assert.Same(Item.ITEMS[31901], Item.ITEMS[31900]!.getContainerItem());
    }
}
