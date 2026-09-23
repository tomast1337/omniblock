namespace OmniBlock.Items.Behaviors;

public sealed class ArmorBehavior(ArmorMaterial material, ArmorSlot slot) : IItemBehavior
{
    private static readonly int[] s_damageReduceAmountArray = [3, 8, 6, 3];
    private static readonly int[] s_maxDamageArray = [11, 16, 15, 13];

    public ArmorMaterial Material { get; } = material;
    public ArmorSlot Slot { get; } = slot;
    public int ArmorLevel => Material.ArmorLevel;
    public string TexturePrefix => Material.TexturePrefix;
    public int ArmorType => (int)Slot;
    public int DamageReduceAmount { get; } = s_damageReduceAmountArray[(int)slot];

    public void Apply(Item item) => item.SetMaxDamage((s_maxDamageArray[ArmorType] * 3) << Material.ArmorLevel);
}
