using betareborn.Entities;
using betareborn.Items;

namespace betareborn
{
    public class InventoryLargeChest : java.lang.Object, IInventory
    {
        private string name;
        private IInventory upperChest;
        private IInventory lowerChest;

        public InventoryLargeChest(string var1, IInventory var2, IInventory var3)
        {
            name = var1;
            upperChest = var2;
            lowerChest = var3;
        }

        public int getSizeInventory()
        {
            return upperChest.getSizeInventory() + lowerChest.getSizeInventory();
        }

        public string getInvName()
        {
            return name;
        }

        public ItemStack getStackInSlot(int var1)
        {
            return var1 >= upperChest.getSizeInventory() ? lowerChest.getStackInSlot(var1 - upperChest.getSizeInventory()) : upperChest.getStackInSlot(var1);
        }

        public ItemStack decrStackSize(int var1, int var2)
        {
            return var1 >= upperChest.getSizeInventory() ? lowerChest.decrStackSize(var1 - upperChest.getSizeInventory(), var2) : upperChest.decrStackSize(var1, var2);
        }

        public void setInventorySlotContents(int var1, ItemStack var2)
        {
            if (var1 >= upperChest.getSizeInventory())
            {
                lowerChest.setInventorySlotContents(var1 - upperChest.getSizeInventory(), var2);
            }
            else
            {
                upperChest.setInventorySlotContents(var1, var2);
            }

        }

        public int getInventoryStackLimit()
        {
            return upperChest.getInventoryStackLimit();
        }

        public void markDirty()
        {
            upperChest.markDirty();
            lowerChest.markDirty();
        }

        public bool canInteractWith(EntityPlayer var1)
        {
            return upperChest.canInteractWith(var1) && lowerChest.canInteractWith(var1);
        }
    }

}