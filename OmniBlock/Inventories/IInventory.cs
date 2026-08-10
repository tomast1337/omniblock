using OmniBlock.Entities;
using OmniBlock.Items;

namespace OmniBlock.Inventories;

public interface IInventory
{
    int Size { get; }

    string Name { get; }

    int MaxCountPerStack { get; }

    ItemStack? GetStack(int slotIndex);

    ItemStack? RemoveStack(int slotIndex, int amount);

    void SetStack(int slotIndex, ItemStack? itemStack);

    void MarkDirty();

    bool CanPlayerUse(EntityPlayer entityPlayer);
}
