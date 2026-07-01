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
        Assert.Equal(Item.ByName("apple").id, holder.Value.ProtocolId);
        Assert.IsType<FoodBehaviorDefinition>(holder.Value.Behavior);
    }

    [Fact]
    public void DuplicateTranslationKeys_DisambiguateByProtocolId()
    {
        // Both records share the translation key "record"; the first declared keeps the plain
        // name and the second is disambiguated with its protocol ID suffix.
        Holder<ItemDefinition>? thirteen = DefaultRegistries.Items.Get(ResourceLocation.Parse("betasharp:record"));
        Holder<ItemDefinition>? cat = DefaultRegistries.Items.Get(ResourceLocation.Parse($"betasharp:record_{Item.ByName("record_2257").id}"));

        Assert.NotNull(thirteen);
        Assert.NotNull(cat);
        Assert.Equal(Item.ByName("record").id, thirteen.Value.ProtocolId);
        Assert.Equal(Item.ByName("record_2257").id, cat.Value.ProtocolId);
    }

    [Fact]
    public void JsonLoadedItems_AreRegistered()
    {
        Assert.True(DefaultRegistries.Items.ContainsId(Item.ByName("shovel_iron").id));
        Assert.True(DefaultRegistries.Items.ContainsId(Item.ByName("boots_diamond").id));
        Assert.True(DefaultRegistries.Items.ContainsId(Item.ByName("map").id));
    }
}
