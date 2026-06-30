using BetaSharp.Items;
using BetaSharp.Registries;

namespace BetaSharp.Tests.Items;

public sealed class ItemLookupRegistryTests
{
    public ItemLookupRegistryTests() => ItemLookup.Initialize();

    [Fact]
    public void LegacyFieldName_StillResolves()
    {
        Assert.True(ItemLookup.TryGetItemId("ironshovel", out int itemId));
        Assert.Equal(Item.IronShovel.id, itemId);
    }

    [Fact]
    public void RegistryDerivedName_AlsoResolves()
    {
        Assert.True(ItemLookup.TryGetItemId("betasharp:shovel_iron", out int itemId));
        Assert.Equal(Item.IronShovel.id, itemId);
    }

    [Fact]
    public void ItemsRegistryKey_HasExpectedLocation()
    {
        Assert.Equal("betasharp:item", RegistryKeys.Items.Location.ToString());
    }
}
