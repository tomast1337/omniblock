using BetaSharp.Blocks;
using BetaSharp.Worlds.Colors;

namespace BetaSharp.Items;

internal class ItemLeaves : ItemBlock
{
    public ItemLeaves(int id) : base(id)
    {
        setMaxDamage(0);
        setHasSubtypes(true);
    }

    public override int getPlacementMetadata(int meta) => meta | 8;

    public override int getTextureId(int meta) => Block.Leaves.GetTexture(0, meta);

    public override int getColorMultiplier(int leafType) => (leafType & 1) == 1 ? FoliageColors.getSpruceColor() : (leafType & 2) == 2 ? FoliageColors.getBirchColor() : FoliageColors.getDefaultColor();
}
