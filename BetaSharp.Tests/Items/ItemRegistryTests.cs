using BetaSharp.Items;
using BetaSharp.Items.Behaviors;
using BetaSharp.Registries;

namespace BetaSharp.Tests.Items;

public sealed class ItemRegistryTests
{
    public ItemRegistryTests()
    {
        // Trigger Item's static field initializers, mirroring EntityTestsFixture.
        _ = Item.Stick.id;
    }

    [Fact]
    public void Apple_RegistersUnderExpectedResourceLocation()
    {
        Holder<ItemDefinition>? holder = DefaultRegistries.Items.Get(ResourceLocation.Parse("betasharp:apple"));

        Assert.NotNull(holder);
        Assert.Equal(Item.Apple.id, holder.Value.ProtocolId);
        Assert.IsType<FoodBehaviorDefinition>(holder.Value.Behavior);
    }

    [Fact]
    public void DuplicateTranslationKeys_DisambiguateByProtocolId()
    {
        // Both records share the translation key "record"; the first declared keeps the plain
        // name and the second is disambiguated with its protocol ID suffix.
        Holder<ItemDefinition>? thirteen = DefaultRegistries.Items.Get(ResourceLocation.Parse("betasharp:record"));
        Holder<ItemDefinition>? cat = DefaultRegistries.Items.Get(ResourceLocation.Parse($"betasharp:record_{Item.RecordCat.id}"));

        Assert.NotNull(thirteen);
        Assert.NotNull(cat);
        Assert.Equal(Item.RecordThirteen.id, thirteen.Value.ProtocolId);
        Assert.Equal(Item.RecordCat.id, cat.Value.ProtocolId);
    }

    [Fact]
    public void EveryStaticItem_IsRegistered()
    {
        Assert.True(DefaultRegistries.Items.ContainsId(Item.IronShovel.id));
        Assert.True(DefaultRegistries.Items.ContainsId(Item.DiamondBoots.id));
        Assert.True(DefaultRegistries.Items.ContainsId(Item.Map.id));
    }
}
