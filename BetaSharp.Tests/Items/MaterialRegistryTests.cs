using BetaSharp.Items;

namespace BetaSharp.Tests.Items;

public sealed class MaterialRegistryTests
{
    [Fact]
    public void ToolMaterialRegistry_LoadsIronFromJson()
    {
        ToolMaterial iron = ToolMaterialRegistry.Get("iron");

        Assert.Equal(250, iron.MaxUses);
        Assert.Equal(6.0f, iron.Efficiency);
        Assert.Equal(2, iron.DamageBonus);
        Assert.Equal(2, iron.HarvestLevel);
    }

    [Fact]
    public void ArmorMaterialRegistry_LoadsDiamondFromJson()
    {
        ArmorMaterial diamond = ArmorMaterialRegistry.Get("diamond");

        Assert.Equal(3, diamond.ArmorLevel);
        Assert.Equal(3, diamond.RenderIndex);
    }

    [Fact]
    public void IronShovel_MaxDamageMatchesIronToolMaterial()
    {
        Item shovel = Item.ByName("shovel_iron");
        Assert.Equal(250, shovel.getMaxDamage());
    }
}
