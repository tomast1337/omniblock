using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Items.Behaviors;
using OmniBlock.Network.Messages;
using OmniBlock.Stats;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Items;

public class Item
{
    public const int MaxBehaviors = 8;
    internal static JavaRandom s_itemRand = new();
    private readonly IItemBehavior?[] _behaviors = new IItemBehavior[MaxBehaviors];

    public readonly int Id;
    private string[] _aliases = [];
    private Item _craftingReturnItem;
    private int _maxCount = 64;
    private int _maxDamage;
    private Item[] _repairIngredients = [];
    internal int _textureId;
    private string _translationKey;

    internal Item(int id) => Id = 256 + id;
    public bool Handheld { get; private set; }
    public bool HasSubtypes { get; private set; }

    public bool IsFrozen { get; private set; }

    public int BehaviorCount { get; private set; }

    public IReadOnlyList<string> GetItemAlias =>
        _aliases.Concat(_behaviors.Take(BehaviorCount).SelectMany(behavior => behavior!.GetItemAliases(this))).Distinct().ToArray();

    public IReadOnlyList<Item> RepairIngredients => _repairIngredients;

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
        if (BehaviorCount == MaxBehaviors)
            throw new InvalidOperationException($"Item {Id} exceeds the limit of {MaxBehaviors} behaviors.");
        if (GetBehavior(behavior.GetType()) is not null)
            throw new InvalidOperationException($"Item {Id} has duplicate behavior type '{behavior.GetType().Name}'.");
        _behaviors[BehaviorCount++] = behavior;
        behavior.Apply(this);
        return this;
    }

    public TBehavior? GetBehavior<TBehavior>() where TBehavior : class, IItemBehavior
    {
        for (var i = 0; i < BehaviorCount; i++)
            if (_behaviors[i] is TBehavior behavior)
                return behavior;
        return null;
    }

    private IItemBehavior? GetBehavior(Type type)
    {
        for (var i = 0; i < BehaviorCount; i++)
            if (_behaviors[i]!.GetType() == type)
                return _behaviors[i];
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
        for (var i = 0; i < BehaviorCount; i++)
        {
            var texture = _behaviors[i]!.GetTextureId(this, damage);
            if (texture != _textureId) return texture;
        }

        return _textureId;
    }

    public int GetTextureId(ItemStack stack) => GetTextureId(stack.GetDamage());

    public virtual bool useOnBlock(ItemStack itemStack, EntityPlayer entityPlayer, IWorldContext world, int x, int y, int z, int meta)
    {
        for (var i = 0; i < BehaviorCount; i++)
            if (_behaviors[i]!.UseOnBlock(this, itemStack, entityPlayer, world, x, y, z, meta))
                return true;
        return false;
    }

    public float GetMiningSpeedMultiplier(ItemStack itemStack, Block block)
    {
        for (var i = 0; i < BehaviorCount; i++)
        {
            var speed = _behaviors[i]!.GetMiningSpeedMultiplier(this, itemStack, block);
            if (speed != 1.0F) return speed;
        }

        return 1.0F;
    }

    public ItemStack Use(ItemStack itemStack, IWorldContext world, EntityPlayer entityPlayer)
    {
        for (var i = 0; i < BehaviorCount; i++) itemStack = _behaviors[i]!.Use(this, itemStack, world, entityPlayer);
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
        var handled = false;
        for (var i = 0; i < BehaviorCount; i++) handled |= _behaviors[i]!.PostHit(this, itemStack, entityLiving, entityPlayer);
        return handled;
    }

    public bool PostMine(ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving entityLiving)
    {
        var handled = false;
        for (var i = 0; i < BehaviorCount; i++) handled |= _behaviors[i]!.PostMine(this, itemStack, blockId, x, y, z, entityLiving);
        return handled;
    }

    public int GetAttackDamage(Entity entity)
    {
        for (var i = 0; i < BehaviorCount; i++)
        {
            var damage = _behaviors[i]!.GetAttackDamage(this, entity);
            if (damage != 1) return damage;
        }

        return 1;
    }

    public bool IsSuitableFor(Block block)
    {
        for (var i = 0; i < BehaviorCount; i++)
            if (_behaviors[i]!.IsSuitableFor(this, block))
                return true;
        return false;
    }

    public void useOnEntity(ItemStack itemStack, EntityLiving entityLiving, EntityPlayer entityPlayer)
    {
        for (var i = 0; i < BehaviorCount; i++) _behaviors[i]!.UseOnEntity(this, itemStack, entityLiving, entityPlayer);
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
        for (var i = 0; i < BehaviorCount; i++)
            if (_behaviors[i]!.IsHandheld(this))
                return true;
        return false;
    }

    public bool IsHandheldRod()
    {
        for (var i = 0; i < BehaviorCount; i++)
            if (_behaviors[i]!.IsHandheldRod(this))
                return true;
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
        for (var i = 0; i < BehaviorCount; i++)
        {
            var name = _behaviors[i]!.GetItemNameIS(this, itemStack);
            if (name != _translationKey) return name;
        }

        return _translationKey;
    }

    public virtual string GetDisplayName(ItemStack itemStack) =>
        Translations.GetNamed(GetItemNameIs(itemStack));

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

    internal void SetRepairIngredients(Item[] ingredients)
    {
        EnsureMutable();
        _repairIngredients = ingredients;
    }

    internal void Freeze() => IsFrozen = true;

    private void EnsureMutable()
    {
        if (IsFrozen) throw new InvalidOperationException($"Item {Id} has been finalized and cannot be mutated.");
    }

    public string GetStatName()
        => StatCollector.TranslateToLocal(GetItemName() + ".name");

    public virtual int GetColorMultiplier(int color) => 0xFFFFFF;

    public void InventoryTick(ItemStack itemStack, IWorldContext world, Entity entity, int slotIndex, bool shouldUpdate)
    {
        for (var i = 0; i < BehaviorCount; i++) _behaviors[i]!.InventoryTick(this, itemStack, world, entity, slotIndex, shouldUpdate);
    }

    public void OnCraft(ItemStack itemStack, IWorldContext world, EntityPlayer entityPlayer)
    {
        for (var i = 0; i < BehaviorCount; i++) _behaviors[i]!.OnCraft(this, itemStack, world, entityPlayer);
    }

    public bool IsNetworkSynced()
    {
        for (var i = 0; i < BehaviorCount; i++)
            if (_behaviors[i]!.IsNetworkSynced(this))
                return true;
        return false;
    }

    public Message? GetUpdatePacket(ItemStack stack, IWorldContext world, EntityPlayer player)
    {
        for (var i = 0; i < BehaviorCount; i++)
            if (_behaviors[i]!.GetUpdatePacket(this, stack, world, player) is { } packet)
                return packet;
        return null;
    }
}
