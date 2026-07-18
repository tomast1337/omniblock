using BetaSharp.Blocks;
using BetaSharp.Entities;

namespace BetaSharp.Items.Behaviors;

internal sealed class ToolBehavior : IItemBehavior
{
    private readonly int _damageVsEntity;
    private readonly Block[] _effectiveBlocks;
    private readonly float _efficiencyOnProperMaterial;
    private readonly Func<Block, bool>? _suitableFor;
    private readonly ToolMaterial _toolMaterial;

    internal ToolBehavior(ToolMaterial toolMaterial, int baseDamage, Block[] effectiveBlocks, Func<Block, bool>? suitableFor = null)
    {
        _toolMaterial = toolMaterial;
        _efficiencyOnProperMaterial = toolMaterial.Efficiency;
        _damageVsEntity = baseDamage + toolMaterial.DamageBonus;
        _effectiveBlocks = effectiveBlocks;
        _suitableFor = suitableFor;
    }

    public void Apply(Item item) => item.setMaxDamage(_toolMaterial.MaxUses);

    public float GetMiningSpeedMultiplier(Item item, ItemStack itemStack, Block block)
    {
        foreach (Block effective in _effectiveBlocks)
        {
            if (effective == block)
            {
                return _efficiencyOnProperMaterial;
            }
        }

        return 1.0f;
    }

    public bool PostHit(Item item, ItemStack itemStack, EntityLiving target, EntityPlayer player)
    {
        itemStack.DamageItem(2, player);
        return true;
    }

    public bool PostMine(Item item, ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving player)
    {
        itemStack.DamageItem(1, player);
        return true;
    }

    public int GetAttackDamage(Item item, Entity entity) => _damageVsEntity;

    public bool IsHandheld(Item item) => true;

    public bool IsSuitableFor(Item item, Block block) => _suitableFor?.Invoke(block) ?? false;
}
