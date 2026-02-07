using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Items
{
    public class ItemFood : Item
    {

        private int healAmount;
        private bool isWolfsFavoriteMeat;

        public ItemFood(int var1, int var2, bool var3) : base(var1)
        {
            healAmount = var2;
            isWolfsFavoriteMeat = var3;
            maxStackSize = 1;
        }

        public override ItemStack onItemRightClick(ItemStack var1, World var2, EntityPlayer var3)
        {
            --var1.count;
            var3.heal(healAmount);
            return var1;
        }

        public int getHealAmount()
        {
            return healAmount;
        }

        public bool getIsWolfsFavoriteMeat()
        {
            return isWolfsFavoriteMeat;
        }
    }

}