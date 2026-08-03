using BetaSharp.Blocks;

namespace BetaSharp.Items;

internal class ItemLog : ItemBlock
{
    public ItemLog(int id) : base(id)
    {
        SetMaxDamage(0);
        SetHasSubtypes(true);
    }

    public override int GetTextureId(int meta) => BlockRegistry.Get("log").GetTexture(2.ToSide(), meta);

    protected override int GetPlacementMetadata(int meta) => meta;
}
