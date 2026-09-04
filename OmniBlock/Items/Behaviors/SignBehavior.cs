using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class SignBehavior : IItemBehavior
{
    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (meta == 0)
        {
            return false;
        }

        if (!world.Reader.GetMaterial(x, y, z).IsSolid)
        {
            return false;
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

        if (!BlockRegistry.Get("sign").CanPlaceAt(new CanPlaceAtContext(world, 0, x, y, z)))
        {
            return false;
        }

        if (meta == 1)
        {
            world.Writer.SetBlock(x, y, z, BlockRegistry.Get("sign").Id, MathHelper.Floor((player.Yaw + 180.0F) * 16.0F / 360.0F + 0.5D) & 15);
        }
        else
        {
            world.Writer.SetBlock(x, y, z, BlockRegistry.Get("wall_sign").Id, meta);
        }

        itemStack.ConsumeItem(player);
        var blockEntitySign = world.Entities.GetBlockEntity<BlockEntitySign>(x, y, z);
        if (blockEntitySign != null)
        {
            player.openEditSignScreen(blockEntitySign);
        }

        return true;
    }
}
