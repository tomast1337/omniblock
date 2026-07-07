using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;

namespace BetaSharp.Items;

internal class ItemSlab : ItemBlock
{
    public ItemSlab(int id) : base(id)
    {
        setMaxDamage(0);
        setHasSubtypes(true);
    }

    public override int getTextureId(int meta) => Block.Slab.GetTexture(2.ToSide(), meta);

    public override int getPlacementMetadata(int meta) => meta;

    public override string getItemNameIS(ItemStack itemStack)
    {
        if (SlabBehavior.Names.Length > itemStack.getDamage())
        {
            return base.getItemName() + "." + SlabBehavior.Names[itemStack.getDamage()];
        }

        return "";
    }
}
