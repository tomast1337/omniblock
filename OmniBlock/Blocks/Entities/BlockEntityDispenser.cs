using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.Util.Maths;

namespace OmniBlock.Blocks.Entities;

public class BlockEntityDispenser : BlockEntity, IInventory
{
    private readonly JavaRandom _random = new();
    private ItemStack?[] _itemStacks = new ItemStack[9];
    protected override BlockEntityType Type => Dispenser;

    public int Size => 9;

    public ItemStack? GetStack(int slot)
    {
        return _itemStacks[slot];
    }

    public ItemStack? RemoveStack(int slot, int amount)
    {
        var item = _itemStacks[slot];
        if (item == null) return null;

        ItemStack removedStack;
        if (item.Count <= amount)
        {
            removedStack = item;
            _itemStacks[slot] = null;
            MarkDirty();
            return removedStack;
        }

        removedStack = item.Split(amount);
        if (item.Count == 0) _itemStacks[slot] = null;

        MarkDirty();
        return removedStack;
    }

    public void SetStack(int slot, ItemStack? stack)
    {
        _itemStacks[slot] = stack;
        if (stack != null && stack.Count > MaxCountPerStack) stack.Count = MaxCountPerStack;
        MarkDirty();
    }

    public string Name => "Trap";

    public int MaxCountPerStack => 64;

    public bool CanPlayerUse(EntityPlayer player)
    {
        return World!.Entities.GetBlockEntity<BlockEntityDispenser>(X, Y, Z) == this && player.GetSquaredDistance(X + 0.5D, Y + 0.5D, Z + 0.5D) <= 64.0D;
    }

    public ItemStack? GetItemToDispose()
    {
        var selectedSlot = -1;
        var nonNullCount = 1;

        for (var slotIndex = 0; slotIndex < _itemStacks.Length; ++slotIndex)
            if (_itemStacks[slotIndex] != null && _random.NextInt(nonNullCount++) == 0)
                selectedSlot = slotIndex;

        return selectedSlot >= 0 ? RemoveStack(selectedSlot, 1) : null;
    }

    protected override void ReadNbt(NBTTagCompound nbt)
    {
        base.ReadNbt(nbt);
        var itemList = nbt.GetTagList("Items");
        _itemStacks = new ItemStack[Size];

        for (var itemIndex = 0; itemIndex < itemList.TagCount(); ++itemIndex)
        {
            var itemTag = (NBTTagCompound)itemList.TagAt(itemIndex);
            var slotIndex = itemTag.GetByte("Slot") & 255;
            if (slotIndex >= 0 && slotIndex < _itemStacks.Length) _itemStacks[slotIndex] = new ItemStack(itemTag);
        }
    }

    public override void WriteNbt(NBTTagCompound nbt)
    {
        base.WriteNbt(nbt);
        NBTTagList itemList = new();


        for (var slotIndex = 0; slotIndex < _itemStacks.Length; ++slotIndex)
        {
            var itemStack = _itemStacks[slotIndex];
            if (itemStack == null) continue;

            NBTTagCompound itemTag = new();
            itemTag.SetByte("Slot", (sbyte)slotIndex);
            itemStack.WriteToNbt(itemTag);
            itemList.SetTag(itemTag);
        }

        nbt.SetTag("Items", itemList);
    }
}