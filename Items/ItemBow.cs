using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Items
{
    public class ItemBow : Item
    {

        public ItemBow(int var1) : base(var1)
        {
            maxCount = 1;
        }

        public override ItemStack use(ItemStack var1, World var2, EntityPlayer var3)
        {
            if (var3.inventory.consumeInventoryItem(Item.ARROW.id))
            {
                var2.playSound(var3, "random.bow", 1.0F, 1.0F / (itemRand.nextFloat() * 0.4F + 0.8F));
                if (!var2.isRemote)
                {
                    var2.spawnEntity(new EntityArrow(var2, var3));
                }
            }

            return var1;
        }
    }

}