using betareborn.Blocks;
using betareborn.Entities;

namespace betareborn.Items
{
    public class ItemShears : Item
    {

        public ItemShears(int var1) : base(var1)
        {
            setMaxStackSize(1);
            setMaxDamage(238);
        }

        public override bool onBlockDestroyed(ItemStack var1, int var2, int var3, int var4, int var5, EntityLiving var6)
        {
            if (var2 == Block.LEAVES.id || var2 == Block.COBWEB.id)
            {
                var1.damageItem(1, var6);
            }

            return base.onBlockDestroyed(var1, var2, var3, var4, var5, var6);
        }

        public override bool canHarvestBlock(Block var1)
        {
            return var1.id == Block.COBWEB.id;
        }

        public override float getStrVsBlock(ItemStack var1, Block var2)
        {
            return var2.id != Block.COBWEB.id && var2.id != Block.LEAVES.id ? (var2.id == Block.WOOL.id ? 5.0F : base.getStrVsBlock(var1, var2)) : 15.0F;
        }
    }

}