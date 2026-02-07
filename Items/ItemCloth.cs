using betareborn.Blocks;

namespace betareborn.Items
{
    public class ItemCloth : ItemBlock
    {

        public ItemCloth(int var1) : base(var1)
        {
            setMaxDamage(0);
            setHasSubtypes(true);
        }

        public override int getIconFromDamage(int var1)
        {
            return Block.WOOL.getTexture(2, BlockCloth.func_21034_c(var1));
        }

        public override int getPlacedBlockMetadata(int var1)
        {
            return var1;
        }

        public override String getItemNameIS(ItemStack var1)
        {
            return base.getItemName() + "." + ItemDye.dyeColors[BlockCloth.func_21034_c(var1.getItemDamage())];
        }
    }

}