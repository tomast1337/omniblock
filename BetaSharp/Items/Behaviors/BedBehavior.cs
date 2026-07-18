using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

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

        world.Writer.SetBlock(x, y, z, Block.Bed.id, dir);
        world.Writer.SetBlock(x + offsetX, y, z + offsetZ, Block.Bed.id, dir + 8);
        world.Broadcaster.NotifyNeighbors(x, y, z, Block.Bed.id);
        world.Broadcaster.NotifyNeighbors(x + offsetX, y, z + offsetZ, Block.Bed.id);
        itemStack.ConsumeItem(player);
        return true;
    }

    private static bool IsReplaceable(IWorldContext world, int x, int y, int z)
    {
        int blockId = world.Reader.GetBlockId(x, y, z);
        return blockId == 0 || Block.Blocks[blockId].material.IsReplaceable;
    }
}
