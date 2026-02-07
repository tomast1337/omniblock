using betareborn.Containers;
using betareborn.Entities;
using betareborn.Items;

namespace betareborn
{
    public class InventoryCrafting : java.lang.Object, IInventory
    {
        private ItemStack[] stackList;
        private int field_21104_b;
        private Container eventHandler;

        public InventoryCrafting(Container var1, int var2, int var3)
        {
            int var4 = var2 * var3;
            stackList = new ItemStack[var4];
            eventHandler = var1;
            field_21104_b = var2;
        }

        public int getSizeInventory()
        {
            return stackList.Length;
        }

        public ItemStack getStackInSlot(int var1)
        {
            return var1 >= getSizeInventory() ? null : stackList[var1];
        }

        public ItemStack func_21103_b(int var1, int var2)
        {
            if (var1 >= 0 && var1 < field_21104_b)
            {
                int var3 = var1 + var2 * field_21104_b;
                return getStackInSlot(var3);
            }
            else
            {
                return null;
            }
        }

        public string getInvName()
        {
            return "Crafting";
        }

        public ItemStack decrStackSize(int var1, int var2)
        {
            if (stackList[var1] != null)
            {
                ItemStack var3;
                if (stackList[var1].stackSize <= var2)
                {
                    var3 = stackList[var1];
                    stackList[var1] = null;
                    eventHandler.onCraftMatrixChanged(this);
                    return var3;
                }
                else
                {
                    var3 = stackList[var1].splitStack(var2);
                    if (stackList[var1].stackSize == 0)
                    {
                        stackList[var1] = null;
                    }

                    eventHandler.onCraftMatrixChanged(this);
                    return var3;
                }
            }
            else
            {
                return null;
            }
        }

        public void setInventorySlotContents(int var1, ItemStack var2)
        {
            stackList[var1] = var2;
            eventHandler.onCraftMatrixChanged(this);
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