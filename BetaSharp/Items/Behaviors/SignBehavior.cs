using BetaSharp.Blocks;
using BetaSharp.Blocks.Entities;
using BetaSharp.Entities;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

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

        if (!Block.Sign.canPlaceAt(new CanPlaceAtContext(world, 0, x, y, z)))
        {
            return false;
        }

        if (meta == 1)
        {
            world.Writer.SetBlock(x, y, z, Block.Sign.id, MathHelper.Floor((player.Yaw + 180.0F) * 16.0F / 360.0F + 0.5D) & 15);
        }
        else
        {
            world.Writer.SetBlock(x, y, z, Block.WallSign.id, meta);
        }

        itemStack.ConsumeItem(player);
        BlockEntitySign? blockEntitySign = world.Entities.GetBlockEntity<BlockEntitySign>(x, y, z);
        if (blockEntitySign != null)
        {
            player.openEditSignScreen(blockEntitySign);
        }

        return true;
    }
}
