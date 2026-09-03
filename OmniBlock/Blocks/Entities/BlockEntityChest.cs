using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.NBT;

namespace OmniBlock.Blocks.Entities;

internal class BlockEntityChest : BlockEntity, IInventory
{
    private ItemStack?[] _inventory = new ItemStack[36];
    protected override BlockEntityType Type => Chest;

    public int Size => 27;

    public ItemStack? GetStack(int stackIndex)
    {
        return _inventory[stackIndex];
    }

    public ItemStack? RemoveStack(int slot, int amount)
    {
        if (_inventory[slot] == null) return null;

        ItemStack itemStack;
        var stack = _inventory[slot];
        if (stack == null) return null;

        if (stack.Count <= amount)
        {
            itemStack = stack;
            _inventory[slot] = null;
            MarkDirty();
            return itemStack;
        }

        itemStack = stack.Split(amount);
        if (stack.Count == 0) _inventory[slot] = null;

        MarkDirty();
        return itemStack;
    }

    public void SetStack(int slot, ItemStack? stack)
    {
        _inventory[slot] = stack;
        if (stack != null && stack.Count > MaxCountPerStack) stack.Count = MaxCountPerStack;

        MarkDirty();
    }

    public string Name => "Chest";

    public int MaxCountPerStack => 64;

    public bool CanPlayerUse(EntityPlayer player)
    {
        return World!.Entities.GetBlockEntity<BlockEntityChest>(X, Y, Z) == this && player.GetSquaredDistance(X + 0.5D, Y + 0.5D, Z + 0.5D) <= 64.0D;
    }

    protected override void ReadNbt(NBTTagCompound nbt)
    {
        base.ReadNbt(nbt);
        var itemList = nbt.GetTagList("Items");
        _inventory = new ItemStack[Size];

        for (var itemIndex = 0; itemIndex < itemList.TagCount(); ++itemIndex)
        {
            var itemsTag = (NBTTagCompound)itemList.TagAt(itemIndex);
            var slot = itemsTag.GetByte("Slot") & 255;
            if (slot >= 0 && slot < _inventory.Length) _inventory[slot] = new ItemStack(World!.Content.Items, itemsTag);
        }
    }

    public override void WriteNbt(NBTTagCompound nbt)
    {
        base.WriteNbt(nbt);
        NBTTagList itemList = new();

        for (var slotIndex = 0; slotIndex < _inventory.Length; ++slotIndex)
        {
            var stack = _inventory[slotIndex];
            if (stack == null) continue;

            NBTTagCompound itemsTag = new();
            itemsTag.SetByte("Slot", (sbyte)slotIndex);
            stack.WriteToNbt(itemsTag);
            itemList.SetTag(itemsTag);
        }

        nbt.SetTag("Items", itemList);
    }
}
