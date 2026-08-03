using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Items.Behaviors;

namespace BetaSharp.Items;

internal class ItemCloth : ItemBlock
{
    public ItemCloth(int id) : base(id)
    {
        SetMaxDamage(0);
        SetHasSubtypes(true);
    }

    public override int GetTextureId(int meta) => BlockRegistry.Get("wool").GetTexture(2.ToSide(), ClothVisualBehavior.GetBlockMeta(meta));

    protected override int GetPlacementMetadata(int meta) => meta;

    public override string GetItemNameIs(ItemStack itemStack) => base.GetItemName() + "." + DyeBehavior.ColorNames[ClothVisualBehavior.GetBlockMeta(itemStack.getDamage())];
}
