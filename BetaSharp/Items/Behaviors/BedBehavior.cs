using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class BedBehavior : IItemBehavior
{
    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (meta != 1)
        {
            return false;
        }

        ++y;
        int dir = MathHelper.Floor(player.Yaw * 4.0F / 360.0F + 0.5D) & 3;
        int offsetX = 0;
        int offsetZ = 0;
        if (dir == 0)
        {
            offsetZ = 1;
        }

        if (dir == 1)
        {
            offsetX = -1;
        }

        if (dir == 2)
        {
            offsetZ = -1;
        }

        if (dir == 3)
        {
            offsetX = 1;
        }

        bool footReplaceable = IsReplaceable(world, x, y, z);
        bool headReplaceable = IsReplaceable(world, x + offsetX, y, z + offsetZ);
        bool footSupported = world.Reader.ShouldSuffocate(x, y - 1, z);
        bool headSupported = world.Reader.ShouldSuffocate(x + offsetX, y - 1, z + offsetZ);

        if (!footReplaceable || !headReplaceable || !footSupported || !headSupported)
        {
            return false;
        }

        world.Writer.SetBlock(x, y, z, BlockRegistry.Get("bed").Id, dir);
        world.Writer.SetBlock(x + offsetX, y, z + offsetZ, BlockRegistry.Get("bed").Id, dir + 8);
        world.Broadcaster.NotifyNeighbors(x, y, z, BlockRegistry.Get("bed").Id);
        world.Broadcaster.NotifyNeighbors(x + offsetX, y, z + offsetZ, BlockRegistry.Get("bed").Id);
        itemStack.ConsumeItem(player);
        return true;
    }

    private static bool IsReplaceable(IWorldContext world, int x, int y, int z)
    {
        int blockId = world.Reader.GetBlockId(x, y, z);
        return blockId == 0 || Block.Blocks[blockId].Material.IsReplaceable;
    }
}
