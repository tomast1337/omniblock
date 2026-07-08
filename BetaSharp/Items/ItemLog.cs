using BetaSharp.Blocks;

namespace BetaSharp.Items;

internal class ItemLog : ItemBlock
{
    public ItemLog(int id) : base(id)
    {
        setMaxDamage(0);
        setHasSubtypes(true);
    }

    public override int getTextureId(int meta) => BlockRegistry.Get("log").GetTexture(2.ToSide(), meta);

    public override int getPlacementMetadata(int meta) => meta;
}
