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
        var dir = MathHelper.Floor(player.Yaw * 4.0F / 360.0F + 0.5D) & 3;
        var offsetX = 0;
        var offsetZ = 0;
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

        var footReplaceable = IsReplaceable(world, x, y, z);
        var headReplaceable = IsReplaceable(world, x + offsetX, y, z + offsetZ);
        var footSupported = world.Reader.ShouldSuffocate(x, y - 1, z);
        var headSupported = world.Reader.ShouldSuffocate(x + offsetX, y - 1, z + offsetZ);

        if (!footReplaceable || !headReplaceable || !footSupported || !headSupported)
        {
            return false;
        }

        int bedId = world.Content.Blocks.Get("omniblock:bed").Id;
        world.Writer.SetBlock(x, y, z, bedId, dir);
        world.Writer.SetBlock(x + offsetX, y, z + offsetZ, bedId, dir + 8);
        world.Broadcaster.NotifyNeighbors(x, y, z, bedId);
        world.Broadcaster.NotifyNeighbors(x + offsetX, y, z + offsetZ, bedId);
        itemStack.ConsumeItem(player);
        return true;
    }

    private static bool IsReplaceable(IWorldContext world, int x, int y, int z)
    {
        var blockId = world.Reader.GetBlockId(x, y, z);
        return blockId == 0 || world.Content.Blocks.GetByProtocolId(blockId).Material.IsReplaceable;
    }
}
