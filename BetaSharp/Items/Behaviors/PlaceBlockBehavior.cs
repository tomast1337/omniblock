using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class PlaceBlockBehavior : IItemBehavior
{
    private readonly int _blockId;

    internal PlaceBlockBehavior(Block block) => _blockId = block.id;

    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (world.Reader.GetBlockId(x, y, z) == Block.Snow.id)
        {
            meta = 0;
        }
        else
        {
            switch (meta)
            {
                case 0: --y; break;
                case 1: ++y; break;
                case 2: --z; break;
                case 3: ++z; break;
                case 4: --x; break;
                case 5: ++x; break;
            }
        }

        if (itemStack.Count == 0)
        {
            return false;
        }

        if (Block.Blocks[_blockId].canPlaceAt(new CanPlaceAtContext(world, 0, x, y, z)))
        {
            Block block = Block.Blocks[_blockId];
            if (world.Writer.SetBlock(x, y, z, _blockId))
            {
                Block.Blocks[_blockId].onPlaced(new OnPlacedEvent(world, player, meta.ToSide(), meta.ToSide(), x, y, z));
                world.Broadcaster.PlaySoundAtEntity(player, block.SoundGroup.StepSound, (block.SoundGroup.Volume + 1.0F) / 2.0F, block.SoundGroup.Pitch * 0.8F);
                itemStack.ConsumeItem(player);
            }
        }

        return true;
    }
}
