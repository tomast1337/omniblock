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
using Microsoft.Extensions.Logging;

namespace OmniBlock.Items;

public class Item
{
    internal static JavaRandom s_itemRand = new();
    public static Item?[] Items = new Item[32000];

    private static Block[]? s_spadeBlocksLazy;
    internal static Block[] s_spadeBlocks => s_spadeBlocksLazy ??=
        [BlockRegistry.Get("grass_block"), BlockRegistry.Get("dirt"), BlockRegistry.Get("sand"), BlockRegistry.Get("gravel"), BlockRegistry.Get("snow"), BlockRegistry.Get("snow_block"), BlockRegistry.Get("clay"), BlockRegistry.Get("farmland")];

    private static Block[]? s_pickaxeBlocksLazy;
    internal static Block[] s_pickaxeBlocks => s_pickaxeBlocksLazy ??= [BlockRegistry.Get("cobblestone"), BlockRegistry.Get("double_slab"), BlockRegistry.Get("slab"), BlockRegistry.Get("stone"), BlockRegistry.Get("sandstone"), BlockRegistry.Get("mossy_cobblestone"), BlockRegistry.Get("iron_ore"), BlockRegistry.Get("iron_block"), BlockRegistry.Get("coal_ore"), BlockRegistry.Get("gold_block"), BlockRegistry.Get("gold_ore"), BlockRegistry.Get("diamond_ore"), BlockRegistry.Get("diamond_block"), BlockRegistry.Get("ice"), BlockRegistry.Get("netherrack"), BlockRegistry.Get("lapis_ore"), BlockRegistry.Get("lapis_block"), BlockRegistry.Get("redstone_ore"), BlockRegistry.Get("cobblestone_stairs")];

    private static Block[]? s_axeBlocksLazy;
    internal static Block[] s_axeBlocks => s_axeBlocksLazy ??= [BlockRegistry.Get("planks"), BlockRegistry.Get("bookshelf"), BlockRegistry.Get("log"), BlockRegistry.Get("chest"), BlockRegistry.Get("crafting_table"), BlockRegistry.Get("wooden_stairs"), BlockRegistry.Get("ladder"), BlockRegistry.Get("trapdoor"), BlockRegistry.Get("fence")];

    /// <summary>
    /// Resolves an item by its registry path (e.g. <c>"apple"</c>, <c>"shovel_iron"</c> —
    /// see <c>OmniBlock/assets/item/omniblock/*.json</c> for the full list of names).
    /// Requires <see cref="Registries.DefaultRegistries.Initialize"/> to have run.
    ///
    /// TODO: This will become obsolete once Entities and Blocks are fully data-driven
    /// and resolve their drops/interactions via ResourceLocations directly from JSON data files.
    /// </summary>
    public static Item ByName(string name)
    {
        ResourceLocation key = new(Namespace.OmniBlock, name);
        if (ContentRuntime.TryGetCurrent(out ContentRuntime? runtime))
        {
            if (runtime.Items.TryGet(key, out Item? runtimeItem)) return runtimeItem;
            throw new ArgumentException($"Unknown item: '{name}'", nameof(name));
        }

        ItemDefinition? def = DefaultRegistries.Items.Get(new ResourceLocation(Namespace.OmniBlock, name))?.Value;
        if (def is null || Items[def.ProtocolId] is not { } item)
        {
            throw new ArgumentException($"Unknown item: '{name}'", nameof(name));
        }

        return item;
    }

    private readonly ILogger<Item> _logger = Log.Instance.For<Item>();

    public readonly int Id;
    private IItemBehavior? _behavior;
    private Item _craftingReturnItem;
    public bool Handheld { get; private set; }
    public bool HasSubtypes { get; private set; }
    private int _maxCount = 64;
    private int _maxDamage;
    internal int _textureId;
    private string _translationKey;
    private bool _frozen;

    public bool IsFrozen => _frozen;

    internal Item(int id, bool publishLegacy = true)
    {
        Id = 256 + id;
        if (publishLegacy && Items[256 + id] != null)
        {
            _logger.LogInformation($"CONFLICT @ {id}");
        }

        if (publishLegacy) Items[256 + id] = this;
    }

    public IReadOnlyList<string> GetItemAlias => _behavior?.GetItemAliases(this) ?? [];

    public Item SetBehavior(IItemBehavior behavior)
    {
        EnsureMutable();
        _behavior = behavior;
        behavior.Apply(this);
        return this;
    }

    public TBehavior? GetBehavior<TBehavior>() where TBehavior : class, IItemBehavior => _behavior as TBehavior;

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

