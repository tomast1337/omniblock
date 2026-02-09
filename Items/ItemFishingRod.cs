using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Items
{
    public class ItemFishingRod : Item
    {

        public ItemFishingRod(int var1) : base(var1)
        {
            setMaxDamage(64);
            setMaxCount(1);
        }

        public override bool isHandheld()
        {
            return true;
        }

        public override bool isHandheldRod()
        {
            return true;
        }

        public override ItemStack use(ItemStack var1, World var2, EntityPlayer var3)
        {
            if (var3.fishEntity != null)
            {
                int var4 = var3.fishEntity.catchFish();
                var1.damageItem(var4, var3);
                var3.swingHand();
            }
            else
            {
                var2.playSound(var3, "random.bow", 0.5F, 0.4F / (itemRand.nextFloat() * 0.4F + 0.8F));
                if (!var2.isRemote)
                {
                    var2.spawnEntity(new EntityFish(var2, var3));
                }

                var3.swingHand();
            }

            return var1;
        }
    }

}