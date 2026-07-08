using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class RedstoneBehavior : IItemBehavior
{
    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (world.Reader.GetBlockId(x, y, z) != BlockRegistry.Get("snow").Id)
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

            if (!world.Reader.IsAir(x, y, z))
            {
                return false;
            }
        }

        if (BlockRegistry.Get("redstone_wire").CanPlaceAt(new CanPlaceAtContext(world, 0, x, y, z)))
        {
            itemStack.ConsumeItem(player);
            world.Writer.SetBlock(x, y, z, BlockRegistry.Get("redstone_wire").Id);
        }

        return true;
    }
}
