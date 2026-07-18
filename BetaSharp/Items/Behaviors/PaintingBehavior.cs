using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class PaintingBehavior : IItemBehavior
{
    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (meta is 0 or 1)
        {
            return false;
        }

        byte direction = 0;
        if (meta == 4)
        {
            direction = 1;
        }

        if (meta == 3)
        {
            direction = 2;
        }

        if (meta == 5)
        {
            direction = 3;
        }

        EntityPainting painting = new(world, x, y, z, direction);
        if (!painting.CanHangOnWall())
        {
            return true;
        }

        if (!world.IsRemote)
        {
            world.SpawnEntity(painting);
        }

        itemStack.ConsumeItem(player);
        return true;
    }
}
