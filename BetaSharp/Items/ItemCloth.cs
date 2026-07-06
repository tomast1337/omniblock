using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Items.Behaviors;

namespace BetaSharp.Items;

internal class ItemCloth : ItemBlock
{
    public ItemCloth(int id) : base(id)
    {
        setMaxDamage(0);
        setHasSubtypes(true);
    }

    public override int getTextureId(int meta) => Block.Wool.GetTexture(2.ToSide(), ClothVisualBehavior.getBlockMeta(meta));

    public override int getPlacementMetadata(int meta) => meta;

    public override string getItemNameIS(ItemStack itemStack) => base.getItemName() + "." + DyeBehavior.ColorNames[ClothVisualBehavior.getBlockMeta(itemStack.getDamage())];
}
