using betareborn.Entities;
using betareborn.Items;

namespace betareborn
{
    public class InventoryCraftResult : IInventory
    {

        private ItemStack[] stackResult = new ItemStack[1];

        public int getSizeInventory()
        {
            return 1;
        }

        public ItemStack getStackInSlot(int var1)
        {
            return stackResult[var1];
        }

        public string getInvName()
        {
            return "Result";
        }

        public ItemStack decrStackSize(int var1, int var2)
        {
            if (stackResult[var1] != null)
            {
                ItemStack var3 = stackResult[var1];
                stackResult[var1] = null;
                return var3;
            }
            else
            {
                return null;
            }
        }

        public void setInventorySlotContents(int var1, ItemStack var2)
        {
            stackResult[var1] = var2;
        }

        public int getInventoryStackLimit()
        {
            return 64;
        }

        public void markDirty()
        {
        }

        public bool canInteractWith(EntityPlayer var1)
        {
            return true;
        }
    }

}