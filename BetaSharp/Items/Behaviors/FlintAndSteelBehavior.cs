using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class FlintAndSteelBehavior : IItemBehavior
{
    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (meta == 0)
        {
            --y;
        }

        if (meta == 1)
        {
            ++y;
        }

        if (meta == 2)
        {
            --z;
        }

        if (meta == 3)
        {
            ++z;
        }

        if (meta == 4)
        {
            --x;
        }

        if (meta == 5)
        {
            ++x;
        }

        int blockId = world.Reader.GetBlockId(x, y, z);
        if (blockId == 0)
        {
            world.Broadcaster.PlaySoundAtPos(x + 0.5D, y + 0.5D, z + 0.5D, "fire.ignite", 1.0F, Item.itemRand.NextFloat() * 0.4F + 0.8F);
            world.Writer.SetBlock(x, y, z, Block.Fire.id);
        }

        itemStack.DamageItem(1, player);
        return true;
    }
}
