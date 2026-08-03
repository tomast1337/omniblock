using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Items;

internal class ItemSlab : ItemBlock
{
    public ItemSlab(int id) : base(id)
    {
        SetMaxDamage(0);
        SetHasSubtypes(true);
    }

    public override int GetTextureId(int meta) => BlockRegistry.Get("slab").GetTexture(2.ToSide(), meta);

    protected override int GetPlacementMetadata(int meta) => meta;

    public override string GetItemNameIs(ItemStack itemStack)
    {
        if (SlabBehavior.Names.Length > itemStack.getDamage())
        {
            return base.GetItemName() + "." + SlabBehavior.Names[itemStack.getDamage()];
        }

        return "";
    }
}
