using BetaSharp.Blocks;

namespace BetaSharp.Items;

internal class ItemGrass : ItemBlock
{
    public ItemGrass(int id) : base(id)
    {
        SetMaxDamage(0);
        SetHasSubtypes(true);
    }

    public override int GetTextureId(int meta) => BlockRegistry.Get("grass").GetTexture(2.ToSide(), meta);

    protected override int GetPlacementMetadata(int meta) => meta;
}
