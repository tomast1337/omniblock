using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Screens.Slots;

namespace OmniBlock.Screens;

public class PlayerScreenHandler : ScreenHandler
{
    private readonly EntityPlayer _player;

    public InventoryCrafting craftingInput;
    public IInventory craftingResult;
    public bool isLocal;

    public PlayerScreenHandler(InventoryPlayer inventoryPlayer) : this(inventoryPlayer, true)
    {
    }

    public PlayerScreenHandler(InventoryPlayer inventoryPlayer, bool isLocal)
    {
        _player = inventoryPlayer.Player;
        craftingInput = new InventoryCrafting(this, 2, 2);
        craftingResult = new InventoryCraftResult();
        this.isLocal = false;
        this.isLocal = isLocal;
        AddSlot(new CraftingResultSlot(inventoryPlayer.Player, craftingInput, craftingResult, 0, 144, 36));

        int row;
        int column;
        for (row = 0; row < 2; ++row)
        {
            for (column = 0; column < 2; ++column)
            {
                AddSlot(new Slot(craftingInput, column + row * 2, 88 + column * 18, 26 + row * 18));
            }
        }

        for (var armorSlot = 0; armorSlot < 4; ++armorSlot)
        {
            AddSlot(new SlotArmor(this, inventoryPlayer, inventoryPlayer.Size - 1 - armorSlot, 8,
                8 + armorSlot * 18, armorSlot,
                inventoryPlayer.Player.World.Content.Blocks.Get("omniblock:pumpkin").Id));
        }

        for (row = 0; row < 3; ++row)
        {
            for (column = 0; column < 9; ++column)
            {
                AddSlot(new Slot(inventoryPlayer, column + (row + 1) * 9, 8 + column * 18, 84 + row * 18));
            }
        }

        for (var hotbarSlot = 0; hotbarSlot < 9; ++hotbarSlot)
        {
            AddSlot(new Slot(inventoryPlayer, hotbarSlot, 8 + hotbarSlot * 18, 142));
        }

        onSlotUpdate(craftingInput);
    }

    public override void onSlotUpdate(IInventory inv) => craftingResult.SetStack(0, _player.World.Content.Processes.Crafting.Craft(craftingInput));

    public override void onClosed(EntityPlayer player)
    {
        base.onClosed(player);

        for (var slotIndex = 0; slotIndex < 4; ++slotIndex)
        {
            var craftingStack = craftingInput.GetStack(slotIndex);
            if (craftingStack != null)
            {
                player.Inventory.AddItemStackToInventory(craftingStack);
                craftingInput.SetStack(slotIndex, null);
            }
        }
    }

    public override bool canUse(EntityPlayer player) => true;

    public override ItemStack? quickMove(int slotNumber)
    {
        ItemStack? movedStack = null;
        var slot = Slots[slotNumber];
        if (slot is not null && slot.hasStack() && slot.getStack() is { } slotStack)
        {
            movedStack = slotStack.Copy();
            if (slotNumber == 0)
            {
                insertItem(slotStack, 9, 45, true);
            }
            else if (slotNumber >= 5 && slotNumber < 9)
            {
                insertItem(slotStack, 9, 45, false);
            }
            else if (slotNumber >= 9 && slotNumber < 45)
            {
                if (slotStack.GetItem().GetBehavior<ArmorBehavior>() is { } armor)
                {
                    var targetSlot = 5 + armor.ArmorType;
                    var countBefore = slotStack.Count;
                    insertItem(slotStack, targetSlot, targetSlot + 1, false);
                    if (slotStack.Count == countBefore)
                    {
                        if (slotNumber < 36)
                        {
                            insertItem(slotStack, 36, 45, false);
                        }
                        else
                        {
                            insertItem(slotStack, 9, 36, false);
                        }
                    }
                }
                else if (slotNumber < 36)
                {
                    insertItem(slotStack, 36, 45, false);
                }
                else
                {
                    insertItem(slotStack, 9, 36, false);
                }
            }
            else
            {
                insertItem(slotStack, 9, 45, false);
            }

            if (slotStack.Count == 0)
            {
                slot.setStack(null);
            }
            else
            {
                slot.markDirty();
            }

            if (slotStack.Count == movedStack.Count)
            {
                return null;
            }

            slot.onTakeItem(slotStack);
        }

        return movedStack;
    }
}
