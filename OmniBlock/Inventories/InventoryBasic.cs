using OmniBlock.Entities;
using OmniBlock.Items;

namespace OmniBlock.Inventories;

public class InventoryBasic(string inventoryTitle, int slotsCount) : IInventory
{
    private readonly ItemStack?[] _inventoryContents = new ItemStack[slotsCount];

    public ItemStack? GetStack(int slotIndex) => _inventoryContents[slotIndex];

    public ItemStack? RemoveStack(int slotIndex, int amount)
    {
        var inSlot = _inventoryContents[slotIndex];

        if (inSlot == null) return null;

        ItemStack removeStack;
        if (inSlot.Count <= amount)
        {
            removeStack = inSlot;
            _inventoryContents[slotIndex] = null;
            MarkDirty();
            return removeStack;
        }

        removeStack = inSlot.Split(amount);
        if (inSlot.Count == 0)
        {
            _inventoryContents[slotIndex] = null;
        }

        MarkDirty();
        return removeStack;
    }

    public void SetStack(int slotIndex, ItemStack? itemStack)
    {
        _inventoryContents[slotIndex] = itemStack;
        if (itemStack != null && itemStack.Count > MaxCountPerStack)
        {
            itemStack.Count = MaxCountPerStack;
        }

        MarkDirty();
    }

    public int Size { get; } = slotsCount;

    public string Name { get; } = inventoryTitle;

    public int MaxCountPerStack => 64;

    public void MarkDirty()
    {
    }

    public bool CanPlayerUse(EntityPlayer entityPlayer) => true;
}
