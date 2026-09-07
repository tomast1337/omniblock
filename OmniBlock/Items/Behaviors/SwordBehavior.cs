using OmniBlock.Blocks;
using OmniBlock.Entities;

namespace OmniBlock.Items.Behaviors;

internal sealed class SwordBehavior : IItemBehavior
{
    private readonly ToolMaterial _toolMaterial;
    private readonly int _weaponDamage;
    private readonly Block _cobweb;

    internal SwordBehavior(ToolMaterial toolMaterial, Block cobweb)
    {
        _toolMaterial = toolMaterial;
        _cobweb = cobweb;
        _weaponDamage = 4 + toolMaterial.DamageBonus * 2;
    }

    public void Apply(Item item) => item.SetMaxDamage(_toolMaterial.MaxUses);

    public float GetMiningSpeedMultiplier(Item item, ItemStack itemStack, Block block)
        => block == _cobweb ? 15.0F : 1.5F;

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

    public bool IsSuitableFor(Item item, Block block) => block == _cobweb;
}
