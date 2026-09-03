using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Items.Behaviors;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;
using OmniBlock.Registries;
using OmniBlock.Stats;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items;

public class Item
{
    public const int MaxBehaviors = 8;
    internal static JavaRandom s_itemRand = new();

    public readonly int Id;
    private readonly IItemBehavior?[] _behaviors = new IItemBehavior[MaxBehaviors];
    private int _behaviorCount;
    private Item _craftingReturnItem;
    private Item[] _repairIngredients = [];
    public bool Handheld { get; private set; }
    public bool HasSubtypes { get; private set; }
    private int _maxCount = 64;
    private int _maxDamage;
    internal int _textureId;
    private string _translationKey;
    private string[] _aliases = [];
    private bool _frozen;

    public bool IsFrozen => _frozen;

    internal Item(int id)
    {
        Id = 256 + id;
    }

    public int BehaviorCount => _behaviorCount;

    public IReadOnlyList<string> GetItemAlias =>
        _aliases.Concat(_behaviors.Take(_behaviorCount).SelectMany(behavior => behavior!.GetItemAliases(this))).Distinct().ToArray();

    internal Item SetAliases(IEnumerable<string> aliases)
    {
        EnsureMutable();
        _aliases = [.. aliases];
        return this;
    }

    public Item AddBehavior(IItemBehavior behavior)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(behavior);
        if (_behaviorCount == MaxBehaviors)
            throw new InvalidOperationException($"Item {Id} exceeds the limit of {MaxBehaviors} behaviors.");
        if (GetBehavior(behavior.GetType()) is not null)
            throw new InvalidOperationException($"Item {Id} has duplicate behavior type '{behavior.GetType().Name}'.");
        _behaviors[_behaviorCount++] = behavior;
        behavior.Apply(this);
        return this;
    }

    public TBehavior? GetBehavior<TBehavior>() where TBehavior : class, IItemBehavior
    {
        for (int i = 0; i < _behaviorCount; i++)
            if (_behaviors[i] is TBehavior behavior) return behavior;
        return null;
    }

    private IItemBehavior? GetBehavior(Type type)
    {
        for (int i = 0; i < _behaviorCount; i++)
            if (_behaviors[i]!.GetType() == type) return _behaviors[i];
        return null;
    }

    public Item SetTextureId(int textureId)
    {
        EnsureMutable();
        _textureId = textureId;
        return this;
    }

    public Item SetMaxCount(int maxCount)
    {
        EnsureMutable();
        _maxCount = maxCount;
        return this;
    }

    public virtual int GetTextureId(int damage)
    {
        for (int i = 0; i < _behaviorCount; i++)
        {
            int texture = _behaviors[i]!.GetTextureId(this, damage);
            if (texture != _textureId) return texture;
        }
        return _textureId;
    }

    public int GetTextureId(ItemStack stack) => GetTextureId(stack.GetDamage());

    public virtual bool useOnBlock(ItemStack itemStack, EntityPlayer entityPlayer, IWorldContext world, int x, int y, int z, int meta)
    {
        for (int i = 0; i < _behaviorCount; i++)
            if (_behaviors[i]!.UseOnBlock(this, itemStack, entityPlayer, world, x, y, z, meta)) return true;
        return false;
    }

    public float GetMiningSpeedMultiplier(ItemStack itemStack, Block block)
    {
        for (int i = 0; i < _behaviorCount; i++)
        {
            float speed = _behaviors[i]!.GetMiningSpeedMultiplier(this, itemStack, block);
            if (speed != 1.0F) return speed;
        }
        return 1.0F;
    }

    public ItemStack Use(ItemStack itemStack, IWorldContext world, EntityPlayer entityPlayer)
    {
        for (int i = 0; i < _behaviorCount; i++) itemStack = _behaviors[i]!.Use(this, itemStack, world, entityPlayer);
        return itemStack;
    }

    public int GetMaxCount() => _maxCount;

    protected virtual int GetPlacementMetadata(int meta) => 0;

    public bool GetHasSubtypes() => HasSubtypes;

    internal Item SetHasSubtypes(bool has)
    {
        EnsureMutable();
        HasSubtypes = has;
        return this;
    }

    public int GetMaxDamage() => _maxDamage;

    internal Item SetMaxDamage(int dmg)
    {
        EnsureMutable();
        _maxDamage = dmg;
        return this;
    }

    public bool IsDamagable() => _maxDamage > 0 && !HasSubtypes;

    public bool PostHit(ItemStack itemStack, EntityLiving entityLiving, EntityPlayer entityPlayer)
    {
        bool handled = false;
        for (int i = 0; i < _behaviorCount; i++) handled |= _behaviors[i]!.PostHit(this, itemStack, entityLiving, entityPlayer);
        return handled;
    }

    public bool PostMine(ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving entityLiving)
    {
        bool handled = false;
        for (int i = 0; i < _behaviorCount; i++) handled |= _behaviors[i]!.PostMine(this, itemStack, blockId, x, y, z, entityLiving);
        return handled;
    }

    public int GetAttackDamage(Entity entity)
    {
        for (int i = 0; i < _behaviorCount; i++)
        {
            int damage = _behaviors[i]!.GetAttackDamage(this, entity);
            if (damage != 1) return damage;
        }
        return 1;
    }

    public bool IsSuitableFor(Block block)
    {
        for (int i = 0; i < _behaviorCount; i++) if (_behaviors[i]!.IsSuitableFor(this, block)) return true;
        return false;
    }

    public void useOnEntity(ItemStack itemStack, EntityLiving entityLiving, EntityPlayer entityPlayer)
    {
        for (int i = 0; i < _behaviorCount; i++) _behaviors[i]!.UseOnEntity(this, itemStack, entityLiving, entityPlayer);
    }

    public Item SetHandheld()
    {
        EnsureMutable();
        Handheld = true;
        return this;
    }

    public bool IsHandheld()
    {
        if (Handheld) return true;
        for (int i = 0; i < _behaviorCount; i++) if (_behaviors[i]!.IsHandheld(this)) return true;
        return false;
    }

    public bool IsHandheldRod()
    {
        for (int i = 0; i < _behaviorCount; i++) if (_behaviors[i]!.IsHandheldRod(this)) return true;
        return false;
    }

    public Item SetItemName(string name)
    {
        EnsureMutable();
        _translationKey = $"item.{name}";
        return this;
    }

    public virtual string GetItemName() => _translationKey;

    public virtual string GetItemNameIs(ItemStack itemStack)
    {
        for (int i = 0; i < _behaviorCount; i++)
        {
            string name = _behaviors[i]!.GetItemNameIS(this, itemStack);
            if (name != _translationKey) return name;
        }
        return _translationKey;
    }

    public Item SetCraftingReturnItem(Item item)
    {
        EnsureMutable();
        if (_maxCount > 1)
        {
            throw new ArgumentException("Max stack size must be 1 for items with crafting results");
        }

        _craftingReturnItem = item;
        return this;
    }

    public Item GetContainerItem() => _craftingReturnItem;

    public bool HasContainerItem() => _craftingReturnItem != null;

    public IReadOnlyList<Item> RepairIngredients => _repairIngredients;

    internal void SetRepairIngredients(Item[] ingredients)
    {
        EnsureMutable();
        _repairIngredients = ingredients;
    }

    internal void Freeze() => _frozen = true;

    private void EnsureMutable()
    {
        if (_frozen) throw new InvalidOperationException($"Item {Id} has been finalized and cannot be mutated.");
    }

    public string GetStatName()
        => StatCollector.TranslateToLocal(GetItemName() + ".name");

    public virtual int GetColorMultiplier(int color) => 0xFFFFFF;

    public void InventoryTick(ItemStack itemStack, IWorldContext world, Entity entity, int slotIndex, bool shouldUpdate)
    {
        for (int i = 0; i < _behaviorCount; i++) _behaviors[i]!.InventoryTick(this, itemStack, world, entity, slotIndex, shouldUpdate);
    }

    public void OnCraft(ItemStack itemStack, IWorldContext world, EntityPlayer entityPlayer)
    {
        for (int i = 0; i < _behaviorCount; i++) _behaviors[i]!.OnCraft(this, itemStack, world, entityPlayer);
    }

    public bool IsNetworkSynced()
    {
        for (int i = 0; i < _behaviorCount; i++) if (_behaviors[i]!.IsNetworkSynced(this)) return true;
        return false;
    }

    public Message? GetUpdatePacket(ItemStack stack, IWorldContext world, EntityPlayer player)
    {
        for (int i = 0; i < _behaviorCount; i++)
            if (_behaviors[i]!.GetUpdatePacket(this, stack, world, player) is { } packet) return packet;
        return null;
    }

}
