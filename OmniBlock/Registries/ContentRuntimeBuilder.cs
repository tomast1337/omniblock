using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;

namespace OmniBlock.Registries;

/// <summary>
/// Owns the mutable provider set and dependency context used while compiling content definitions.
/// A builder is confined to bootstrap; runtime blocks retain only the immutable behaviors it
/// creates. Future native and Luau providers register with this owner during the Registry phase.
/// </summary>
public sealed class ContentRuntimeBuilder : IItemRuntimeView
{
    private readonly List<(ResourceLocation Key, BlockDefinition Definition, Block Block)> _blocks = [];
    private readonly Dictionary<ResourceLocation, Block> _blocksByKey = [];
    private readonly Dictionary<int, Block> _blocksByProtocolId = [];
    private readonly List<(ResourceLocation Key, Item Item)> _blockItems = [];
    private readonly List<BlockDefinition> _pendingBlockDefinitions = [];
    private readonly List<ItemDefinition> _pendingItemDefinitions = [];
    private readonly List<(ResourceLocation Key, ItemDefinition Definition, Item Item)> _items = [];
    private readonly Dictionary<ResourceLocation, Item> _itemsByKey = [];
    private readonly Dictionary<int, Item> _itemsByProtocolId = [];
    private readonly StagedBlockRuntimeView _blockRuntimeView;
    private bool _itemDraftsCreated;
    private bool _itemsFinalized;
    private bool _built;

    public ContentRuntimeBuilder(
        IBlockBehaviorProviderRegistry blockBehaviorProviders,
        BlockBuildContext blockBuildContext,
        IItemBehaviorProviderRegistry itemBehaviorProviders,
        ItemBuildContext itemBuildContext,
        StagedBlockRuntimeView? blockRuntimeView = null)
    {
        ArgumentNullException.ThrowIfNull(blockBehaviorProviders);
        BlockBehaviorProviders = blockBehaviorProviders;
        ArgumentNullException.ThrowIfNull(itemBehaviorProviders);
        ItemBehaviorProviders = itemBehaviorProviders;
        _blockRuntimeView = blockRuntimeView ?? new StagedBlockRuntimeView();
        BlockBuildContext = blockBuildContext;
        ItemBuildContext = itemBuildContext;
    }

    public IBlockBehaviorProviderRegistry BlockBehaviorProviders { get; }
    public BlockBuildContext BlockBuildContext { get; }
    public IItemBehaviorProviderRegistry ItemBehaviorProviders { get; }
    public ItemBuildContext ItemBuildContext { get; }
    public BehaviorBuildContext BehaviorBuildContext => BlockBuildContext.Behaviors;

    public object BuildBlockBehavior(ResourceLocation type, JsonElement definition) =>
        BlockBehaviorProviders.Build(type, definition, BehaviorBuildContext);

    internal void AddBlockDefinition(BlockDefinition definition)
    {
        if (_built) throw new InvalidOperationException("Cannot add content after the runtime has been built.");
        ArgumentNullException.ThrowIfNull(definition);
        _pendingBlockDefinitions.Add(definition);
    }

    internal void AddItemDefinition(ItemDefinition definition)
    {
        if (_built) throw new InvalidOperationException("Cannot add content after the runtime has been built.");
        ArgumentNullException.ThrowIfNull(definition);
        _pendingItemDefinitions.Add(definition);
    }

    public Item Get(ResourceLocation key) => _itemsByKey.TryGetValue(key, out Item? item)
        ? item : throw new KeyNotFoundException($"Unknown item '{key}'.");

    public Item GetByProtocolId(int protocolId) => _itemsByProtocolId.TryGetValue(protocolId, out Item? item)
        ? item : throw new KeyNotFoundException($"Unknown item protocol id {protocolId}.");

    public bool TryGet(ResourceLocation key, out Item? item) => _itemsByKey.TryGetValue(key, out item);
    public bool TryGetByProtocolId(int protocolId, out Item? item) => _itemsByProtocolId.TryGetValue(protocolId, out item);