    public virtual int GetTextureId(int damage) => _behavior?.GetTextureId(this, damage) ?? _textureId;

    public int GetTextureId(ItemStack stack) => GetTextureId(stack.GetDamage());

    public virtual bool useOnBlock(ItemStack itemStack, EntityPlayer entityPlayer, IWorldContext world, int x, int y, int z, int meta) => _behavior?.UseOnBlock(this, itemStack, entityPlayer, world, x, y, z, meta) ?? false;

    public float GetMiningSpeedMultiplier(ItemStack itemStack, Block block) => _behavior?.GetMiningSpeedMultiplier(this, itemStack, block) ?? 1.0F;

    public ItemStack Use(ItemStack itemStack, IWorldContext world, EntityPlayer entityPlayer) => _behavior?.Use(this, itemStack, world, entityPlayer) ?? itemStack;

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

    public bool PostHit(ItemStack itemStack, EntityLiving entityLiving, EntityPlayer entityPlayer) => _behavior?.PostHit(this, itemStack, entityLiving, entityPlayer) ?? false;

    public bool PostMine(ItemStack itemStack, int blockId, int x, int y, int z, EntityLiving entityLiving) => _behavior?.PostMine(this, itemStack, blockId, x, y, z, entityLiving) ?? false;

    public int GetAttackDamage(Entity entity) => _behavior?.GetAttackDamage(this, entity) ?? 1;

    public bool IsSuitableFor(Block block) => _behavior?.IsSuitableFor(this, block) ?? false;

    public void useOnEntity(ItemStack itemStack, EntityLiving entityLiving, EntityPlayer entityPlayer) => _behavior?.UseOnEntity(this, itemStack, entityLiving, entityPlayer);

    public Item SetHandheld()
    {
        EnsureMutable();
        Handheld = true;
        return this;
    }

    public bool IsHandheld() => _behavior?.IsHandheld(this) ?? Handheld;

    public bool IsHandheldRod() => _behavior?.IsHandheldRod(this) ?? false;

    public Item SetItemName(string name)
    {
        EnsureMutable();
        _translationKey = $"item.{name}";
        return this;
    }

    public virtual string GetItemName() => _translationKey;

    public virtual string GetItemNameIs(ItemStack itemStack) => _behavior?.GetItemNameIS(this, itemStack) ?? _translationKey;

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

    internal void Freeze() => _frozen = true;

    private void EnsureMutable()
    {
        if (_frozen) throw new InvalidOperationException($"Item {Id} has been finalized and cannot be mutated.");
    }

    public string GetStatName()
        => StatCollector.TranslateToLocal(GetItemName() + ".name");

    public virtual int GetColorMultiplier(int color) => 0xFFFFFF;

    public void InventoryTick(ItemStack itemStack, IWorldContext world, Entity entity, int slotIndex, bool shouldUpdate) => _behavior?.InventoryTick(this, itemStack, world, entity, slotIndex, shouldUpdate);

    public void OnCraft(ItemStack itemStack, IWorldContext world, EntityPlayer entityPlayer) => _behavior?.OnCraft(this, itemStack, world, entityPlayer);

    public bool IsNetworkSynced() => _behavior?.IsNetworkSynced(this) ?? false;

    public Message? GetUpdatePacket(ItemStack stack, IWorldContext world, EntityPlayer player) => _behavior?.GetUpdatePacket(this, stack, world, player);

    internal static Func<Block, bool> PickaxeSuitableFor(ToolMaterial material) => block =>
    {
        if (block == BlockRegistry.Get("obsidian"))
        {
            return material.HarvestLevel == 3;
        }

        if (block == BlockRegistry.Get("diamond_block") || block == BlockRegistry.Get("diamond_ore"))
        {
            return material.HarvestLevel >= 2;
        }

        if (block == BlockRegistry.Get("gold_block") || block == BlockRegistry.Get("gold_ore"))
        {
            return material.HarvestLevel >= 2;
        }

        if (block == BlockRegistry.Get("iron_block") || block == BlockRegistry.Get("iron_ore"))
        {
            return material.HarvestLevel >= 1;
        }

        if (block == BlockRegistry.Get("lapis_block") || block == BlockRegistry.Get("lapis_ore"))
        {
            return material.HarvestLevel >= 1;
        }

        if (block == BlockRegistry.Get("redstone_ore") || block == BlockRegistry.Get("lit_redstone_ore"))
        {
            return material.HarvestLevel >= 2;
        }

        return block.Material == Material.Stone || block.Material == Material.Metal;
    };
}
