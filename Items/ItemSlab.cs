using betareborn.Blocks;

namespace betareborn.Items
{
    public class ItemSlab : ItemBlock
    {

        public ItemSlab(int var1) : base(var1)
        {
            setMaxDamage(0);
            setHasSubtypes(true);
        }

        public override int getTextureId(int var1)
        {
            return Block.SLAB.getTexture(2, var1);
        }

        public override int getPlacementMetadata(int var1)
        {
            return var1;
        }

        public override String getItemNameIS(ItemStack var1)
        {
            return base.getItemName() + "." + BlockSlab.names[var1.getDamage()];
        }
    }

}