using betareborn.Entities;
using betareborn.Items;
using betareborn.TileEntities;

namespace betareborn.Containers
{
    public class ContainerFurnace : Container
    {

        private TileEntityFurnace furnace;
        private int cookTime = 0;
        private int burnTime = 0;
        private int itemBurnTime = 0;

        public ContainerFurnace(InventoryPlayer var1, TileEntityFurnace var2)
        {
            furnace = var2;
            addSlot(new Slot(var2, 0, 56, 17));
            addSlot(new Slot(var2, 1, 56, 53));
            addSlot(new SlotFurnace(var1.player, var2, 2, 116, 35));

            int var3;
            for (var3 = 0; var3 < 3; ++var3)
            {
                for (int var4 = 0; var4 < 9; ++var4)
                {
                    addSlot(new Slot(var1, var4 + var3 * 9 + 9, 8 + var4 * 18, 84 + var3 * 18));
                }
            }

            for (var3 = 0; var3 < 9; ++var3)
            {
                addSlot(new Slot(var1, var3, 8 + var3 * 18, 142));
            }

        }

        public override void updateCraftingResults()
        {
            base.updateCraftingResults();

            for (int var1 = 0; var1 < field_20121_g.size(); ++var1)
            {
                ICrafting var2 = (ICrafting)field_20121_g.get(var1);
                if (cookTime != furnace.furnaceCookTime)
                {
                    var2.func_20158_a(this, 0, furnace.furnaceCookTime);
                }

                if (burnTime != furnace.furnaceBurnTime)
                {
                    var2.func_20158_a(this, 1, furnace.furnaceBurnTime);
                }

                if (itemBurnTime != furnace.currentItemBurnTime)
                {
                    var2.func_20158_a(this, 2, furnace.currentItemBurnTime);
                }
            }

            cookTime = furnace.furnaceCookTime;
            burnTime = furnace.furnaceBurnTime;
            itemBurnTime = furnace.currentItemBurnTime;
        }

        public override void func_20112_a(int var1, int var2)
        {
            if (var1 == 0)
            {
                furnace.furnaceCookTime = var2;
            }

            if (var1 == 1)
            {
                furnace.furnaceBurnTime = var2;
            }

            if (var1 == 2)
            {
                furnace.currentItemBurnTime = var2;
            }

        }

        public override bool isUsableByPlayer(EntityPlayer var1)
        {
            return furnace.canPlayerUse(var1);
        }

        public override ItemStack getStackInSlot(int var1)
        {
            ItemStack var2 = null;
            Slot var3 = (Slot)slots.get(var1);
            if (var3 != null && var3.getHasStack())
            {
                ItemStack var4 = var3.getStack();
                var2 = var4.copy();
                if (var1 == 2)
                {
                    func_28125_a(var4, 3, 39, true);
                }
                else if (var1 >= 3 && var1 < 30)
                {
                    func_28125_a(var4, 30, 39, false);
                }
                else if (var1 >= 30 && var1 < 39)
                {
                    func_28125_a(var4, 3, 30, false);
                }
                else
                {
                    func_28125_a(var4, 3, 39, false);
                }

                if (var4.stackSize == 0)
                {
                    var3.putStack((ItemStack)null);
                }
                else
                {
                    var3.onSlotChanged();
                }

                if (var4.stackSize == var2.stackSize)
                {
                    return null;
                }

                var3.onPickupFromSlot(var4);
            }

            return var2;
        }
    }

}
