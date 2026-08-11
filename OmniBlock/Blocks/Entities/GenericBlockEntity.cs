using OmniBlock.Items;
using OmniBlock.NBT;

namespace OmniBlock.Blocks.Entities;

/// <summary>
///     Data-bag tile entity for scripted blocks. Fixed, type-segregated stores instead of a
///     boxed `Dictionary&lt;string, object&gt;` — Luau FFI reads a primitive by key without an
///     allocation or a cast.
/// </summary>
public class GenericBlockEntity : BlockEntity
{
    private readonly Dictionary<string, int> _integers = new();
    private readonly Dictionary<string, float> _floats = new();
    private readonly Dictionary<string, string> _strings = new();

    private ItemStack?[] _inventory = [];

    protected override BlockEntityType Type => Generic;

    public int GetInt(string key) => _integers.TryGetValue(key, out int value) ? value : 0;
    public void SetInt(string key, int value) => _integers[key] = value;

    public float GetFloat(string key) => _floats.TryGetValue(key, out float value) ? value : 0f;
    public void SetFloat(string key, float value) => _floats[key] = value;

    public string GetString(string key) => _strings.TryGetValue(key, out string? value) ? value : string.Empty;
    public void SetString(string key, string value) => _strings[key] = value;

    public int InventorySize => _inventory.Length;

    public void SetInventorySize(int size)
    {
        if (size == _inventory.Length) return;

        ItemStack?[] resized = new ItemStack?[size];
        Array.Copy(_inventory, resized, Math.Min(size, _inventory.Length));
        _inventory = resized;
    }

    public ItemStack? GetStack(int slot) => _inventory[slot];

    public void SetStack(int slot, ItemStack? stack)
    {
        _inventory[slot] = stack;
        MarkDirty();
    }

    protected override void ReadNbt(NBTTagCompound nbt)
    {
        base.ReadNbt(nbt);

        _integers.Clear();
        NBTTagCompound intData = nbt.GetCompoundTag("IntData");
        foreach ((string key, NBTBase value) in intData.Dictionary)
        {
            _integers[key] = ((NBTTagInt)value).Value;
        }

        _floats.Clear();
        NBTTagCompound floatData = nbt.GetCompoundTag("FloatData");
        foreach ((string key, NBTBase value) in floatData.Dictionary)
        {
            _floats[key] = ((NBTTagFloat)value).Value;
        }

        _strings.Clear();
        NBTTagCompound stringData = nbt.GetCompoundTag("StringData");
        foreach ((string key, NBTBase value) in stringData.Dictionary)
        {
            _strings[key] = ((NBTTagString)value).Value;
        }

        int inventorySize = nbt.GetInteger("InventorySize");
        _inventory = new ItemStack?[inventorySize];

        NBTTagList itemList = nbt.GetTagList("Inventory");
        for (int itemIndex = 0; itemIndex < itemList.TagCount(); ++itemIndex)
        {
            NBTTagCompound itemTag = (NBTTagCompound)itemList.TagAt(itemIndex);
            int slot = itemTag.GetByte("Slot") & 255;
            if (slot >= 0 && slot < _inventory.Length)
            {
                _inventory[slot] = new ItemStack(itemTag);
            }
        }
    }

    public override void WriteNbt(NBTTagCompound nbt)
    {
        base.WriteNbt(nbt);

        NBTTagCompound intData = new();
        foreach ((string key, int value) in _integers)
        {
            intData.SetInteger(key, value);
        }
        nbt.SetTag("IntData", intData);

        NBTTagCompound floatData = new();
        foreach ((string key, float value) in _floats)
        {
            floatData.SetFloat(key, value);
        }
        nbt.SetTag("FloatData", floatData);

        NBTTagCompound stringData = new();
        foreach ((string key, string value) in _strings)
        {
            stringData.SetString(key, value);
        }
        nbt.SetTag("StringData", stringData);

        nbt.SetInteger("InventorySize", _inventory.Length);

        NBTTagList itemList = new();
        for (int slotIndex = 0; slotIndex < _inventory.Length; ++slotIndex)
        {
            ItemStack? stack = _inventory[slotIndex];
            if (stack == null) continue;

            NBTTagCompound itemTag = new();
            itemTag.SetByte("Slot", (sbyte)slotIndex);
            stack.WriteToNbt(itemTag);
            itemList.SetTag(itemTag);
        }
        nbt.SetTag("Inventory", itemList);
    }
}
