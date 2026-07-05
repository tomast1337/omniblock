namespace BetaSharp.Items.Behaviors;

public sealed class ArmorBehavior : IItemBehavior
{
    private static readonly int[] s_damageReduceAmountArray = [3, 8, 6, 3];
    private static readonly int[] s_maxDamageArray = [11, 16, 15, 13];

    public ArmorBehavior(ArmorMaterial material, ArmorSlot slot)
    {
        Material = material;
        Slot = slot;
        DamageReduceAmount = s_damageReduceAmountArray[(int)slot];
    }

    public ArmorMaterial Material { get; }
    public ArmorSlot Slot { get; }
    public int ArmorLevel => Material.ArmorLevel;
    public int RenderIndex => Material.RenderIndex;
    public int ArmorType => (int)Slot;
    public int DamageReduceAmount { get; }

    public void Apply(Item item) => item.setMaxDamage((s_maxDamageArray[ArmorType] * 3) << Material.ArmorLevel);
}
