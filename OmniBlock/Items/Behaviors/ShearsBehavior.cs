using OmniBlock.Blocks;
using OmniBlock.Entities;

namespace OmniBlock.Items.Behaviors;

internal sealed class ShearsBehavior : IItemBehavior
{
    private readonly Block _cobweb;
    private readonly Block _leaves;
    private readonly Block _wool;

    internal ShearsBehavior(Block cobweb, Block leaves, Block wool)
    {
        _cobweb = cobweb;
        _leaves = leaves;
        _wool = wool;
    }

    public float GetMiningSpeedMultiplier(Item item, ItemStack itemStack, Block block)
    {
        if (block == _cobweb || block == _leaves)
        {
            return 15.0F;
        }

        if (block == _wool)
        {
            return 5.0F;
        }

        return 1.0F;
    }

    public bool PostMine(Item item, ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving player)
    {
        if (blockId == _leaves.Id || blockId == _cobweb.Id)
        {
            itemStack.DamageItem(1, player);
        }

        return false;
    }

    public bool IsSuitableFor(Item item, Block block) => block == _cobweb;
}
