using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.Screens.Slots;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Screens;

public class CraftingScreenHandler : ScreenHandler
{
    private readonly IWorldContext world;
    private readonly int x;
    private readonly int y;
    private readonly int z;

    public InventoryCrafting input;
    public IInventory result = new InventoryCraftResult();

    public CraftingScreenHandler(InventoryPlayer playerInventory, IWorldContext world, int x, int y, int z)
    {
        input = new InventoryCrafting(this, 3, 3);
        this.world = world;
        this.x = x;
        this.y = y;
        this.z = z;
        AddSlot(new CraftingResultSlot(playerInventory.Player, input, result, 0, 124, 35));

        int row;
        int column;
        for (row = 0; row < 3; ++row)
        {
            for (column = 0; column < 3; ++column)
            {
                AddSlot(new Slot(input, column + row * 3, 30 + column * 18, 17 + row * 18));
            }
        }

        for (row = 0; row < 3; ++row)
        {
            for (column = 0; column < 9; ++column)
            {
                AddSlot(new Slot(playerInventory, column + row * 9 + 9, 8 + column * 18, 84 + row * 18));
            }
        }

        for (row = 0; row < 9; ++row)
        {
            AddSlot(new Slot(playerInventory, row, 8 + row * 18, 142));
        }

        onSlotUpdate(input);
    }

    public override void onSlotUpdate(IInventory inv) => result.SetStack(0, world.Content.Processes.Crafting.Craft(input));

    public override void onClosed(EntityPlayer player)
    {
        base.onClosed(player);
        if (!world.IsRemote)
        {
            for (var i = 0; i < 9; ++i)
            {
                var itemStack = input.GetStack(i);
                if (itemStack != null)
                {
                    player.Inventory.AddItemStackToInventoryOrDrop(itemStack);
                }
            }
        }
    }

    public override bool canUse(EntityPlayer player) => world.Reader.GetBlockId(x, y, z) == world.Content.Blocks.Get("omniblock:crafting_table").Id
                                                        && player.GetSquaredDistance(x + 0.5D, y + 0.5D, z + 0.5D) <= 64.0D;

    public override ItemStack quickMove(int slotNumber)
    {
        ItemStack movedStack = null;
        var slot = Slots[slotNumber];
        if (slot != null && slot.hasStack())
        {
            var slotStack = slot.getStack();
            movedStack = slotStack.Copy();
            if (slotNumber == 0)
            {
                insertItem(slotStack, 10, 46, true);
            }
            else if (slotNumber >= 10 && slotNumber < 37)
            {
                insertItem(slotStack, 37, 46, false);
            }
            else if (slotNumber >= 37 && slotNumber < 46)
            {
                insertItem(slotStack, 10, 37, false);
            }
            else
            {
                insertItem(slotStack, 10, 46, false);
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
