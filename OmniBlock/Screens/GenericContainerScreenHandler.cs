using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.Screens.Slots;

namespace OmniBlock.Screens;

public class GenericContainerScreenHandler : ScreenHandler
{
    private readonly IInventory inventory;
    private readonly int rows;

    public GenericContainerScreenHandler(IInventory playerInventory, IInventory inventory)
    {
        this.inventory = inventory;
        rows = inventory.Size / 9;
        var inventoryYOffset = (rows - 4) * 18;

        int row;
        int column;
        for (row = 0; row < rows; ++row)
        {
            for (column = 0; column < 9; ++column)
            {
                AddSlot(new Slot(inventory, column + row * 9, 8 + column * 18, 18 + row * 18));
            }
        }

        for (row = 0; row < 3; ++row)
        {
            for (column = 0; column < 9; ++column)
            {
                AddSlot(new Slot(playerInventory, column + row * 9 + 9, 8 + column * 18, 103 + row * 18 + inventoryYOffset));
            }
        }

        for (row = 0; row < 9; ++row)
        {
            AddSlot(new Slot(playerInventory, row, 8 + row * 18, 161 + inventoryYOffset));
        }
    }

    public override bool canUse(EntityPlayer player) => inventory.CanPlayerUse(player);

    public override ItemStack quickMove(int slotNumber)
    {
        ItemStack movedStack = null;
        var slot = Slots[slotNumber];
        if (slot != null && slot.hasStack())
        {
            var slotStack = slot.getStack();
            movedStack = slotStack.Copy();
            if (slotNumber < rows * 9)
            {
                insertItem(slotStack, rows * 9, Slots.Count, true);
            }
            else
            {
                insertItem(slotStack, 0, rows * 9, false);
            }

            if (slotStack.Count == 0)
            {
                slot.setStack(null);
            }
            else
            {
                slot.markDirty();
            }
        }

        return movedStack;
    }
}
