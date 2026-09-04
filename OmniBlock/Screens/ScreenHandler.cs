using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.Screens.Slots;

namespace OmniBlock.Screens;

public abstract class ScreenHandler
{
    public const int NullSlot = -999;
    private readonly HashSet<EntityPlayer> _players = new();
    private short _revision;

    protected List<ScreenHandlerListener> Listeners { get; } = [];

    public List<ItemStack?> TrackedStacks { get; } = [];
    public List<Slot> Slots { get; } = [];
    public int SyncId { get; set; } = 0;


    protected void AddSlot(Slot slot)
    {
        slot.id = Slots.Count;
        Slots.Add(slot);
        TrackedStacks.Add(null);
    }

    public virtual void AddListener(ScreenHandlerListener listener)
    {
        if (Listeners.Contains(listener))
        {
            throw new ArgumentException("Listener already listening", nameof(listener));
        }

        Listeners.Add(listener);
        listener.onContentsUpdate(this, GetStacks());
        SendContentUpdates();
    }

    public List<ItemStack> GetStacks()
    {
        var stacks = new List<ItemStack>();

        for (var slotIndex = 0; slotIndex < Slots.Count; slotIndex++)
        {
            stacks.Add(Slots[slotIndex].getStack());
        }

        return stacks;
    }

    public virtual void SendContentUpdates()
    {
        for (var slotIndex = 0; slotIndex < Slots.Count; ++slotIndex)
        {
            var slotStack = Slots[slotIndex].getStack();
            var trackedStack = TrackedStacks[slotIndex];
            if (!ItemStack.AreEqual(trackedStack, slotStack))
            {
                trackedStack = slotStack is null ? null : slotStack.Copy();
                TrackedStacks[slotIndex] = trackedStack;

                for (var listenerIndex = 0; listenerIndex < Listeners.Count; ++listenerIndex)
                {
                    Listeners[listenerIndex].onSlotUpdate(this, slotIndex, trackedStack);
                }
            }
        }
    }

    public Slot? GetSlot(IInventory inventory, int index)
    {
        for (var slotIndex = 0; slotIndex < Slots.Count; slotIndex++)
        {
            var slot = Slots[slotIndex];
            if (slot.Equals(inventory, index))
            {
                return slot;
            }
        }

        return null;
    }

    public Slot GetSlot(int index) => Slots[index];

    public virtual ItemStack? quickMove(int index)
    {
        if (index < 0 || index > Slots.Count)
            return null;

        return Slots[index].getStack();
    }

    public ItemStack? onSlotClick(int index, int button, bool shift, EntityPlayer player)
    {
        ItemStack? returnStack = null;
        if (button == 0 || button == 1)
        {
            var playerInventory = player.Inventory;
            if (index == NullSlot)
            {
                if (playerInventory.GetCursorStack() is not null)
                {
                    if (button == 0)
                    {
                        if (player.DropItem(playerInventory.GetCursorStack()))
                        {
                            playerInventory.SetCursorStack(null);
                        }
                    }

                    if (button == 1 && player.GameMode.CanDrop)
                    {
                        player.DropItem(playerInventory.GetCursorStack().Split(1));
                        if (playerInventory.GetCursorStack().Count == 0)
                        {
                            playerInventory.SetCursorStack(null);
                        }
                    }
                }
            }
            else
            {
                int slotItemStackSize;
                if (shift)
                {
                    var itemStack = quickMove(index);
                    if (itemStack is not null)
                    {
                        var itemStackSize = itemStack.Count;
                        returnStack = itemStack.Copy();
                        var slot = Slots[index];
                        if (slot is not null && slot.getStack() is not null)
                        {
                            slotItemStackSize = slot.getStack().Count;
                            if (slotItemStackSize < itemStackSize)
                            {
                                onSlotClick(index, button, shift, player);
                            }
                        }
                    }
                }
                else
                {
                    var slot = Slots[index];
                    if (slot is not null)
                    {
                        slot.markDirty();
                        var slotStack = slot.getStack();
                        var cursorStack = playerInventory.GetCursorStack();
                        if (slotStack is not null)
                        {
                            returnStack = slotStack.Copy();
                        }

                        if (slotStack is null)
                        {
                            if (cursorStack is not null && slot.canInsert(cursorStack))
                            {
                                slotItemStackSize = button == 0 ? cursorStack.Count : 1;
                                if (slotItemStackSize > slot.getMaxItemCount())
                                {
                                    slotItemStackSize = slot.getMaxItemCount();
                                }

                                slot.setStack(cursorStack.Split(slotItemStackSize));
                                if (cursorStack.Count == 0)
                                {
                                    playerInventory.SetCursorStack(null);
                                }
                            }
                        }
                        else if (cursorStack is null)
                        {
                            slotItemStackSize = button == 0 ? slotStack.Count : (slotStack.Count + 1) / 2;
                            var takenStack = slot.takeStack(slotItemStackSize);
                            playerInventory.SetCursorStack(takenStack);
                            if (slotStack.Count == 0)
                            {
                                slot.setStack(null);
                            }

                            slot.onTakeItem(playerInventory.GetCursorStack());
                        }
                        else if (slot.canInsert(cursorStack))
                        {
                            if (slotStack.ItemId != cursorStack.ItemId || (slotStack.GetHasSubtypes() && slotStack.GetDamage() != cursorStack.GetDamage()))
                            {
                                if (cursorStack.Count <= slot.getMaxItemCount())
                                {
                                    slot.setStack(cursorStack);
                                    playerInventory.SetCursorStack(slotStack);
                                }
                            }
                            else
                            {
                                slotItemStackSize = button == 0 ? cursorStack.Count : 1;
                                if (slotItemStackSize > slot.getMaxItemCount() - slotStack.Count)
                                {
                                    slotItemStackSize = slot.getMaxItemCount() - slotStack.Count;
                                }

                                if (slotItemStackSize > cursorStack.GetMaxCount() - slotStack.Count)
                                {
                                    slotItemStackSize = cursorStack.GetMaxCount() - slotStack.Count;
                                }

                                cursorStack.Split(slotItemStackSize);
                                if (cursorStack.Count == 0)
                                {
                                    playerInventory.SetCursorStack(null);
                                }

                                slotStack.Count += slotItemStackSize;
                            }
                        }
                        else if (slotStack.ItemId == cursorStack.ItemId && cursorStack.GetMaxCount() > 1 && (!slotStack.GetHasSubtypes() || slotStack.GetDamage() == cursorStack.GetDamage()))
                        {
                            slotItemStackSize = slotStack.Count;
                            if (slotItemStackSize > 0 && slotItemStackSize + cursorStack.Count <= cursorStack.GetMaxCount())
                            {
                                cursorStack.Count += slotItemStackSize;
                                slotStack.Split(slotItemStackSize);
                                if (slotStack.Count == 0)
                                {
                                    slot.setStack(null);
                                }

                                slot.onTakeItem(playerInventory.GetCursorStack());
                            }
                        }
                    }
                }
            }
        }

        return returnStack;
    }

