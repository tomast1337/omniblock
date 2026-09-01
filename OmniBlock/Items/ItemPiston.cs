using OmniBlock.Blocks;

namespace OmniBlock.Items;

internal class ItemPiston(Block block) : ItemBlock(block)
{
    protected override int GetPlacementMetadata(int meta) => 7;
}
