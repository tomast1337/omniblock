using BetaSharp.Blocks;
using BetaSharp.Entities;

namespace BetaSharp.Items.Behaviors;

internal sealed class ShearsBehavior : IItemBehavior
{
    public float GetMiningSpeedMultiplier(Item item, ItemStack itemStack, Block block)
    {
        if (block.id == Block.Cobweb.id || block.id == Block.Leaves.id)
        {
            return 15.0F;
        }

        if (block.id == Block.Wool.id)
        {
            return 5.0F;
        }

        return 1.0F;
    }

    public bool PostMine(Item item, ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving player)
    {
        if (blockId == Block.Leaves.id || blockId == Block.Cobweb.id)
        {
            itemStack.DamageItem(1, player);
        }

        return false;
    }

    public bool IsSuitableFor(Item item, Block block) => block.id == Block.Cobweb.id;
}
