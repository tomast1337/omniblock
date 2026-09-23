using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class PaintingBehavior : IItemBehavior
{
    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (meta is 0 or 1) return false;

        byte direction = meta switch
        {
            4 => 1,
            3 => 2,
            5 => 3,
            _ => 0
        };

        var painting = HangingArtBehavior.HangAt(world, x, y, z, direction);
        if (!painting.Behaviors.Find<HangingArtBehavior>()!.CanHang(painting)) return true;

        if (!world.IsRemote) world.SpawnEntity(painting);

        itemStack.ConsumeItem(player);
        return true;
    }
}
