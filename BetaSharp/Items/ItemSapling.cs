using OmniBlock.Blocks;

namespace OmniBlock.Items;

internal class ItemSapling : ItemBlock
{
    public ItemSapling(int id) : base(id)
    {
        SetMaxDamage(0);
        SetHasSubtypes(true);
    }

    protected override int GetPlacementMetadata(int meta) => meta;

    public override int GetTextureId(int meta) => BlockRegistry.Get("sapling").GetTexture(0, meta);
}
