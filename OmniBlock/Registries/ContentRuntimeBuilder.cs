using System.Text.Json;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Entities.State;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Processes;
using OmniBlock.Textures;
using OmniBlock.Util;
using OmniBlock.Worlds;
using OmniBlock.Worlds.Generation;
using OmniBlock.Worlds.Generation.Biomes;

namespace OmniBlock.Registries;

/// <summary>
///     Owns the mutable provider set and dependency context used while compiling content definitions.
///     A builder is confined to bootstrap; runtime blocks retain only the immutable behaviors it
///     creates. Future native and Luau providers register with this owner during the Registry phase.
/// </summary>
public sealed class ContentRuntimeBuilder : IItemRuntimeView, IEntityTypeBuildView
{
    private readonly List<(ResourceLocation Key, Item Item)> _blockItems = [];
    private readonly StagedBlockRuntimeView _blockRuntimeView;
    private readonly List<(ResourceLocation Key, BlockDefinition Definition, Block Block)> _blocks = [];
    private readonly Dictionary<ResourceLocation, Block> _blocksByKey = [];
    private readonly Dictionary<int, Block> _blocksByProtocolId = [];
    private readonly List<(ResourceLocation Key, EntityDefinition? Definition, EntityType Type)> _entityTypes = [];
    private readonly Dictionary<ResourceLocation, EntityType> _entityTypesByKey = [];
    private readonly Dictionary<int, EntityType> _entityTypesByProtocolId = [];
    private readonly List<(ResourceLocation Key, ItemDefinition Definition, Item Item)> _items = [];
    private readonly Dictionary<ResourceLocation, Item> _itemsByKey = [];
    private readonly Dictionary<int, Item> _itemsByProtocolId = [];
    private readonly List<BiomeGenerationDefinition> _pendingBiomeGenerationDefinitions = [];
    private readonly List<BlockDefinition> _pendingBlockDefinitions = [];
    private readonly List<DimensionGeneratorProfileDefinition> _pendingDimensionGeneratorProfiles = [];
    private readonly List<EntityDefinition> _pendingEntityDefinitions = [];
    private readonly List<ItemDefinition> _pendingItemDefinitions = [];
    private readonly List<ProcessDefinition> _pendingProcessDefinitions = [];
    private readonly List<WorldTypeDefinition> _pendingWorldTypeDefinitions = [];
    private bool _blocksBuilt;
    private bool _built;
    private bool _itemDraftsCreated;
    private bool _itemsFinalized;

    public ContentRuntimeBuilder(
        IBlockBehaviorProviderRegistry blockBehaviorProviders,
        BlockBuildContext blockBuildContext,
        IItemBehaviorProviderRegistry itemBehaviorProviders,
        ItemBuildContext itemBuildContext,
        StagedBlockRuntimeView? blockRuntimeView = null,
        IProcessProviderRegistry? processProviders = null,
        IEntityBehaviorProviderRegistry? entityBehaviorProviders = null,
        IEntityConstructorProviderRegistry? entityConstructorProviders = null,
        IWorldGeneratorProviderRegistry? worldGeneratorProviders = null)
    {
        ArgumentNullException.ThrowIfNull(blockBehaviorProviders);
        BlockBehaviorProviders = blockBehaviorProviders;
        ArgumentNullException.ThrowIfNull(itemBehaviorProviders);
        ItemBehaviorProviders = itemBehaviorProviders;
        ProcessProviders = processProviders ?? BuiltInProcessProviders.CreateRegistry();
        EntityBehaviorProviders = entityBehaviorProviders ?? new EntityBehaviorProviderRegistry();
        EntityConstructorProviders = entityConstructorProviders ?? new EntityConstructorProviderRegistry();
        WorldGeneratorProviders = worldGeneratorProviders ?? BuiltInWorldGeneratorProviders.CreateRegistry();
        _blockRuntimeView = blockRuntimeView ?? new StagedBlockRuntimeView();
        BlockBuildContext = blockBuildContext;
        ItemBuildContext = itemBuildContext;
    }

