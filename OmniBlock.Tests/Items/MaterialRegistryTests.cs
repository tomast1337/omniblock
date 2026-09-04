namespace OmniBlock.Tests.Items;

public sealed class MaterialRegistryTests
{
    [Fact]
    public void ToolMaterialRegistry_LoadsIronFromJson()
    {
        var iron = ToolMaterialRegistry.Get("iron");

        Assert.Equal(250, iron.MaxUses);
        Assert.Equal(6.0f, iron.Efficiency);
        Assert.Equal(2, iron.DamageBonus);
        Assert.Equal(2, iron.HarvestLevel);
    }

    [Fact]
    public void ArmorMaterialRegistry_LoadsDiamondFromJson()
    {
        var diamond = ArmorMaterialRegistry.Get("diamond");

        Assert.Equal(3, diamond.ArmorLevel);
        Assert.Equal("diamond", diamond.TexturePrefix);
    }

    [Fact]
    public void IronShovel_MaxDamageMatchesIronToolMaterial()
    {
        var shovel = ContentRuntime.Current.Items.Get("omniblock:shovel_iron");
        Assert.Equal(250, shovel.GetMaxDamage());
    }
}
