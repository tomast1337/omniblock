namespace OmniBlock.Tests.Items;

public sealed class ItemLookupRegistryTests
{
    [Fact]
    public void RegistryDerivedName_Resolves()
    {
        Assert.True(ContentRuntime.Current.Items.TryParse("omniblock:shovel_iron", out var stack));
        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:shovel_iron").Id, stack.ItemId);
    }

    [Fact]
    public void ItemsRegistryKey_HasExpectedLocation() => Assert.Equal("omniblock:item", RegistryKeys.Items.Location.ToString());

    [Theory]
    [InlineData("shovel_iron", "omniblock:shovel_iron", 0)]
    [InlineData("omniblock:shovel_iron", "omniblock:shovel_iron", 0)]
    [InlineData("litredstonetorch", "omniblock:lit_redstone_torch", 0)]
    [InlineData("omniblock:LitRedstoneTorch", "omniblock:lit_redstone_torch", 0)]
    [InlineData("omniblock:Wool:0", "omniblock:wool", 0)]
    [InlineData("omniblock:Dandelion", "omniblock:dandelion", 0)]
    [InlineData("charcoal", "omniblock:coal", 1)]
    public void Runtime_parser_preserves_legacy_names(string input, string expectedKey, int expectedMeta)
    {
        Assert.True(ContentRuntime.Current.Items.TryParse(input, out var stack));
        Assert.Same(ContentRuntime.Current.Items.Get(expectedKey), stack.GetItem());
        Assert.Equal(expectedMeta, stack.GetDamage());
    }
}
