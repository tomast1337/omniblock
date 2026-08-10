using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;

namespace OmniBlock.Items;

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
        return SlabBehavior.Names.Length > itemStack.GetDamage() ? $"{base.GetItemName()}.{SlabBehavior.Names[itemStack.GetDamage()]}" : "";
    }
}
