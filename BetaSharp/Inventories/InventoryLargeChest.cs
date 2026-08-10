using OmniBlock.Entities;
using OmniBlock.Items;

namespace OmniBlock.Inventories;

internal class InventoryLargeChest(string name, IInventory upperChest, IInventory lowerChest) : IInventory
{
    public int Size => upperChest.Size + lowerChest.Size;

    public string Name { get; } = name;

    public ItemStack? GetStack(int slotIndex) => slotIndex >= upperChest.Size ? lowerChest.GetStack(slotIndex - upperChest.Size) : upperChest.GetStack(slotIndex);

    public ItemStack? RemoveStack(int slotIndex, int amount) => slotIndex >= upperChest.Size ? lowerChest.RemoveStack(slotIndex - upperChest.Size, amount) : upperChest.RemoveStack(slotIndex, amount);

    public void SetStack(int slotIndex, ItemStack? itemStack)
    {
        if (slotIndex >= upperChest.Size)
        {
            lowerChest.SetStack(slotIndex - upperChest.Size, itemStack);
        }
        else
        {
            upperChest.SetStack(slotIndex, itemStack);
        }
    }

    public int MaxCountPerStack => upperChest.MaxCountPerStack;

    public void MarkDirty()
    {
        upperChest.MarkDirty();
        lowerChest.MarkDirty();
    }

    public bool CanPlayerUse(EntityPlayer entityPlayer) => upperChest.CanPlayerUse(entityPlayer) && lowerChest.CanPlayerUse(entityPlayer);
}
