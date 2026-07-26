using BetaSharp.Blocks;

namespace BetaSharp.Items;

internal class ItemGrass : ItemBlock
{
    public ItemGrass(int id) : base(id)
    {
        setMaxDamage(0);
        setHasSubtypes(true);
    }

    public override int getTextureId(int meta) => BlockRegistry.Get("grass").GetTexture(2.ToSide(), meta);

    public override int getPlacementMetadata(int meta) => meta;
}