    public IBlockBehaviorProviderRegistry BlockBehaviorProviders { get; }
    public BlockBuildContext BlockBuildContext { get; }
    public IItemBehaviorProviderRegistry ItemBehaviorProviders { get; }
    public IProcessProviderRegistry ProcessProviders { get; }
    public IEntityBehaviorProviderRegistry EntityBehaviorProviders { get; }
    public IEntityConstructorProviderRegistry EntityConstructorProviders { get; }
    public IWorldGeneratorProviderRegistry WorldGeneratorProviders { get; }
    public ItemBuildContext ItemBuildContext { get; }
    public BehaviorBuildContext BehaviorBuildContext => BlockBuildContext.Behaviors;
    internal IBlockRuntimeView StagedBlocks => _blockRuntimeView;

    EntityType IEntityTypeBuildView.Get(ResourceLocation key) => GetEntityType(key);

    bool IEntityTypeBuildView.TryGet(ResourceLocation key, out EntityType? type) =>
        _entityTypesByKey.TryGetValue(key, out type);

    public Item Get(ResourceLocation key) => _itemsByKey.TryGetValue(key, out var item)
        ? item
        : throw new KeyNotFoundException($"Unknown item '{key}'.");

    public Item GetByProtocolId(int protocolId) => _itemsByProtocolId.TryGetValue(protocolId, out var item)
        ? item
        : throw new KeyNotFoundException($"Unknown item protocol id {protocolId}.");

    public bool TryGet(ResourceLocation key, out Item? item) => _itemsByKey.TryGetValue(key, out item);
    public bool TryGetByProtocolId(int protocolId, out Item? item) => _itemsByProtocolId.TryGetValue(protocolId, out item);

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

    internal void AddProcessDefinition(ProcessDefinition definition)
    {
        if (_built) throw new InvalidOperationException("Cannot add content after the runtime has been built.");
        ArgumentNullException.ThrowIfNull(definition);
        _pendingProcessDefinitions.Add(definition);
    }

    internal void AddEntityDefinition(EntityDefinition definition)
    {
        if (_built) throw new InvalidOperationException("Cannot add content after the runtime has been built.");
        ArgumentNullException.ThrowIfNull(definition);
        _pendingEntityDefinitions.Add(definition);
    }

    internal void AddWorldTypeDefinition(WorldTypeDefinition definition)
    {
        if (_built) throw new InvalidOperationException("Cannot add content after the runtime has been built.");
        ArgumentNullException.ThrowIfNull(definition);
        _pendingWorldTypeDefinitions.Add(definition);
    }

    internal void AddDimensionGeneratorProfile(DimensionGeneratorProfileDefinition definition)
    {
        if (_built) throw new InvalidOperationException("Cannot add content after the runtime has been built.");
        ArgumentNullException.ThrowIfNull(definition);
        _pendingDimensionGeneratorProfiles.Add(definition);
    }

    internal void AddBiomeGenerationDefinition(BiomeGenerationDefinition definition)
    {
        if (_built) throw new InvalidOperationException("Cannot add content after the runtime has been built.");
        ArgumentNullException.ThrowIfNull(definition);
        _pendingBiomeGenerationDefinitions.Add(definition);
    }

    internal bool ContainsEntityDefinition(ResourceLocation key) =>
        _pendingEntityDefinitions.Any(definition =>
            new ResourceLocation(definition.Namespace, definition.Name) == key);

    internal EntityType GetEntityType(ResourceLocation key) =>
        _entityTypesByKey.TryGetValue(key, out var type)
            ? type
            : throw new KeyNotFoundException($"Unknown entity type '{key}'.");

    internal Item GetItem(ResourceLocation key) => Get(key);
    internal Item GetItemByProtocolId(int protocolId) => GetByProtocolId(protocolId);

    internal void BuildItemsForBootstrap() => CreatePendingItemDrafts();

