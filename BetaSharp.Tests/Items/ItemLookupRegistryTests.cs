using BetaSharp.Items;
using BetaSharp.Registries;

namespace BetaSharp.Tests.Items;

public sealed class ItemLookupRegistryTests
{
    public ItemLookupRegistryTests() => ItemLookup.Initialize();

    [Fact]
    public void RegistryDerivedName_Resolves()
    {
        // Items load from JSON, with no static Item.* fields, so the registry path is the only
        // name ItemLookup knows: there is no field-name alias.
        Assert.True(ItemLookup.TryGetItemId("betasharp:shovel_iron", out int itemId));
        Assert.Equal(Item.ByName("shovel_iron").Id, itemId);
    }

    [Fact]
    public void ItemsRegistryKey_HasExpectedLocation()
    {
        Assert.Equal("betasharp:item", RegistryKeys.Items.Location.ToString());
    }
}