    public virtual void onClosed(EntityPlayer player)
    {
        var playerInventory = player.Inventory;
        if (playerInventory.GetCursorStack() is not null)
        {
            if (player.GameMode.CanDrop)
            {
                if (player.DropItem(playerInventory.GetCursorStack()))
                {
                    playerInventory.SetCursorStack(null);
                }
            }
            else
            {
                player.Inventory.AddItemStackToInventoryOrDrop(playerInventory.GetCursorStack());
            }
        }
    }

    public virtual void onSlotUpdate(IInventory inventory) => SendContentUpdates();

    public void setStackInSlot(int index, ItemStack stack) => GetSlot(index).setStack(stack);

    public void updateSlotStacks(ItemStack?[] stacks)
    {
        for (var index = 0; index < stacks.Length; ++index)
        {
            GetSlot(index).setStack(stacks[index]);
        }
    }

    public virtual void setProperty(int id, int value)
    {
    }

    public short nextRevision(InventoryPlayer inventory)
    {
        ++_revision;
        return _revision;
    }

    public static void onAcknowledgementAccepted(short actionType)
    {
    }

    public static void onAcknowledgementDenied(short actionType)
    {
    }

    public bool canOpen(EntityPlayer player) => !_players.Contains(player);

    public void updatePlayerList(EntityPlayer player, bool remove)
    {
        if (remove)
        {
            _players.Remove(player);
        }
        else
        {
            _players.Add(player);
        }
    }

    public abstract bool canUse(EntityPlayer player);

    protected void insertItem(ItemStack stack, int start, int end, bool fromLast)
    {
        var slotIndex = start;
        if (fromLast)
        {
            slotIndex = end - 1;
        }

        Slot slotToInsertStack;
        ItemStack itemStackToInsert;
        if (stack.IsStackable())
        {
            while (stack.Count > 0 && ((!fromLast && slotIndex < end) || (fromLast && slotIndex >= start)))
            {
                slotToInsertStack = Slots[slotIndex];
                itemStackToInsert = slotToInsertStack.getStack();
                if (itemStackToInsert is not null && itemStackToInsert.ItemId == stack.ItemId && (!stack.GetHasSubtypes() || stack.GetDamage() == itemStackToInsert.GetDamage()))
                {
                    var newitemStackSize = itemStackToInsert.Count + stack.Count;
                    if (newitemStackSize <= stack.GetMaxCount())
                    {
                        stack.Count = 0;
                        itemStackToInsert.Count = newitemStackSize;
                        slotToInsertStack.markDirty();
                    }
                    else if (itemStackToInsert.Count < stack.GetMaxCount())
                    {
                        stack.Count -= stack.GetMaxCount() - itemStackToInsert.Count;
                        itemStackToInsert.Count = stack.GetMaxCount();
                        slotToInsertStack.markDirty();
                    }
                }

                if (fromLast)
                {
                    --slotIndex;
                }
                else
                {
                    ++slotIndex;
                }
            }
        }

        if (stack.Count > 0)
        {
            if (fromLast)
            {
                slotIndex = end - 1;
            }
            else
            {
                slotIndex = start;
            }

            while ((!fromLast && slotIndex < end) || (fromLast && slotIndex >= start))
            {
                slotToInsertStack = Slots[slotIndex];
                itemStackToInsert = slotToInsertStack.getStack();
                if (itemStackToInsert is null)
                {
                    slotToInsertStack.setStack(stack.Copy());
                    slotToInsertStack.markDirty();
                    stack.Count = 0;
                    break;
                }

                if (fromLast)
                {
                    --slotIndex;
                }
                else
                {
                    ++slotIndex;
                }
            }
        }
    }
}
