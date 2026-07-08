using BetaSharp.Blocks;
using BetaSharp.Entities;

namespace BetaSharp.Items.Behaviors;

internal sealed class ToolBehavior : IItemBehavior
{
    private readonly int _damageVsEntity;
    // Deferred: ToolBehaviorDefinition.Build() runs during ItemFactory.Create(), before
    // BlockRegistry.Initialize() has loaded any blocks — Item.s_axeBlocks etc. are themselves
    // lazy, but only forcing their first evaluation this late (mining time, not boot time)
    // actually keeps them lazy in practice.
    private readonly Func<Block[]> _effectiveBlocksFactory;
    private Block[] _effectiveBlocks => _effectiveBlocksFactory();
    private readonly float _efficiencyOnProperMaterial;
    private readonly Func<Block, bool>? _suitableFor;
    private readonly ToolMaterial _toolMaterial;

    internal ToolBehavior(ToolMaterial toolMaterial, int baseDamage, Func<Block[]> effectiveBlocks, Func<Block, bool>? suitableFor = null)
    {
        _toolMaterial = toolMaterial;
        _efficiencyOnProperMaterial = toolMaterial.Efficiency;
        _damageVsEntity = baseDamage + toolMaterial.DamageBonus;
        _effectiveBlocksFactory = effectiveBlocks;
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
