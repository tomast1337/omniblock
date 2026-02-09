using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Items
{
    public class ItemSoup : ItemFood
    {

        public ItemSoup(int var1, int var2) : base(var1, var2, false)
        {
        }

        public override ItemStack use(ItemStack var1, World var2, EntityPlayer var3)
        {
            base.use(var1, var2, var3);
            return new ItemStack(Item.BOWL);
        }
    }

}