    internal Item GetItem(ResourceLocation key) => Get(key);
    internal Item GetItemByProtocolId(int protocolId) => GetByProtocolId(protocolId);

    internal void BuildItemsForBootstrap()
    {
        CreatePendingItemDrafts();
    }

    internal void FinalizeItemsForBootstrap() => BuildPendingItems();

    internal void AddBlock(BlockDefinition definition, Block block)
    {
        if (_built) throw new InvalidOperationException("Cannot add content after the runtime has been built.");
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(block);
        if (definition.ProtocolId is < 0 or >= BlockRegistry.ProtocolIdCapacity)
            throw new ArgumentOutOfRangeException(
                nameof(definition), definition.ProtocolId,
                $"Block protocol id must be between 0 and {BlockRegistry.ProtocolIdCapacity - 1}.");

        ResourceLocation key = new(definition.Namespace, definition.Name);
        _blocksByKey.TryAdd(key, block);
        _blocksByProtocolId.TryAdd(definition.ProtocolId, block);
        _blockRuntimeView.Add(key, block);
        _blocks.Add((key, definition, block));
    }

    internal Block GetBlock(ResourceLocation key) =>
        _blocksByKey.TryGetValue(key, out Block? block)
            ? block
            : throw new KeyNotFoundException($"Unknown block '{key}'.");

    private Block ResolveBlockReference(ResourceLocation key)
    {
        if (_blocksByKey.TryGetValue(key, out Block? exact)) return exact;
        foreach ((ResourceLocation candidateKey, BlockDefinition definition, Block block) in _blocks)
        {
            if (candidateKey.Namespace == key.Namespace && definition.TranslationKey == key.Path) return block;
        }
        throw new KeyNotFoundException($"Unknown block '{key}'.");
    }

    private int ResolveLootItemOrBlockId(ResourceLocation key)
    {
        if (_itemsByKey.TryGetValue(key, out Item? item)) return item.Id;
        foreach ((ResourceLocation _, BlockDefinition definition, Block block) in _blocks)
            if (definition.BlockItem.Aliases.Contains(key.Path, StringComparer.OrdinalIgnoreCase)) return block.Id;
        return ResolveBlockReference(key).Id;
    }

    internal Block GetBlockByProtocolId(int protocolId) =>
        _blocksByProtocolId.TryGetValue(protocolId, out Block? block)
            ? block
            : throw new KeyNotFoundException($"Unknown block protocol id {protocolId}.");

    internal bool TryGetBlockByProtocolId(int protocolId, out Block? block)
    {
        return _blocksByProtocolId.TryGetValue(protocolId, out block);
    }

    internal void BuildBlockItems(IEnumerable<BlockDefinition> definitions)
    {
        if (_built) throw new InvalidOperationException("Cannot add content after the runtime has been built.");
        if (_blockItems.Count != 0) throw new InvalidOperationException("Block items have already been built.");

        var staged = new List<(ResourceLocation Key, Block Block, Item Item)>();
        var keys = new HashSet<ResourceLocation>();
        var protocolIds = new HashSet<int>();
        foreach (BlockDefinition definition in definitions)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            if (!keys.Add(key)) throw new InvalidOperationException($"Duplicate block-item key '{key}'.");
            if (!protocolIds.Add(definition.ProtocolId))
                throw new InvalidOperationException($"Duplicate block-item protocol id {definition.ProtocolId}.");
            Block block = GetBlock(key);
            Item item = BlockItemFactory.Create(definition, block);
            if (item.Id != block.Id)
                throw new InvalidOperationException($"Block item '{key}' has id {item.Id}, expected {block.Id}.");
            if (_itemsByProtocolId.ContainsKey(item.Id))
                throw new InvalidOperationException($"Block item '{key}' collides with item protocol id {item.Id}.");
            staged.Add((key, block, item));
        }

