using betareborn.Blocks;
using betareborn.Entities;

namespace betareborn.Items
{
    public class ItemSword : Item
    {

        private int weaponDamage;

        public ItemSword(int var1, EnumToolMaterial var2) : base(var1)
        {
            maxStackSize = 1;
            setMaxDamage(var2.getMaxUses());
            weaponDamage = 4 + var2.getDamageVsEntity() * 2;
        }

        public override float getStrVsBlock(ItemStack var1, Block var2)
        {
            return var2.id == Block.COBWEB.id ? 15.0F : 1.5F;
        }

        public override bool hitEntity(ItemStack var1, EntityLiving var2, EntityLiving var3)
        {
            var1.damageItem(1, var3);
            return true;
        }

        public override bool onBlockDestroyed(ItemStack var1, int var2, int var3, int var4, int var5, EntityLiving var6)
        {
            var1.damageItem(2, var6);
            return true;
        }

        public override int getDamageVsEntity(Entity var1)
        {
            return weaponDamage;
        }

        public override bool isFull3D()
        {
            return true;
        }

        public override bool canHarvestBlock(Block var1)
        {
            return var1.id == Block.COBWEB.id;
        }
    }

}