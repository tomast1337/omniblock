using betareborn.Blocks;
using betareborn.Entities;
using betareborn.NBT;
using betareborn.Stats;
using betareborn.Worlds;

namespace betareborn.Items
{
    public class ItemStack : java.lang.Object
    {
        public int stackSize;
        public int animationsToGo;
        public int itemID;
        private int itemDamage;

        public ItemStack(Block var1) : this((Block)var1, 1)
        {
        }

        public ItemStack(int id, int count) {
            itemID = id;
            stackSize = count;
        }

        public ItemStack(Block var1, int var2) : this(var1.blockID, var2, 0)
        {
        }

        public ItemStack(Block var1, int var2, int var3) : this(var1.blockID, var2, var3)
        {
        }

        public ItemStack(Item var1) : this(var1.shiftedIndex, 1, 0)
        {
        }

        public ItemStack(Item var1, int var2) : this(var1.shiftedIndex, var2, 0)
        {
        }

        public ItemStack(Item var1, int var2, int var3) : this(var1.shiftedIndex, var2, var3)
        {
        }

        public ItemStack(int var1, int var2, int var3)
        {
            stackSize = 0;
            itemID = var1;
            stackSize = var2;
            itemDamage = var3;
        }

        public ItemStack(NBTTagCompound var1)
        {
            stackSize = 0;
            readFromNBT(var1);
        }

        public ItemStack splitStack(int var1)
        {
            stackSize -= var1;
            return new ItemStack(itemID, var1, itemDamage);
        }

        public Item getItem()
        {
            return Item.itemsList[itemID];
        }

        public int getIconIndex()
        {
            return getItem().getIconIndex(this);
        }

        public bool useItem(EntityPlayer var1, World var2, int var3, int var4, int var5, int var6)
        {
            bool var7 = getItem().onItemUse(this, var1, var2, var3, var4, var5, var6);
            if (var7)
            {
                var1.addStat(StatList.field_25172_A[itemID], 1);
            }

            return var7;
        }

        public float getStrVsBlock(Block var1)
        {
            return getItem().getStrVsBlock(this, var1);
        }

        public ItemStack useItemRightClick(World var1, EntityPlayer var2)
        {
            return getItem().onItemRightClick(this, var1, var2);
        }

        public NBTTagCompound writeToNBT(NBTTagCompound var1)
        {
            var1.setShort("id", (short)itemID);
            var1.setByte("Count", (sbyte)stackSize);
            var1.setShort("Damage", (short)itemDamage);
            return var1;
        }

        public void readFromNBT(NBTTagCompound var1)
        {
            itemID = var1.getShort("id");
            stackSize = var1.getByte("Count");
            itemDamage = var1.getShort("Damage");
        }

        public int getMaxStackSize()
        {
            return getItem().getItemStackLimit();
        }

        public bool isStackable()
        {
            return getMaxStackSize() > 1 && (!isItemStackDamageable() || !isItemDamaged());
        }

        public bool isItemStackDamageable()
        {
            return Item.itemsList[itemID].getMaxDamage() > 0;
        }

        public bool getHasSubtypes()
        {
            return Item.itemsList[itemID].getHasSubtypes();
        }

        public bool isItemDamaged()
        {
            return isItemStackDamageable() && itemDamage > 0;
        }

        public int getItemDamageForDisplay()
        {
            return itemDamage;
        }

        public int getItemDamage()
        {
            return itemDamage;
        }

        public void setItemDamage(int var1)
        {
            itemDamage = var1;
        }

        public int getMaxDamage()
        {
            return Item.itemsList[itemID].getMaxDamage();
        }

        public void damageItem(int var1, Entity var2)
        {
            if (isItemStackDamageable())
            {
                itemDamage += var1;
                if (itemDamage > getMaxDamage())
                {
                    if (var2 is EntityPlayer)
                    {
                        ((EntityPlayer)var2).addStat(StatList.field_25170_B[itemID], 1);
                    }

                    --stackSize;
                    if (stackSize < 0)
                    {
                        stackSize = 0;
                    }

                    itemDamage = 0;
                }

            }
        }

        public void hitEntity(EntityLiving var1, EntityPlayer var2)
        {
            bool var3 = Item.itemsList[itemID].hitEntity(this, var1, var2);
            if (var3)
            {
                var2.addStat(StatList.field_25172_A[itemID], 1);
            }

        }

        public void onDestroyBlock(int var1, int var2, int var3, int var4, EntityPlayer var5)
        {
            bool var6 = Item.itemsList[itemID].onBlockDestroyed(this, var1, var2, var3, var4, var5);
            if (var6)
            {
                var5.addStat(StatList.field_25172_A[itemID], 1);
            }

        }

        public int getDamageVsEntity(Entity var1)
        {
            return Item.itemsList[itemID].getDamageVsEntity(var1);
        }

        public bool canHarvestBlock(Block var1)
        {
            return Item.itemsList[itemID].canHarvestBlock(var1);
        }

        public void func_1097_a(EntityPlayer var1)
        {
        }

        public void useItemOnEntity(EntityLiving var1)
        {
            Item.itemsList[itemID].saddleEntity(this, var1);
        }

        public ItemStack copy()
        {
            return new ItemStack(itemID, stackSize, itemDamage);
        }

        public static bool areItemStacksEqual(ItemStack var0, ItemStack var1)
        {
            return var0 == null && var1 == null ? true : (var0 != null && var1 != null ? var0.isItemStackEqual(var1) : false);
        }

        private bool isItemStackEqual(ItemStack var1)
        {
            return stackSize != var1.stackSize ? false : (itemID != var1.itemID ? false : itemDamage == var1.itemDamage);
        }

        public bool isItemEqual(ItemStack var1)
        {
            return itemID == var1.itemID && itemDamage == var1.itemDamage;
        }

        public string getItemName()
        {
            return Item.itemsList[itemID].getItemNameIS(this);
        }

        public static ItemStack copyItemStack(ItemStack var0)
        {
            return var0 == null ? null : var0.copy();
        }

        public override string toString()
        {
            return stackSize + "x" + Item.itemsList[itemID].getItemName() + "@" + itemDamage;
        }

        public void updateAnimation(World var1, Entity var2, int var3, bool var4)
        {
            if (animationsToGo > 0)
            {
                --animationsToGo;
            }

            Item.itemsList[itemID].onUpdate(this, var1, var2, var3, var4);
        }

        public void onCrafting(World var1, EntityPlayer var2)
        {
            var2.addStat(StatList.field_25158_z[itemID], stackSize);
            Item.itemsList[itemID].onCreated(this, var1, var2);
        }

        public bool isStackEqual(ItemStack var1)
        {
            return itemID == var1.itemID && stackSize == var1.stackSize && itemDamage == var1.itemDamage;
        }
    }

}