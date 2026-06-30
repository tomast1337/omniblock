namespace BetaSharp.Items.Behaviors;

public sealed class ArmorBehavior : IItemBehavior
{
    private static readonly int[] s_damageReduceAmountArray = [3, 8, 6, 3];
    private static readonly int[] s_maxDamageArray = [11, 16, 15, 13];

    public ArmorBehavior(ArmorMaterial material, int armorType)
    {
        Material = material;
        ArmorType = armorType;
        DamageReduceAmount = s_damageReduceAmountArray[armorType];
    }

    public ArmorMaterial Material { get; }
    public int ArmorLevel => Material.ArmorLevel;
    public int RenderIndex => Material.RenderIndex;
    public int ArmorType { get; }
    public int DamageReduceAmount { get; }

    public void Apply(Item item)
    {
        item.maxCount = 1;
        item.setMaxDamage((s_maxDamageArray[ArmorType] * 3) << Material.ArmorLevel);
    }
}
