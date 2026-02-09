using betareborn.Blocks;
using betareborn.Entities;

namespace betareborn.Items
{
    public class ItemTool : Item
    {

        private Block[] blocksEffectiveAgainst;
        private float efficiencyOnProperMaterial = 4.0F;
        private int damageVsEntity;
        protected EnumToolMaterial toolMaterial;

        protected ItemTool(int var1, int var2, EnumToolMaterial var3, Block[] var4) : base(var1)
        {
            toolMaterial = var3;
            blocksEffectiveAgainst = var4;
            maxCount = 1;
            setMaxDamage(var3.getMaxUses());
            efficiencyOnProperMaterial = var3.getEfficiencyOnProperMaterial();
            damageVsEntity = var2 + var3.getDamageVsEntity();
        }

        public override float getMiningSpeedMultiplier(ItemStack var1, Block var2)
        {
            for (int var3 = 0; var3 < blocksEffectiveAgainst.Length; ++var3)
            {
                if (blocksEffectiveAgainst[var3] == var2)
                {
                    return efficiencyOnProperMaterial;
                }
            }

            return 1.0F;
        }

        public override bool postHit(ItemStack var1, EntityLiving var2, EntityLiving var3)
        {
            var1.damageItem(2, var3);
            return true;
        }

        public override bool postMine(ItemStack var1, int var2, int var3, int var4, int var5, EntityLiving var6)
        {
            var1.damageItem(1, var6);
            return true;
        }

        public override int getAttackDamage(Entity var1)
        {
            return damageVsEntity;
        }

        public override bool isHandheld()
        {
            return true;
        }
    }

}