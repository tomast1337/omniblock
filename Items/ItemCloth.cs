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

        public override int getTextureId(int var1)
        {
            return Block.WOOL.getTexture(2, BlockCloth.getBlockMeta(var1));
        }

        public override int getPlacementMetadata(int var1)
        {
            return var1;
        }

        public override String getItemNameIS(ItemStack var1)
        {
            return base.getItemName() + "." + ItemDye.dyeColors[BlockCloth.getBlockMeta(var1.getDamage())];
        }
    }

}