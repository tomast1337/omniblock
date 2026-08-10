using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.NBT;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items;

public class ItemStack
{
    private int _damage;
    public int AnimationTime;
    public int Count;
    public int ItemId;

    public ItemStack(Block block) : this((Block)block, 1)
    {
    }

    public ItemStack(Block block, int count) : this(block.Id, count, 0)
    {
    }

    public ItemStack(Item item) : this(item.Id, 1, 0)
    {
    }

    public ItemStack(Item item, int count) : this(item.Id, count, 0)
    {
    }

    public ItemStack(Item item, int count, int damage) : this(item.Id, count, damage)
    {
    }

    public ItemStack(int itemId, int count, int damage)
    {
        Count = 0;
        ItemId = itemId;
        Count = count;
        _damage = damage;
    }

    public ItemStack(NBTTagCompound nbt)
    {
        Count = 0;
        ReadFromNbt(nbt);
    }

    public ItemStack Split(int splitAmount)
    {
        Count -= splitAmount;
        return new ItemStack(ItemId, splitAmount, _damage);
    }

    public Item GetItem() => Item.Items[ItemId];

    public int GetTextureId() => GetItem().GetTextureId(this);

    public bool useOnBlock(EntityPlayer entityPlayer, IWorldContext world, int x, int y, int z, int meta)
    {
        bool item = GetItem().useOnBlock(this, entityPlayer, world, x, y, z, meta);
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
        return nbt;
    }

    public void ReadFromNbt(NBTTagCompound nbt)
    {
        ItemId = nbt.GetShort("id");
        Count = nbt.GetByte("Count");
        _damage = nbt.GetShort("Damage");
    }

    public int GetMaxCount() => GetItem().GetMaxCount();

    public bool IsStackable() => GetMaxCount() > 1 && (!IsDamageable() || !IsDamaged());

    public bool IsDamageable() => Item.Items[ItemId].GetMaxDamage() > 0;

    public bool GetHasSubtypes() => Item.Items[ItemId].GetHasSubtypes();

    public bool IsDamaged() => IsDamageable() && _damage > 0;

    public int GetDamage2() => _damage;

    public int GetDamage() => _damage;

    public void SetDamage(int damage) => _damage = damage;

    public int GetMaxDamage() => Item.Items[ItemId].GetMaxDamage();

    public void ConsumeItem(EntityPlayer player)
    {
        if (!player.GameMode.FiniteResources)
        {
            return;
        }

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
        bool hit = Item.Items[ItemId].PostHit(this, entityLiving, entityPlayer);
        if (hit)
        {
            entityPlayer.IncreaseStat(Stats.Stats.Used[ItemId], 1);
        }
    }

    public void PostMine(int blockId, int x, int y, int z, EntityPlayer entityPlayer)
    {
        bool mined = Item.Items[ItemId].PostMine(this, blockId, x, y, z, entityPlayer);
        if (mined)
        {
            entityPlayer.IncreaseStat(Stats.Stats.Used[ItemId], 1);
        }
    }

    public int GetAttackDamage(Entity entity) => Item.Items[ItemId].GetAttackDamage(entity);

    public bool IsSuitableFor(Block block) => Item.Items[ItemId].IsSuitableFor(block);

    public static void OnRemoved(EntityPlayer entityPlayer)
    {
    }

    public void useOnEntity(EntityLiving entityLiving, EntityPlayer entityPlayer) => Item.Items[ItemId].useOnEntity(this, entityLiving, entityPlayer);

    public ItemStack Copy() => new(ItemId, Count, _damage);

    public static bool AreEqual(ItemStack? a, ItemStack? b) => a == null && b == null || a != null && b != null && a.Equals2(b);

    private bool Equals2(ItemStack itemStack) => Count == itemStack.Count && ItemId == itemStack.ItemId && _damage == itemStack._damage;

    public bool IsItemEqual(ItemStack itemStack) => ItemId == itemStack.ItemId && _damage == itemStack._damage;

    public string GetItemName() => Item.Items[ItemId].GetItemNameIs(this);

    public static ItemStack Clone(ItemStack itemStack) => itemStack == null ? null : itemStack.Copy();

    public override string ToString() => $"{Count}x{Item.Items[ItemId].GetItemName()}@{_damage}";

    public void InventoryTick(IWorldContext world, Entity entity, int slotIndex, bool shouldUpdate)
    {
        if (AnimationTime > 0)
        {
            --AnimationTime;
        }

        Item.Items[ItemId].InventoryTick(this, world, entity, slotIndex, shouldUpdate);
    }

    public void OnCraft(IWorldContext world, EntityPlayer entityPlayer)
    {
        entityPlayer.IncreaseStat(Stats.Stats.Crafted[ItemId], Count);
        Item.Items[ItemId].OnCraft(this, world, entityPlayer);
    }

    public bool Equals(ItemStack itemStack) => ItemId == itemStack.ItemId && Count == itemStack.Count && _damage == itemStack._damage;
}
