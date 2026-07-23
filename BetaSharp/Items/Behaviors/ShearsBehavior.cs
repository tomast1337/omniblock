using BetaSharp.Blocks;
using BetaSharp.Entities;

namespace BetaSharp.Items.Behaviors;

internal sealed class ShearsBehavior : IItemBehavior
{
    public float GetMiningSpeedMultiplier(Item item, ItemStack itemStack, Block block)
    {
        if (block.id == BlockRegistry.Get("cobweb").id || block.id == BlockRegistry.Get("leaves").id)
        {
            return 15.0F;
        }

        if (block.id == BlockRegistry.Get("wool").id)
        {
            return 5.0F;
        }

        return 1.0F;
    }

    public bool PostMine(Item item, ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving player)
    {
        if (blockId == BlockRegistry.Get("leaves").id || blockId == BlockRegistry.Get("cobweb").id)
        {
            itemStack.DamageItem(1, player);
        }

        return false;
    }

    public bool IsSuitableFor(Item item, Block block) => block.id == BlockRegistry.Get("cobweb").id;
}
