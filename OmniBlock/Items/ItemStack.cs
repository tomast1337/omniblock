using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.NBT;
using OmniBlock.Registries;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items;

public class ItemStack
{
    public const int MaxSerializedComponentBytes = 16 * 1024;

    private readonly Item _item;
    private NBTTagCompound _components = new();
    private int _damage;
    public int AnimationTime;
    public int Count;
    public int ItemId;

    public ItemStack(Item item) : this(item, 1, 0)
    {
    }

    public ItemStack(Item item, int count) : this(item, count, 0)
    {
    }

    public ItemStack(Item item, int count, int damage)
    {
        _item = item ?? throw new ArgumentNullException(nameof(item));
        ItemId = item.Id;
        Count = count;
        _damage = damage;
    }

    public ItemStack(IItemRuntimeView items, int itemId, int count, int damage)
        : this(items.GetByProtocolId(itemId), count, damage)
    {
    }

    public ItemStack(IItemRuntimeView items, NBTTagCompound nbt)
    {
        ArgumentNullException.ThrowIfNull(items);
        ItemId = nbt.GetShort("id");
        _item = items.GetByProtocolId(ItemId);
        Count = nbt.GetByte("Count");
        _damage = nbt.GetShort("Damage");
        if (nbt.HasKey("Components")) _components = Normalize(nbt.GetCompoundTag("Components"));
    }

    public ItemStack Split(int splitAmount)
    {
        Count -= splitAmount;
        var split = new ItemStack(_item, splitAmount, _damage)
        {
            _components = CloneComponents()
        };
        return split;
    }

    public Item GetItem() => _item;

    public int GetTextureId() => GetItem().GetTextureId(this);

    public bool useOnBlock(EntityPlayer entityPlayer, IWorldContext world, int x, int y, int z, int meta)
    {
        var item = GetItem().useOnBlock(this, entityPlayer, world, x, y, z, meta);
        if (item)
        {
            entityPlayer.IncreaseStat(Stats.Stats.Used[ItemId], 1);
        }

        return item;
    }

    public float GetMiningSpeedMultiplier(Block block) => GetItem().GetMiningSpeedMultiplier(this, block);

    public ItemStack Use(IWorldContext world, EntityPlayer entityPlayer) => GetItem().Use(this, world, entityPlayer);

    public NBTTagCompound WriteToNbt(NBTTagCompound nbt)
    {
        nbt.SetShort("id", (short)ItemId);
        nbt.SetByte("Count", (sbyte)Count);
        nbt.SetShort("Damage", (short)_damage);
        if (_components.Dictionary.Count != 0) nbt.SetCompoundTag("Components", CloneComponents());
        return nbt;
    }

    public void ReadFromNbt(NBTTagCompound nbt)
    {
        int itemId = nbt.GetShort("id");
        if (itemId != ItemId)
            throw new InvalidOperationException("An item stack cannot be rebound to a different runtime item.");
        Count = nbt.GetByte("Count");
        _damage = nbt.GetShort("Damage");
        _components = nbt.HasKey("Components")
            ? Normalize(nbt.GetCompoundTag("Components"))
            : new NBTTagCompound();
    }

    public int GetMaxCount() => GetItem().GetMaxCount();

    public bool IsStackable() => GetMaxCount() > 1 && (!IsDamageable() || !IsDamaged());

    public bool IsDamageable() => _item.GetMaxDamage() > 0;

    public bool GetHasSubtypes() => _item.GetHasSubtypes();

    public bool IsDamaged() => IsDamageable() && _damage > 0;

    public int GetDamage2() => _damage;

    public int GetDamage() => _damage;

    public void SetDamage(int damage) => _damage = damage;

    public int GetMaxDamage() => _item.GetMaxDamage();

    public void ConsumeItem(EntityPlayer player)
    {
        if (!player.GameMode.FiniteResources) return;

        Count--;
    }

    public void DamageItem(int damageAmount, Entity entity)
    {
        if (!IsDamageable())
        {
            return;
        }

        if (entity is EntityPlayer player)
        {
            DamageItemForced(damageAmount, player);
        }
        else
        {
            _damage += damageAmount;
            UpdateBroken();
        }
    }

    public void DamageItem(int damageAmount, EntityPlayer player)
    {
        if (!IsDamageable())
        {
            return;
        }

        DamageItemForced(damageAmount, player);
    }

    private void DamageItemForced(int damageAmount, EntityPlayer player)
    {
        if (!player.GameMode.FiniteResources)
        {
            return;
        }

        _damage += damageAmount;
        if (UpdateBroken())
        {
            player.IncreaseStat(Stats.Stats.Broken[ItemId], 1);
        }
    }

