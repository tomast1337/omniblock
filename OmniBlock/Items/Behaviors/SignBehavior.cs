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
        if (meta == 0) return false;

        if (!world.Reader.GetMaterial(x, y, z).IsSolid) return false;

        switch (meta)
        {
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

        var sign = world.Content.Blocks.Get("omniblock:sign");
        if (!sign.CanPlaceAt(new CanPlaceAtContext(world, 0, x, y, z))) return false;

        if (meta == 1)
            world.Writer.SetBlock(x, y, z, sign.Id, MathHelper.Floor((player.Yaw + 180.0F) * 16.0F / 360.0F + 0.5D) & 15);
        else
            world.Writer.SetBlock(x, y, z, world.Content.Blocks.Get("omniblock:wall_sign").Id, meta);

        itemStack.ConsumeItem(player);
        var blockEntitySign = world.Entities.GetBlockEntity<BlockEntitySign>(x, y, z);
        if (blockEntitySign != null)
            player.openEditSignScreen(blockEntitySign);

        return true;
    }
}
