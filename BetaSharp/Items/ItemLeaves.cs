using OmniBlock.Blocks;
using OmniBlock.Worlds.Colors;

namespace OmniBlock.Items;

internal class ItemLeaves : ItemBlock
{
    public ItemLeaves(int id) : base(id)
    {
        SetMaxDamage(0);
        SetHasSubtypes(true);
    }

    protected override int GetPlacementMetadata(int meta) => meta | 8;

    public override int GetTextureId(int meta) => BlockRegistry.Get("leaves").GetTexture(0, meta);

    public override int GetColorMultiplier(int leafType) => (leafType & 1) == 1 ? FoliageColors.getSpruceColor() : (leafType & 2) == 2 ? FoliageColors.getBirchColor() : FoliageColors.getDefaultColor();
}
