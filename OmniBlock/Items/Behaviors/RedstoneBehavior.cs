using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class RedstoneBehavior : IItemBehavior
{
    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (world.Reader.GetBlockId(x, y, z) != world.Content.Blocks.Get("omniblock:snow").Id)
        {
            switch (meta)
            {
                case 0:
                    --y;
                    break;
                case 1:
                    ++y;
                    break;
                case 2:
                    --z;
                    break;
                case 3:
                    ++z;
                    break;
                case 4:
                    --x;
                    break;
                case 5:
                    ++x;
                    break;
            }

            if (!world.Reader.IsAir(x, y, z))
            {
                return false;
            }
        }

        var redstoneWire = world.Content.Blocks.Get("omniblock:redstone_wire");

        if (!redstoneWire.CanPlaceAt(new CanPlaceAtContext(world, 0, x, y, z))) return true;

        itemStack.ConsumeItem(player);
        world.Writer.SetBlock(x, y, z, redstoneWire.Id);

        return true;
    }
}
