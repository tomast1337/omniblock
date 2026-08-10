using OmniBlock.Blocks;
using OmniBlock.Entities;

namespace OmniBlock.Items.Behaviors;

internal sealed class ShearsBehavior : IItemBehavior
{
    public float GetMiningSpeedMultiplier(Item item, ItemStack itemStack, Block block)
    {
        if (block.Id == BlockRegistry.Get("cobweb").Id || block.Id == BlockRegistry.Get("leaves").Id)
        {
            return 15.0F;
        }

        if (block.Id == BlockRegistry.Get("wool").Id)
        {
            return 5.0F;
        }

        return 1.0F;
    }

    public bool PostMine(Item item, ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving player)
    {
        if (blockId == BlockRegistry.Get("leaves").Id || blockId == BlockRegistry.Get("cobweb").Id)
        {
            itemStack.DamageItem(1, player);
        }

        return false;
    }

    public bool IsSuitableFor(Item item, Block block) => block.Id == BlockRegistry.Get("cobweb").Id;
}
