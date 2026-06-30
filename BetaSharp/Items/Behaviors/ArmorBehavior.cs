namespace BetaSharp.Items.Behaviors;

public sealed class ArmorBehavior : IItemBehavior
{
    private static readonly int[] s_damageReduceAmountArray = [3, 8, 6, 3];
    private static readonly int[] s_maxDamageArray = [11, 16, 15, 13];

    public ArmorBehavior(int armorLevel, int renderIndex, int armorType)
    {
        ArmorLevel = armorLevel;
        ArmorType = armorType;
        RenderIndex = renderIndex;
        DamageReduceAmount = s_damageReduceAmountArray[armorType];
    }

    public int ArmorLevel { get; }
    public int ArmorType { get; }
    public int RenderIndex { get; }
    public int DamageReduceAmount { get; }

    public void Apply(Item item)
    {
        item.maxCount = 1;
        item.setMaxDamage((s_maxDamageArray[ArmorType] * 3) << ArmorLevel);
    }
}
