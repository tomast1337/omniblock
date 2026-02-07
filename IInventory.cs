using betareborn.Entities;
using betareborn.Items;

namespace betareborn
{
    public interface IInventory
    {
        int getSizeInventory();

        ItemStack getStackInSlot(int var1);

        ItemStack decrStackSize(int var1, int var2);

        void setInventorySlotContents(int var1, ItemStack var2);

        string getInvName();

        int getInventoryStackLimit();

        void markDirty();

        bool canInteractWith(EntityPlayer var1);
    }

}