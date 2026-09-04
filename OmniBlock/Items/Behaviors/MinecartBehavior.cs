using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class MinecartBehavior : IItemBehavior
{
    private readonly int _minecartType;

    internal MinecartBehavior(int minecartType) => _minecartType = minecartType;

    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        var blockId = world.Reader.GetBlockId(x, y, z);
        if (!RailBehavior.IsRail(BlockRegistry.GetByProtocolId(blockId)))
        {
            return false;
        }

        if (!world.IsRemote)
        {
            world.SpawnEntity(Entities.Behaviors.MinecartBehavior.Place(world, x + 0.5F, y + 0.5F, z + 0.5F, _minecartType));
        }

        itemStack.ConsumeItem(player);
        return true;
    }
}
