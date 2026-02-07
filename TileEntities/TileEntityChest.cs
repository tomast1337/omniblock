using betareborn.Entities;
using betareborn.Items;
using betareborn.NBT;

namespace betareborn.TileEntities
{
    public class TileEntityChest : TileEntity, IInventory
    {
        private ItemStack[] chestContents = new ItemStack[36];

        public int getSizeInventory()
        {
            return 27;
        }

        public ItemStack getStackInSlot(int var1)
        {
            return chestContents[var1];
        }

        public ItemStack decrStackSize(int var1, int var2)
        {
            if (chestContents[var1] != null)
            {
                ItemStack var3;
                if (chestContents[var1].stackSize <= var2)
                {
                    var3 = chestContents[var1];
                    chestContents[var1] = null;
                    markDirty();
                    return var3;
                }
                else
                {
                    var3 = chestContents[var1].splitStack(var2);
                    if (chestContents[var1].stackSize == 0)
                    {
                        chestContents[var1] = null;
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
            chestContents[var1] = var2;
            if (var2 != null && var2.stackSize > getInventoryStackLimit())
            {
                var2.stackSize = getInventoryStackLimit();
            }

            markDirty();
        }

        public string getInvName()
        {
            return "Chest";
        }

        public override void readNbt(NBTTagCompound var1)
        {
            base.readNbt(var1);
            NBTTagList var2 = var1.getTagList("Items");
            chestContents = new ItemStack[getSizeInventory()];

            for (int var3 = 0; var3 < var2.tagCount(); ++var3)
            {
                NBTTagCompound var4 = (NBTTagCompound)var2.tagAt(var3);
                int var5 = var4.getByte("Slot") & 255;
                if (var5 >= 0 && var5 < chestContents.Length)
                {
                    chestContents[var5] = new ItemStack(var4);
                }
            }

        }

        public override void writeNbt(NBTTagCompound var1)
        {
            base.writeNbt(var1);
            NBTTagList var2 = new NBTTagList();

            for (int var3 = 0; var3 < chestContents.Length; ++var3)
            {
                if (chestContents[var3] != null)
                {
                    NBTTagCompound var4 = new NBTTagCompound();
                    var4.setByte("Slot", (sbyte)var3);
                    chestContents[var3].writeToNBT(var4);
                    var2.setTag(var4);
                }
            }

            var1.setTag("Items", var2);
        }

        public int getInventoryStackLimit()
        {
            return 64;
        }

        public bool canInteractWith(EntityPlayer var1)
        {
            return world.getBlockTileEntity(x, y, z) != this ? false : var1.getDistanceSq((double)x + 0.5D, (double)y + 0.5D, (double)z + 0.5D) <= 64.0D;
        }
    }

}