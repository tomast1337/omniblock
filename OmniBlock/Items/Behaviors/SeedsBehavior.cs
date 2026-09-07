using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items.Behaviors;

internal sealed class SeedsBehavior : IItemBehavior
{
    // Deferred: SeedsBehaviorDefinition.Build() runs during ItemFactory.Create(), before
    // the builder has constructed any block drafts.
    private readonly Func<int> _blockIdFactory;

    internal SeedsBehavior(Func<int> blockId) => _blockIdFactory = blockId;
    private int _blockId => _blockIdFactory();

    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (meta != 1)
        {
            return false;
        }

        var blockId = world.Reader.GetBlockId(x, y, z);
        if (blockId == world.Content.Blocks.Get("omniblock:farmland").Id && world.Reader.IsAir(x, y + 1, z))
        {
            world.Writer.SetBlock(x, y + 1, z, _blockId);
            itemStack.ConsumeItem(player);
            return true;
        }

        return false;
    }
}
