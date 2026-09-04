using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Screens;

namespace OmniBlock.Inventories;

public class InventoryCrafting : IInventory
{
    private readonly ScreenHandler _handler;
    private readonly ItemStack?[] _stacks;
    private readonly int _width;

    public InventoryCrafting(ScreenHandler eventHandler, int width, int height)
    {
        var gridSize = width * height;
        _stacks = new ItemStack[gridSize];
        _handler = eventHandler;
        _width = width;
    }

    public int Size => _stacks.Length;

    public ItemStack? GetStack(int slotIndex) => slotIndex >= Size ? null : _stacks[slotIndex];

    public string Name => "Crafting";

    public ItemStack? RemoveStack(int slotIndex, int amount)
    {
        var stack = _stacks[slotIndex];

        if (stack == null) return null;

        ItemStack removeStack;
        if (stack.Count <= amount)
        {
            removeStack = stack;
            _stacks[slotIndex] = null;
            _handler.onSlotUpdate(this);
            return removeStack;
        }

        removeStack = stack.Split(amount);
        if (stack.Count == 0)
        {
            _stacks[slotIndex] = null;
        }

        _handler.onSlotUpdate(this);
        return removeStack;
    }

    public void SetStack(int slotIndex, ItemStack? itemStack)
    {
        _stacks[slotIndex] = itemStack;
        _handler.onSlotUpdate(this);
    }

    public int MaxCountPerStack => 64;

    public void MarkDirty()
    {
    }

    public bool CanPlayerUse(EntityPlayer entityPlayer) => true;

    public ItemStack? GetStackAt(int x, int y)
    {
        if (x < 0 || x >= _width) return null;

        var slotIndex = x + y * _width;
        return GetStack(slotIndex);
    }
}