    private bool UpdateBroken()
    {
        if (_damage > GetMaxDamage())
        {
            --Count;
            if (Count < 0)
            {
                Count = 0;
            }

            _damage = 0;
            return true;
        }

        return false;
    }

    public void PostHit(EntityLiving entityLiving, EntityPlayer entityPlayer)
    {
        var hit = _item.PostHit(this, entityLiving, entityPlayer);
        if (hit)
        {
            entityPlayer.IncreaseStat(Stats.Stats.Used[ItemId], 1);
        }
    }

    public void PostMine(int blockId, int x, int y, int z, EntityPlayer entityPlayer)
    {
        var mined = _item.PostMine(this, blockId, x, y, z, entityPlayer);
        if (mined)
        {
            entityPlayer.IncreaseStat(Stats.Stats.Used[ItemId], 1);
        }
    }

    public int GetAttackDamage(Entity entity) => _item.GetAttackDamage(entity);

    public bool IsSuitableFor(Block block) => _item.IsSuitableFor(block);

    public static void OnRemoved(EntityPlayer entityPlayer)
    {
    }

    public void useOnEntity(EntityLiving entityLiving, EntityPlayer entityPlayer) => _item.useOnEntity(this, entityLiving, entityPlayer);

    public ItemStack Copy()
    {
        var copy = new ItemStack(_item, Count, _damage);
        copy._components = CloneComponents();
        return copy;
    }

    public static bool AreEqual(ItemStack? a, ItemStack? b) => (a == null && b == null) || (a != null && b != null && a.Equals2(b));

    private bool Equals2(ItemStack itemStack) =>
        Count == itemStack.Count
        && ItemId == itemStack.ItemId
        && _damage == itemStack._damage
        && SerializeComponents().AsSpan().SequenceEqual(itemStack.SerializeComponents());

    public bool IsItemEqual(ItemStack itemStack) =>
        ItemId == itemStack.ItemId
        && _damage == itemStack._damage
        && SerializeComponents().AsSpan().SequenceEqual(itemStack.SerializeComponents());

    public bool HasComponent(ResourceLocation key) => _components.HasKey(key.ToString());

    public string GetStringComponent(ResourceLocation key) => _components.GetString(key.ToString());

    public void SetStringComponent(ResourceLocation key, string value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _components.SetString(key.ToString(), value);
    }

    public bool RemoveComponent(ResourceLocation key) => _components.RemoveTag(key.ToString());

    internal byte[] SerializeComponents()
    {
        if (_components.Dictionary.Count == 0) return [];
        NBTTagCompound canonical = new();
        foreach (var (key, value) in _components.Dictionary.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            canonical.SetTag(key, value);
        using MemoryStream stream = new();
        NbtIo.Write(canonical, stream);
        var bytes = stream.ToArray();
        if (bytes.Length > MaxSerializedComponentBytes)
            throw new InvalidOperationException($"Item stack components exceed {MaxSerializedComponentBytes} bytes.");
        return bytes;
    }

    internal void ReadSerializedComponents(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > MaxSerializedComponentBytes)
            throw new InvalidDataException($"Item stack components exceed {MaxSerializedComponentBytes} bytes.");
        _components = bytes.Length == 0
            ? new NBTTagCompound()
            : Normalize(NbtIo.Read(new MemoryStream(bytes, false)));
    }

    private NBTTagCompound CloneComponents()
    {
        var bytes = SerializeComponents();
        return bytes.Length == 0
            ? new NBTTagCompound()
            : Normalize(NbtIo.Read(new MemoryStream(bytes, false)));
    }

    private static NBTTagCompound Normalize(NBTTagCompound components)
    {
        components.Key = string.Empty;
        return components;
    }

    public string GetItemName() => _item.GetItemNameIs(this);

    public string GetDisplayName() => _item.GetDisplayName(this);

    public static ItemStack? Clone(ItemStack? itemStack) => itemStack?.Copy();

    public override string ToString() => $"{Count}x{_item.GetItemName()}@{_damage}";

    public void InventoryTick(IWorldContext world, Entity entity, int slotIndex, bool shouldUpdate)
    {
        if (AnimationTime > 0)
        {
            --AnimationTime;
        }

        _item.InventoryTick(this, world, entity, slotIndex, shouldUpdate);
    }

    public void OnCraft(IWorldContext world, EntityPlayer entityPlayer)
    {
        entityPlayer.IncreaseStat(Stats.Stats.Crafted[ItemId], Count);
        _item.OnCraft(this, world, entityPlayer);
    }

    public bool Equals(ItemStack itemStack) => ItemId == itemStack.ItemId && Count == itemStack.Count && _damage == itemStack._damage;
}
