using BetaSharp.Blocks;
using BetaSharp.Entities;

namespace BetaSharp.Items.Behaviors;

internal sealed class SwordBehavior : IItemBehavior
{
    private readonly ToolMaterial _toolMaterial;
    private readonly int _weaponDamage;

    internal SwordBehavior(ToolMaterial toolMaterial)
    {
        _toolMaterial = toolMaterial;
        _weaponDamage = 4 + toolMaterial.DamageBonus * 2;
    }

    public void Apply(Item item) => item.setMaxDamage(_toolMaterial.MaxUses);

    public float GetMiningSpeedMultiplier(Item item, ItemStack itemStack, Block block)
        => block.id == Block.Cobweb.id ? 15.0F : 1.5F;

    public bool PostHit(Item item, ItemStack itemStack, EntityLiving target, EntityPlayer player)
    {
        itemStack.DamageItem(1, player);
        return true;
    }

    public bool PostMine(Item item, ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving player)
    {
        itemStack.DamageItem(2, player);
        return true;
    }

    public int GetAttackDamage(Item item, Entity entity) => _weaponDamage;

    public bool IsHandheld(Item item) => true;

    public bool IsSuitableFor(Item item, Block block) => block.id == Block.Cobweb.id;
}
