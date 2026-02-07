using betareborn.Entities;
using betareborn.Items;
using java.util;

namespace betareborn
{
    public class InventoryBasic : IInventory
    {

        private String inventoryTitle;
        private int slotsCount;
        private ItemStack[] inventoryContents;
        private List field_20073_d;

        public InventoryBasic(String var1, int var2)
        {
            inventoryTitle = var1;
            slotsCount = var2;
            inventoryContents = new ItemStack[var2];
        }

        public ItemStack getStackInSlot(int var1)
        {
            return inventoryContents[var1];
        }

        public ItemStack decrStackSize(int var1, int var2)
        {
            if (inventoryContents[var1] != null)
            {
                ItemStack var3;
                if (inventoryContents[var1].stackSize <= var2)
                {
                    var3 = inventoryContents[var1];
                    inventoryContents[var1] = null;
                    markDirty();
                    return var3;
                }
                else
                {
                    var3 = inventoryContents[var1].splitStack(var2);
                    if (inventoryContents[var1].stackSize == 0)
                    {
                        inventoryContents[var1] = null;
                    }

                    markDirty();
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
            inventoryContents[var1] = var2;
            if (var2 != null && var2.stackSize > getInventoryStackLimit())
            {
                var2.stackSize = getInventoryStackLimit();
            }

            markDirty();
        }

        public int getSizeInventory()
        {
            return slotsCount;
        }

        public String getInvName()
        {
            return inventoryTitle;
        }

        public int getInventoryStackLimit()
        {
            return 64;
        }

        public void markDirty()
        {
            if (field_20073_d != null)
            {
                for (int var1 = 0; var1 < field_20073_d.size(); ++var1)
                {
                    ((IInvBasic)field_20073_d.get(var1)).func_20134_a(this);
                }
            }

        }

        public bool canInteractWith(EntityPlayer var1)
        {
            return true;
        }
    }

}