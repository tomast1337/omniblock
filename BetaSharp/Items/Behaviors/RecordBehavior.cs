using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

public sealed class RecordBehavior : IItemBehavior
{
    public RecordBehavior(string recordName) => RecordName = recordName;

    public string RecordName { get; }

    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (world.Reader.GetBlockId(x, y, z) != Block.Jukebox.id || world.Reader.GetBlockMeta(x, y, z) != 0)
        {
            return false;
        }

        if (world.IsRemote)
        {
            return true;
        }

        BlockJukeBox.insertRecord(world, x, y, z, item.Id);
        world.Broadcaster.WorldEvent(1005, x, y, z, item.Id);
        itemStack.ConsumeItem(player);
        return true;
    }
}
