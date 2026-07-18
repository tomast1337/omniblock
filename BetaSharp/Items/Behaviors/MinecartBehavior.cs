using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class MinecartBehavior : IItemBehavior
{
    private readonly int _minecartType;

    internal MinecartBehavior(int minecartType) => _minecartType = minecartType;

    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        int blockId = world.Reader.GetBlockId(x, y, z);
        if (!BlockRail.isRail(blockId))
        {
            return false;
        }

        if (!world.IsRemote)
        {
            world.SpawnEntity(new EntityMinecart(world, x + 0.5F, y + 0.5F, z + 0.5F, _minecartType));
        }

        itemStack.ConsumeItem(player);
        return true;
    }
}
