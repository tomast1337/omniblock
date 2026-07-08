using BetaSharp.Blocks;
using BetaSharp.Entities;

namespace BetaSharp.Items.Behaviors;

internal sealed class ShearsBehavior : IItemBehavior
{
    public float GetMiningSpeedMultiplier(Item item, ItemStack itemStack, Block block)
    {
        if (block.Id == Block.Cobweb.Id || block.Id == Block.Leaves.Id)
        {
            return 15.0F;
        }

        if (block.Id == Block.Wool.Id)
        {
            return 5.0F;
        }

        return 1.0F;
    }

    public bool PostMine(Item item, ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving player)
    {
        if (blockId == Block.Leaves.Id || blockId == Block.Cobweb.Id)
        {
            itemStack.DamageItem(1, player);
        }

        return false;
    }

    public bool IsSuitableFor(Item item, Block block) => block.Id == Block.Cobweb.Id;
}