    internal void FinalizeItemsForBootstrap() => BuildPendingItems();

    internal void BuildBlocksForBootstrap() => BuildPendingBlockDefinitions();

    internal void AddBlock(BlockDefinition definition, Block block)
    {
        if (_built) throw new InvalidOperationException("Cannot add content after the runtime has been built.");
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(block);
        if (definition.ProtocolId is < 0 or >= RuntimeBlockRegistry.ProtocolIdCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition), definition.ProtocolId,
                $"Block protocol id must be between 0 and {RuntimeBlockRegistry.ProtocolIdCapacity - 1}.");
        }

        ResourceLocation key = new(definition.Namespace, definition.Name);
        _blocksByKey.TryAdd(key, block);
        _blocksByProtocolId.TryAdd(definition.ProtocolId, block);
        _blockRuntimeView.Add(key, block);
        _blocks.Add((key, definition, block));
    }

    internal Block GetBlock(ResourceLocation key) =>
        _blocksByKey.TryGetValue(key, out var block)
            ? block
            : throw new KeyNotFoundException($"Unknown block '{key}'.");

    private Block ResolveBlockReference(ResourceLocation key)
    {
        if (_blocksByKey.TryGetValue(key, out var exact)) return exact;
        foreach (var (candidateKey, definition, block) in _blocks)
        {
            if (candidateKey.Namespace == key.Namespace && definition.TranslationKey == key.Path) return block;
        }

        throw new KeyNotFoundException($"Unknown block '{key}'.");
    }

    private int ResolveLootItemOrBlockId(ResourceLocation key)
    {
        if (_itemsByKey.TryGetValue(key, out var item)) return item.Id;
        foreach (var (_, definition, block) in _blocks)
        {
            if (definition.BlockItem.Aliases.Contains(key.Path, StringComparer.OrdinalIgnoreCase))
                return block.Id;
        }

        return ResolveBlockReference(key).Id;
    }

    internal Block GetBlockByProtocolId(int protocolId) =>
        _blocksByProtocolId.TryGetValue(protocolId, out var block)
            ? block
            : throw new KeyNotFoundException($"Unknown block protocol id {protocolId}.");

    internal bool TryGetBlockByProtocolId(int protocolId, out Block? block) => _blocksByProtocolId.TryGetValue(protocolId, out block);

    internal void BuildBlockItems(IEnumerable<BlockDefinition> definitions)
    {
        if (_built) throw new InvalidOperationException("Cannot add content after the runtime has been built.");
        if (_blockItems.Count != 0) throw new InvalidOperationException("Block items have already been built.");

        var staged = new List<(ResourceLocation Key, Block Block, Item Item)>();
        var keys = new HashSet<ResourceLocation>();
        var protocolIds = new HashSet<int>();
        foreach (var definition in definitions)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            if (!keys.Add(key)) throw new InvalidOperationException($"Duplicate block-item key '{key}'.");
            if (!protocolIds.Add(definition.ProtocolId))
                throw new InvalidOperationException($"Duplicate block-item protocol id {definition.ProtocolId}.");
            var block = GetBlock(key);
            var item = BlockItemFactory.Create(definition, block);
            if (item.Id != block.Id)
                throw new InvalidOperationException($"Block item '{key}' has id {item.Id}, expected {block.Id}.");
            if (_itemsByProtocolId.ContainsKey(item.Id))
                throw new InvalidOperationException($"Block item '{key}' collides with item protocol id {item.Id}.");
            staged.Add((key, block, item));
        }

        foreach (var (key, block, item) in staged)
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
        BuildPendingEntityDefinitions();
        ValidateBlocks();
        foreach (var (_, _, block) in _blocks) block.Freeze();
        foreach (var (_, _, item) in _items) item.Freeze();
        foreach (var (_, item) in _blockItems) item.Freeze();
        _blockRuntimeView.Freeze();
        var processes = BuildProcesses();
        var biomeGeneration = BuildBiomeGeneration();
        var worldTypes = BuildWorldTypes(biomeGeneration);
        var dimensionGeneratorProfiles = BuildDimensionGeneratorProfiles(biomeGeneration);
        ContentRuntime runtime = new(
            _blocks.Select(static entry => (entry.Key, entry.Block)),
            _items.Select(static entry => (entry.Key, entry.Item)),
            _blockItems,
            _entityTypes.Select(static entry => (
                entry.Key, entry.Definition?.ProtocolId ?? 100, entry.Type)),
            BlockBehaviorProviders,
            ItemBehaviorProviders,
            ProcessProviders,
            processes,
            biomeGeneration,
            worldTypes,
            dimensionGeneratorProfiles,
            WorldGeneratorProviders);
        _built = true;
        return runtime;
    }

    private IReadOnlyList<WorldType> BuildWorldTypes(RuntimeBiomeGenerationRegistry biomeGeneration)
    {
        var types = new List<WorldType>(_pendingWorldTypeDefinitions.Count);
        var keys = new HashSet<ResourceLocation>();
        var legacyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in _pendingWorldTypeDefinitions)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            if (!keys.Add(key))
                throw new InvalidOperationException($"Duplicate world type '{key}'.");
            if (!legacyNames.Add(key.Path))
                throw new InvalidOperationException($"Duplicate legacy world-type name '{key.Path}'.");
            if (string.IsNullOrWhiteSpace(definition.Generator))
                throw new InvalidOperationException($"World type '{key}': generator provider is required.");

            var providerType = ResourceLocation.Parse(definition.Generator);
            if (!WorldGeneratorProviders.Contains(providerType))
            {
                throw new InvalidOperationException(
                    $"World type '{key}': unknown generator provider '{providerType}'.");
            }

            var compiledGenerator = WorldGeneratorProviders.Compile(
                providerType,
                key,
                definition.GeneratorSettings,
                new WorldGeneratorCompileContext(_blockRuntimeView, biomeGeneration));
            types.Add(new WorldType(
                key,
                providerType,
                definition.IconPath,
                definition.CanBeCreated,
                compiledGenerator));
        }

        return types;
    }

    private RuntimeBiomeGenerationRegistry BuildBiomeGeneration()
    {
        var settings = new List<KeyValuePair<ResourceLocation, BiomeGenerationSettings>>();
        var keys = new HashSet<ResourceLocation>();
        foreach (var definition in _pendingBiomeGenerationDefinitions)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            if (!keys.Add(key))
                throw new InvalidOperationException($"Duplicate biome generation definition '{key}'.");
            if (definition.FernSelectionBound < 0)
            {
                throw new InvalidOperationException(
                    $"Biome generation definition '{key}' has invalid FernSelectionBound {definition.FernSelectionBound}.");
            }

            settings.Add(new KeyValuePair<ResourceLocation, BiomeGenerationSettings>(
                key,
                new BiomeGenerationSettings(definition.FernSelectionBound)));
        }

        return new RuntimeBiomeGenerationRegistry(settings);
    }

    private IReadOnlyList<DimensionGeneratorProfile> BuildDimensionGeneratorProfiles(
        RuntimeBiomeGenerationRegistry biomeGeneration)
    {
        var profiles = new List<DimensionGeneratorProfile>(_pendingDimensionGeneratorProfiles.Count);
        var keys = new HashSet<ResourceLocation>();
        var dimensionIds = new HashSet<int>();
        foreach (var definition in _pendingDimensionGeneratorProfiles)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            if (!keys.Add(key))
                throw new InvalidOperationException($"Duplicate dimension generator profile '{key}'.");
            if (!dimensionIds.Add(definition.DimensionId))
            {
                throw new InvalidOperationException(
                    $"Duplicate dimension generator profile id {definition.DimensionId} for '{key}'.");
            }

            if (string.IsNullOrWhiteSpace(definition.Generator))
            {
                throw new InvalidOperationException(
                    $"Dimension generator profile '{key}': generator provider is required.");
            }

            var providerType = ResourceLocation.Parse(definition.Generator);
            if (!WorldGeneratorProviders.Contains(providerType))
            {
                throw new InvalidOperationException(
                    $"Dimension generator profile '{key}': unknown generator provider '{providerType}'.");
            }

            var compiledGenerator = WorldGeneratorProviders.Compile(
                providerType,
                key,
                definition.GeneratorSettings,
                new WorldGeneratorCompileContext(_blockRuntimeView, biomeGeneration));
            profiles.Add(new DimensionGeneratorProfile(
                key,
                definition.DimensionId,
                providerType,
                compiledGenerator));
        }

        return profiles;
    }

    internal void BuildEntitiesForBootstrap() => BuildPendingEntityDefinitions();

    private void BuildPendingEntityDefinitions()
    {
        if (_entityTypes.Count != 0) return;

        var definitionsByKey = new Dictionary<ResourceLocation, EntityDefinition>();
        var protocolIds = new HashSet<int>();
        var spawnObjectIds = new HashSet<int>();
        var globalSpawnIds = new HashSet<int>();
        foreach (var definition in _pendingEntityDefinitions)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            if (!definitionsByKey.TryAdd(key, definition))
                throw new InvalidOperationException($"Duplicate entity key '{key}'.");
            if (definition.ProtocolId is < 1 or > sbyte.MaxValue)
                throw new InvalidOperationException($"Entity '{key}' has invalid protocol id {definition.ProtocolId}.");
            if (!protocolIds.Add(definition.ProtocolId))
                throw new InvalidOperationException($"Duplicate entity protocol id {definition.ProtocolId} for '{key}'.");
            if (definition.SpawnObjectId != 0 && !spawnObjectIds.Add(definition.SpawnObjectId))
                throw new InvalidOperationException($"Duplicate entity object-spawn id {definition.SpawnObjectId} for '{key}'.");
            if (definition.GlobalSpawnId != 0 && !globalSpawnIds.Add(definition.GlobalSpawnId))
                throw new InvalidOperationException($"Duplicate global entity-spawn id {definition.GlobalSpawnId} for '{key}'.");
        }

        // Establish every key before behavior construction so forward entity references can resolve.
        foreach (var (key, definition) in definitionsByKey)
        {
            var (constructorType, constructor) = ConstructorFor(key, definition);
            EntityType draft = new(constructor.Create, constructor.RuntimeType, DisplayName(key), definition,
                constructorProviderType: constructorType);
            _entityTypesByKey.Add(key, draft);
            _entityTypesByProtocolId.Add(definition.ProtocolId, draft);
        }

        EntityBuildContext context = new(_blockRuntimeView, this, this);
        foreach (var (key, definition) in definitionsByKey)
        {
            var draft = _entityTypesByKey[key];
            try
            {
                ValidateEntityDefinitionReferences(key, definition);
                var behaviors = EntityFactory.BuildBehaviors(
                    definition, draft.BaseType, context, EntityBehaviorProviders);
                var renderDescriptor = definition.Renderer is { } renderer
                    ? EntityRenderDescriptor.Compile(renderer)
                    : null;
                var (constructorType, constructor) = ConstructorFor(key, definition);
                EntityType finalized = new(
                    constructor.Create, draft.BaseType, draft.Id, definition, behaviors, renderDescriptor,
                    constructorType);
                _entityTypesByKey[key] = finalized;
                _entityTypesByProtocolId[definition.ProtocolId] = finalized;
                _entityTypes.Add((key, definition, finalized));
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Entity '{key}' failed construction: {error.Message}", error);
            }
        }

        var playerKey = ResourceLocation.Parse("omniblock:player");
        EntityType player = new(
            static (_, _) => throw new NotSupportedException("Players must be created via ServerPlayerEntity constructor"),
            typeof(ServerPlayerEntity), "Player",
            constructorProviderType: ResourceLocation.Parse("omniblock:player"));
        _entityTypesByKey.Add(playerKey, player);
        _entityTypesByProtocolId.Add(100, player);
        _entityTypes.Add((playerKey, null, player));
    }

    private void ValidateEntityDefinitionReferences(ResourceLocation key, EntityDefinition definition)
    {
        if (definition.HeldItem is { } heldItem)
            _ = Get(ResourceLocation.Parse(heldItem));

        DataSynchronizer synchronizer = new(this);
        synchronizer.MakeProperty<byte>(0, 0);
        SyncedPropertyFactory.Declare(synchronizer, definition.SyncedProperties, key.ToString());
    }

    private (ResourceLocation Type, IEntityConstructorProvider Provider) ConstructorFor(
        ResourceLocation key, EntityDefinition definition)
    {
        var providerType = definition.Constructor is { Length: > 0 } declared
            ? ResourceLocation.Parse(declared)
            : key.Path switch
            {
                "slime" or "ghast" or "squid" => ResourceLocation.Parse("omniblock:living"),
                "creeper" or "skeleton" or "spider" or "giant" or "zombie" or "pigzombie" or
                    "pig" or "sheep" or "cow" or "chicken" or "wolf" => ResourceLocation.Parse("omniblock:creature"),
                _ => ResourceLocation.Parse("omniblock:object")
            };
        try
        {
            return (providerType, EntityConstructorProviders.Get(providerType));
        }
        catch (KeyNotFoundException error)
        {
            throw new InvalidOperationException(
                $"Entity '{key}' references unknown constructor provider '{providerType}'.", error);
        }
    }

    private static string DisplayName(ResourceLocation key) => key.Path switch
    {
        "primedtnt" => "PrimedTnt",
        "fallingsand" => "FallingSand",
        "fishhook" => "FishHook",
        "lightningbolt" => "LightningBolt",
        "pigzombie" => "PigZombie",
        _ => char.ToUpperInvariant(key.Path[0]) + key.Path[1..]
    };

    private RuntimeProcessRegistry BuildProcesses()
    {
        RuntimeBlockRegistry blocks = new(_blocks.Select(static entry => (entry.Key, entry.Block)));
        RuntimeItemRegistry items = new(
            _items.Select(static entry => (entry.Key, entry.Item)), _blockItems, blocks);
        ProcessBuildContext context = new(items, blocks);
        return RuntimeProcessRegistry.Compile(_pendingProcessDefinitions, ProcessProviders, context);
    }

    private void BuildPendingItems()
    {
        if (_pendingItemDefinitions.Count == 0 || _itemsFinalized) return;

        CreatePendingItemDrafts();

        foreach (var (key, definition, item) in _items)
        {
            try
            {
                ItemFactory.AttachBehavior(item, definition, ItemBuildContext, ItemBehaviorProviders);
                if (definition.CraftingReturnItemProtocolId is { } returnId)
                    item.SetCraftingReturnItem(GetItemByProtocolId(returnId));
                item.SetRepairIngredients([
                    .. definition.RepairIngredients.Select(reference =>
                        ItemBuildContext.ResolveItem(ResourceLocation.Parse(reference)))
                ]);
                if (item.BehaviorCount != definition.Behaviors.Length)
                {
                    throw new InvalidOperationException(
                        $"built {item.BehaviorCount} of {definition.Behaviors.Length} declared behaviors");
                }
            }
            catch (Exception error)
            {
                throw new InvalidOperationException($"Item '{key}' failed reference validation: {error.Message}", error);
            }
        }

        foreach (var (_, _, item) in _items) item.Freeze();
        _itemsFinalized = true;
    }

    private void CreatePendingItemDrafts()
    {
        if (_itemDraftsCreated) return;

        var assignedDefinitions = ContentIdAllocator.AssignItemIds(_pendingItemDefinitions);
        _pendingItemDefinitions.Clear();
        _pendingItemDefinitions.AddRange(assignedDefinitions);

        foreach (var definition in _pendingItemDefinitions)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            if (definition.ProtocolId is < 256 or >= 32000)
                throw new InvalidOperationException($"Item '{key}' has invalid protocol id {definition.ProtocolId}.");
            if (_itemsByKey.ContainsKey(key)) throw new InvalidOperationException($"Duplicate item key '{key}'.");
            if (_itemsByProtocolId.ContainsKey(definition.ProtocolId))
                throw new InvalidOperationException($"Duplicate item protocol id {definition.ProtocolId} for '{key}'.");
            try
            {
                var item = ItemFactory.CreateDraft(definition, ItemBuildContext);
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
        if (_blocksBuilt || _pendingBlockDefinitions.Count == 0) return;
        var definitions = ContentIdAllocator.AssignBlockIds(_pendingBlockDefinitions);
        foreach (var definition in definitions)
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

        foreach (var definition in definitions)
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

        BuildBlockItems(definitions);
        _blocksBuilt = true;
    }

    private void ValidateBlocks()
    {
        HashSet<ResourceLocation> keys = [];
        HashSet<int> protocolIds = [];
        foreach (var (key, definition, block) in _blocks)
        {
            if (!keys.Add(key)) throw new InvalidOperationException($"Duplicate block key '{key}'.");
            if (!protocolIds.Add(definition.ProtocolId))
                throw new InvalidOperationException($"Duplicate block protocol id {definition.ProtocolId}.");
            if (block.Id != definition.ProtocolId)
            {
                throw new InvalidOperationException(
                    $"Block '{key}' was constructed with id {block.Id}, expected {definition.ProtocolId}.");
            }

            ValidateSlots(key, definition, block);
        }
    }

    private static void ValidateSlots(ResourceLocation key, BlockDefinition definition, Block block)
    {
        HashSet<string> occupiedSlots = [];
        foreach (var behavior in definition.Behaviors)
        {
            foreach (var slotElement in behavior.GetProperty("Slots").EnumerateArray())
            {
                var slot = slotElement.GetString()
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
        CreateWithRuntimeView(BehaviorBuildContext.BuiltIns, true);

    internal static ContentRuntimeBuilder CreateBuiltIns(BehaviorBuildContext context) =>
        CreateWithRuntimeView(context, false);

    private static ContentRuntimeBuilder CreateWithRuntimeView(BehaviorBuildContext context, bool bindRuntime)
    {
        StagedBlockRuntimeView blocks = new();
        ContentRuntimeBuilder? builder = null;
        var runtimeContext = bindRuntime
            ? context.WithContent(blocks, key => builder!.GetItem(key))
            : context;
        ItemBuildContext items = new(
            key => builder!.ResolveBlockReference(key),
            key => builder!._blockItems.First(entry => entry.Key == key).Item,
            key => builder!.GetItem(key),
            key => ToolMaterialRegistry.Get(key.Path),
            key => ArmorMaterialRegistry.Get(key.Path),
            key => MaterialRegistry.Get(key.Path),
            key => Atlases.Items.IndexOf(key),
            key => builder!.GetEntityType(key),
            key => DefaultRegistries.BlockEntityTypes.Get(key)?.Value
                   ?? throw new KeyNotFoundException($"Unknown block-entity type '{key}'."),
            key => throw new KeyNotFoundException($"Unknown recipe '{key}'."),
            key => throw new KeyNotFoundException($"Unknown interaction dependency '{key}'."),
            key =>
            {
                if (!builder!.ContainsEntityDefinition(key) && key != ResourceLocation.Parse("omniblock:player"))
                    throw new KeyNotFoundException($"Unknown entity type '{key}'.");
            });
        builder = new ContentRuntimeBuilder(
            new BlockBehaviorProviderRegistry(runtimeContext),
            BlockBuildContext.BuiltIns(runtimeContext, key => builder!.ResolveLootItemOrBlockId(key)),
            new ItemBehaviorProviderRegistry(),
            items,
            blocks);
        return builder;
    }
}
