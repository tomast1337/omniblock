using BetaSharp.Items;
using BetaSharp.Registries;

namespace BetaSharp.Tests.Items;

public sealed class ItemLookupRegistryTests
{
    public ItemLookupRegistryTests() => ItemLookup.Initialize();

    [Fact]
    public void RegistryDerivedName_Resolves()
    {
        // Since Phase 4, items load from JSON (no more static Item.* fields), so the registry
        // path is the only name ItemLookup knows about — there is no legacy field-name alias.
        Assert.True(ItemLookup.TryGetItemId("betasharp:shovel_iron", out int itemId));
        Assert.Equal(Item.ByName("shovel_iron").Id, itemId);
    }

    [Fact]
    public void ItemsRegistryKey_HasExpectedLocation()
    {
        Assert.Equal("betasharp:item", RegistryKeys.Items.Location.ToString());
    }
}
