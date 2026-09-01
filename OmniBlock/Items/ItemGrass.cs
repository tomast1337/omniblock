using OmniBlock.Blocks;

namespace OmniBlock.Items;

internal class ItemGrass : ItemBlock
{
    public ItemGrass(Block block) : base(block)
    {
        SetMaxDamage(0);
        SetHasSubtypes(true);
    }

    public override int GetTextureId(int meta) => Block.GetTexture(2.ToSide(), meta);

    protected override int GetPlacementMetadata(int meta) => meta;
}
