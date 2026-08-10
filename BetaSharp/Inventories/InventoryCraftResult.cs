using OmniBlock.Entities;
using OmniBlock.Items;

namespace OmniBlock.Inventories;

internal class InventoryCraftResult : IInventory
{
    private readonly ItemStack?[] _result = new ItemStack[1];

    public int Size => 1;

    public ItemStack? GetStack(int slotIndex) => _result[slotIndex];

    public string Name => "Result";

    public ItemStack? RemoveStack(int slotIndex, int amount)
    {
        ItemStack? stack = _result[slotIndex];

        if (stack == null) return null;

        _result[slotIndex] = null;
        return stack;

    }

    public void SetStack(int slotIndex, ItemStack? itemStack) => _result[slotIndex] = itemStack;

    public int MaxCountPerStack => 64;

    public void MarkDirty()
    {
    }

    public bool CanPlayerUse(EntityPlayer entityPlayer) => true;
}
