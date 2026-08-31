using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class PlaceBlockBehavior : IItemBehavior
{
    // Deferred: PlaceBlockBehaviorDefinition.Build() runs during ItemFactory.Create(), before
    // BlockRegistry.Initialize() has loaded any blocks.
    private readonly Func<Block> _blockFactory;
    private int _blockId => _blockFactory().Id;

    internal PlaceBlockBehavior(Func<Block> block) => _blockFactory = block;

    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (world.Reader.GetBlockId(x, y, z) == BlockRegistry.Get("snow").Id)
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

        Block block = BlockRegistry.GetByProtocolId(_blockId);
        if (block.CanPlaceAt(new CanPlaceAtContext(world, 0, x, y, z)))
        {
            if (world.Writer.SetBlock(x, y, z, _blockId))
            {
                block.OnPlaced(new OnPlacedEvent(world, player, meta.ToSide(), meta.ToSide(), x, y, z));
                world.Broadcaster.PlaySoundAtEntity(player, block.SoundGroup.StepSound, (block.SoundGroup.Volume + 1.0F) / 2.0F, block.SoundGroup.Pitch * 0.8F);
                itemStack.ConsumeItem(player);
            }
        }

        return true;
    }
}
