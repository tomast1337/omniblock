using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Items.Behaviors;

namespace OmniBlock.Items;

internal class ItemCloth : ItemBlock
{
    public ItemCloth(Block block) : base(block)
    {
        SetMaxDamage(0);
        SetHasSubtypes(true);
    }

    public override int GetTextureId(int meta) => Block.GetTexture(2.ToSide(), ClothVisualBehavior.GetBlockMeta(meta));

    protected override int GetPlacementMetadata(int meta) => meta;

    public override string GetItemNameIs(ItemStack itemStack) => $"{base.GetItemName()}.{DyeBehavior.ColorNames[ClothVisualBehavior.GetBlockMeta(itemStack.GetDamage())]}";
}