        foreach ((ResourceLocation key, Block block, Item item) in staged)
        {
            _blockItems.Add((key, item));
            _itemsByKey.TryAdd(key, item);
            _itemsByProtocolId.Add(item.Id, item);
            block.Init();
        }
    }

    public ContentRuntime Build()
    {
        if (_built) throw new InvalidOperationException("This content runtime builder has already been built.");

        CreatePendingItemDrafts();
        BuildPendingBlockDefinitions();
        BuildPendingItems();
        ValidateBlocks();
        foreach ((_, _, Block block) in _blocks) block.Freeze();
        foreach ((_, _, Item item) in _items) item.Freeze();
        foreach ((_, Item item) in _blockItems) item.Freeze();
        _blockRuntimeView.Freeze();
        ContentRuntime runtime = new(
            _blocks.Select(static entry => (entry.Key, entry.Block)),
            _items.Select(static entry => (entry.Key, entry.Item)),
            _blockItems,
            BlockBehaviorProviders,
            ItemBehaviorProviders);
        _built = true;
        return runtime;
    }

    private void BuildPendingItems()
    {
        if (_pendingItemDefinitions.Count == 0 || _itemsFinalized) return;

        CreatePendingItemDrafts();

        foreach ((ResourceLocation key, ItemDefinition definition, Item item) in _items)
        {
            try
            {
                ItemFactory.AttachBehavior(item, definition, ItemBuildContext, ItemBehaviorProviders);
                if (definition.CraftingReturnItemProtocolId is { } returnId)
                    item.SetCraftingReturnItem(GetItemByProtocolId(returnId));
                item.SetRepairIngredients([.. definition.RepairIngredients.Select(reference =>
                    ItemBuildContext.ResolveItem(ResourceLocation.Parse(reference)))]);
                if (item.BehaviorCount != definition.Behaviors.Length)
                    throw new InvalidOperationException(
                        $"built {item.BehaviorCount} of {definition.Behaviors.Length} declared behaviors");
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Item '{key}' failed reference validation: {error.Message}", error);
            }
        }

        foreach ((_, _, Item item) in _items) item.Freeze();
        _itemsFinalized = true;
    }

    private void CreatePendingItemDrafts()
    {
        if (_itemDraftsCreated) return;

        foreach (ItemDefinition definition in _pendingItemDefinitions)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            if (definition.ProtocolId is < 256 or >= 32000)
                throw new InvalidOperationException($"Item '{key}' has invalid protocol id {definition.ProtocolId}.");
            if (_itemsByKey.ContainsKey(key)) throw new InvalidOperationException($"Duplicate item key '{key}'.");
            if (_itemsByProtocolId.ContainsKey(definition.ProtocolId))
                throw new InvalidOperationException($"Duplicate item protocol id {definition.ProtocolId} for '{key}'.");
            try
            {
                Item item = ItemFactory.CreateDraft(definition, ItemBuildContext);
                _itemsByKey.Add(key, item);
                _itemsByProtocolId.Add(definition.ProtocolId, item);
                _items.Add((key, definition, item));
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Item '{key}' failed construction: {error.Message}", error);
            }
        }
        _itemDraftsCreated = true;
    }

    private void BuildPendingBlockDefinitions()
    {
        if (_pendingBlockDefinitions.Count == 0) return;
        List<BlockDefinition> definitions = ContentIdAllocator.AssignBlockIds(_pendingBlockDefinitions);
        foreach (BlockDefinition definition in definitions)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            try
            {
                AddBlock(definition, BlockFactory.Create(definition, BlockBuildContext));
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Block '{key}' failed construction: {error.Message}", error);
            }
        }

        foreach (BlockDefinition definition in definitions)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            try
            {
                BlockFactory.AttachBehaviors(GetBlock(key), definition, BlockBehaviorProviders, BlockBuildContext);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Block '{key}' failed reference validation: {error.Message}", error);
            }
        }
    }

    private void ValidateBlocks()
    {
        HashSet<ResourceLocation> keys = [];
        HashSet<int> protocolIds = [];
        foreach ((ResourceLocation key, BlockDefinition definition, Block block) in _blocks)
        {
            if (!keys.Add(key)) throw new InvalidOperationException($"Duplicate block key '{key}'.");
            if (!protocolIds.Add(definition.ProtocolId))
                throw new InvalidOperationException($"Duplicate block protocol id {definition.ProtocolId}.");
            if (block.Id != definition.ProtocolId)
                throw new InvalidOperationException(
                    $"Block '{key}' was constructed with id {block.Id}, expected {definition.ProtocolId}.");

            ValidateSlots(key, definition, block);
        }
    }

    private static void ValidateSlots(ResourceLocation key, BlockDefinition definition, Block block)
    {
        HashSet<string> occupiedSlots = [];
        foreach (JsonElement behavior in definition.Behaviors)
        {
            foreach (JsonElement slotElement in behavior.GetProperty("Slots").EnumerateArray())
            {
                string slot = slotElement.GetString()
                    ?? throw new InvalidOperationException($"Block '{key}' has a null behavior slot.");
                if (!occupiedSlots.Add(slot))
                    throw new InvalidOperationException($"Block '{key}' declares duplicate behavior slot '{slot}'.");
                object? attached = slot switch
                {
                    "Ticker" => block.Ticker,
                    "Physics" => block.Physics,
                    "Lifecycle" => block.Lifecycle,
                    "Visuals" => block.Visuals,
                    "Interactable" => block.Interactable,
                    "Redstone" => block.Redstone,
                    _ => throw new InvalidOperationException($"Block '{key}' has unknown behavior slot '{slot}'.")
                };

                if (attached is null)
                    throw new InvalidOperationException($"Block '{key}' did not attach declared behavior slot '{slot}'.");
            }
        }
    }

    internal static ContentRuntimeBuilder CreateBuiltIns() =>
        CreateWithRuntimeView(BehaviorBuildContext.BuiltIns, bindRuntime: true);

    internal static ContentRuntimeBuilder CreateBuiltIns(BehaviorBuildContext context) =>
        CreateWithRuntimeView(context, bindRuntime: false);

    private static ContentRuntimeBuilder CreateWithRuntimeView(BehaviorBuildContext context, bool bindRuntime)
    {
        StagedBlockRuntimeView blocks = new();
        ContentRuntimeBuilder? builder = null;
        BehaviorBuildContext runtimeContext = bindRuntime
            ? context.WithContent(blocks, key => builder!.GetItem(key))
            : context;
        ItemBuildContext items = new(
            key => builder!.ResolveBlockReference(key),
            key => builder!._blockItems.First(entry => entry.Key == key).Item,
            key => builder!.GetItem(key),
            key => ToolMaterialRegistry.Get(key.Path),
            key => ArmorMaterialRegistry.Get(key.Path),
            key => MaterialRegistry.Get(key.Path),
            key => Textures.Atlases.Items.IndexOf(key),
            key => EntityRegistry.ByName(key.Path),
            key => DefaultRegistries.BlockEntityTypes.Get(key)?.Value
                   ?? throw new KeyNotFoundException($"Unknown block-entity type '{key}'."),
            key => throw new KeyNotFoundException($"Unknown recipe '{key}'."),
            key => throw new KeyNotFoundException($"Unknown interaction dependency '{key}'."),
            key => _ = EntityDefinitionRegistry.Get(key.Path));
        builder = new(
            new BlockBehaviorProviderRegistry(runtimeContext),
            BlockBuildContext.BuiltIns(runtimeContext, key => builder!.ResolveLootItemOrBlockId(key)),
            new ItemBehaviorProviderRegistry(),
            items,
            blocks);
        return builder;
    }
}
