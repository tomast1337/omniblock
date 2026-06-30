using BetaSharp.Blocks;

namespace BetaSharp.Items;

internal class ItemSapling : ItemBlock
{
    public ItemSapling(int id) : base(id)
    {
        setMaxDamage(0);
        setHasSubtypes(true);
    }

    public override int getPlacementMetadata(int meta) => meta;

    public override int getTextureId(int meta) => Block.Sapling.GetTexture(0, meta);
}
