using OmniBlock.Blocks;

namespace OmniBlock.Items;

internal class ItemSapling : ItemBlock
{
    public ItemSapling(Block block) : base(block)
    {
        SetMaxDamage(0);
        SetHasSubtypes(true);
    }

    protected override int GetPlacementMetadata(int meta) => meta;

    public override int GetTextureId(int meta) => Block.GetTexture(0, meta);
}
