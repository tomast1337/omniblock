using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Items.Behaviors;

internal sealed class SeedsBehavior : IItemBehavior
{
    // Deferred: SeedsBehaviorDefinition.Build() runs during ItemFactory.Create(), before
    // BlockRegistry.Initialize() has loaded any blocks.
    private readonly Func<int> _blockIdFactory;
    private int _blockId => _blockIdFactory();

    internal SeedsBehavior(Func<int> blockId) => _blockIdFactory = blockId;

    public bool UseOnBlock(Item item, ItemStack itemStack, EntityPlayer player, IWorldContext world, int x, int y, int z, int meta)
    {
        if (meta != 1)
        {
            return false;
        }

        int blockId = world.Reader.GetBlockId(x, y, z);
        if (blockId == BlockRegistry.Get("farmland").id && world.Reader.IsAir(x, y + 1, z))
        {
            world.Writer.SetBlock(x, y + 1, z, _blockId);
            itemStack.ConsumeItem(player);
            return true;
        }

        return false;
    }
}
