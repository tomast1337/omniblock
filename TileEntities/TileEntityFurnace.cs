using betareborn.Blocks;
using betareborn.Entities;
using betareborn.Items;
using betareborn.Materials;
using betareborn.NBT;

namespace betareborn.TileEntities
{
    public class TileEntityFurnace : TileEntity, IInventory
    {
        private ItemStack[] furnaceItemStacks = new ItemStack[3];
        public int furnaceBurnTime = 0;
        public int currentItemBurnTime = 0;
        public int furnaceCookTime = 0;

        public int size()
        {
            return furnaceItemStacks.Length;
        }

        public ItemStack getStack(int var1)
        {
            return furnaceItemStacks[var1];
        }

        public ItemStack removeStack(int var1, int var2)
        {
            if (furnaceItemStacks[var1] != null)
            {
                ItemStack var3;
                if (furnaceItemStacks[var1].stackSize <= var2)
                {
                    var3 = furnaceItemStacks[var1];
                    furnaceItemStacks[var1] = null;
                    return var3;
                }
                else
                {
                    var3 = furnaceItemStacks[var1].splitStack(var2);
                    if (furnaceItemStacks[var1].stackSize == 0)
                    {
                        furnaceItemStacks[var1] = null;
                    }

                    return var3;
                }
            }
            else
            {
                return null;
            }
        }

        public void setStack(int var1, ItemStack var2)
        {
            furnaceItemStacks[var1] = var2;
            if (var2 != null && var2.stackSize > getMaxCountPerStack())
            {
                var2.stackSize = getMaxCountPerStack();
            }

        }

        public string getName()
        {
            return "Furnace";
        }

        public override void readNbt(NBTTagCompound var1)
        {
            base.readNbt(var1);
            NBTTagList var2 = var1.getTagList("Items");
            furnaceItemStacks = new ItemStack[size()];

            for (int var3 = 0; var3 < var2.tagCount(); ++var3)
            {
                NBTTagCompound var4 = (NBTTagCompound)var2.tagAt(var3);
                sbyte var5 = var4.getByte("Slot");
                if (var5 >= 0 && var5 < furnaceItemStacks.Length)
                {
                    furnaceItemStacks[var5] = new ItemStack(var4);
                }
            }

            furnaceBurnTime = var1.getShort("BurnTime");
            furnaceCookTime = var1.getShort("CookTime");
            currentItemBurnTime = getItemBurnTime(furnaceItemStacks[1]);
        }

        public override void writeNbt(NBTTagCompound var1)
        {
            base.writeNbt(var1);
            var1.setShort("BurnTime", (short)furnaceBurnTime);
            var1.setShort("CookTime", (short)furnaceCookTime);
            NBTTagList var2 = new NBTTagList();

            for (int var3 = 0; var3 < furnaceItemStacks.Length; ++var3)
            {
                if (furnaceItemStacks[var3] != null)
                {
                    NBTTagCompound var4 = new NBTTagCompound();
                    var4.setByte("Slot", (sbyte)var3);
                    furnaceItemStacks[var3].writeToNBT(var4);
                    var2.setTag(var4);
                }
            }

            var1.setTag("Items", var2);
        }

        public int getMaxCountPerStack()
        {
            return 64;
        }

        public int getCookProgressScaled(int var1)
        {
            return furnaceCookTime * var1 / 200;
        }

        public int getBurnTimeRemainingScaled(int var1)
        {
            if (currentItemBurnTime == 0)
            {
                currentItemBurnTime = 200;
            }

            return furnaceBurnTime * var1 / currentItemBurnTime;
        }

        public bool isBurning()
        {
            return furnaceBurnTime > 0;
        }

        public override void tick()
        {
            bool var1 = furnaceBurnTime > 0;
            bool var2 = false;
            if (furnaceBurnTime > 0)
            {
                --furnaceBurnTime;
            }

            if (!world.multiplayerWorld)
            {
                if (furnaceBurnTime == 0 && canSmelt())
                {
                    currentItemBurnTime = furnaceBurnTime = getItemBurnTime(furnaceItemStacks[1]);
                    if (furnaceBurnTime > 0)
                    {
                        var2 = true;
                        if (furnaceItemStacks[1] != null)
                        {
                            --furnaceItemStacks[1].stackSize;
                            if (furnaceItemStacks[1].stackSize == 0)
                            {
                                furnaceItemStacks[1] = null;
                            }
                        }
                    }
                }

                if (isBurning() && canSmelt())
                {
                    ++furnaceCookTime;
                    if (furnaceCookTime == 200)
                    {
                        furnaceCookTime = 0;
                        smeltItem();
                        var2 = true;
                    }
                }
                else
                {
                    furnaceCookTime = 0;
                }

                if (var1 != furnaceBurnTime > 0)
                {
                    var2 = true;
                    BlockFurnace.updateFurnaceBlockState(furnaceBurnTime > 0, world, x, y, z);
                }
            }

            if (var2)
            {
                markDirty();
            }

        }

        private bool canSmelt()
        {
            if (furnaceItemStacks[0] == null)
            {
                return false;
            }
            else
            {
                ItemStack var1 = FurnaceRecipes.smelting().getSmeltingResult(furnaceItemStacks[0].getItem().shiftedIndex);
                return var1 == null ? false : (furnaceItemStacks[2] == null ? true : (!furnaceItemStacks[2].isItemEqual(var1) ? false : (furnaceItemStacks[2].stackSize < getMaxCountPerStack() && furnaceItemStacks[2].stackSize < furnaceItemStacks[2].getMaxStackSize() ? true : furnaceItemStacks[2].stackSize < var1.getMaxStackSize())));
            }
        }

        public void smeltItem()
        {
            if (canSmelt())
            {
                ItemStack var1 = FurnaceRecipes.smelting().getSmeltingResult(furnaceItemStacks[0].getItem().shiftedIndex);
                if (furnaceItemStacks[2] == null)
                {
                    furnaceItemStacks[2] = var1.copy();
                }
                else if (furnaceItemStacks[2].itemID == var1.itemID)
                {
                    ++furnaceItemStacks[2].stackSize;
                }

                --furnaceItemStacks[0].stackSize;
                if (furnaceItemStacks[0].stackSize <= 0)
                {
                    furnaceItemStacks[0] = null;
                }

            }
        }

        private int getItemBurnTime(ItemStack var1)
        {
            if (var1 == null)
            {
                return 0;
            }
            else
            {
                int var2 = var1.getItem().shiftedIndex;
                return var2 < 256 && Block.blocksList[var2].blockMaterial == Material.wood ? 300 : (var2 == Item.stick.shiftedIndex ? 100 : (var2 == Item.coal.shiftedIndex ? 1600 : (var2 == Item.bucketLava.shiftedIndex ? 20000 : (var2 == Block.sapling.blockID ? 100 : 0))));
            }
        }

        public bool canPlayerUse(EntityPlayer var1)
        {
            return world.getBlockTileEntity(x, y, z) != this ? false : var1.getDistanceSq((double)x + 0.5D, (double)y + 0.5D, (double)z + 0.5D) <= 64.0D;
        }
    }

}