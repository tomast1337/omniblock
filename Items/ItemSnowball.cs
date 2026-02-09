using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Items
{

    public class ItemSnowball : Item
    {

        public ItemSnowball(int var1) : base(var1)
        {
            maxCount = 16;
        }

        public override ItemStack use(ItemStack var1, World var2, EntityPlayer var3)
        {
            --var1.count;
            var2.playSound(var3, "random.bow", 0.5F, 0.4F / (itemRand.nextFloat() * 0.4F + 0.8F));
            if (!var2.isRemote)
            {
                var2.spawnEntity(new EntitySnowball(var2, var3));
            }

            return var1;
        }
    }

}