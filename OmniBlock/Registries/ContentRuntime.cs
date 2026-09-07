using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Processes;

namespace OmniBlock.Registries;

/// <summary>
///     Immutable content snapshot published after bootstrap has constructed and validated every entry.
///     Worlds will eventually receive this object directly; <see cref="Current" /> is the transitional
///     process publication point while legacy static consumers are migrated.
/// </summary>
public sealed class ContentRuntime
{
    private static ContentRuntime? s_current;

    internal ContentRuntime(
        IEnumerable<(ResourceLocation Key, Block Block)> blocks,
        IEnumerable<(ResourceLocation Key, Item Item)> items,
        IEnumerable<(ResourceLocation Key, Item Item)> blockItems,
        IEnumerable<(ResourceLocation Key, int ProtocolId, EntityType Type)> entityTypes,
        IBlockBehaviorProviderRegistry blockBehaviorProviders,
        IItemBehaviorProviderRegistry itemBehaviorProviders,
        IProcessProviderRegistry processProviders,
        RuntimeProcessRegistry processes)
    {
        ArgumentNullException.ThrowIfNull(blockBehaviorProviders);
        var blockEntries = blocks.ToArray();
        var itemEntries = items.ToArray();
        var blockItemEntries = blockItems.ToArray();
        var entityEntries = entityTypes.ToArray();
        Blocks = new RuntimeBlockRegistry(blockEntries);
        Items = new RuntimeItemRegistry(itemEntries, blockItemEntries, Blocks);
        EntityTypes = new RuntimeEntityTypeRegistry(entityEntries);
        Manifest = new ContentCatalogManifest(Blocks.Keys.Select(key =>
                new KeyValuePair<ResourceLocation, int>(key, Blocks.Get(key).Id)),
            itemEntries.Select(entry => new KeyValuePair<ResourceLocation, int>(entry.Key, entry.Item.Id)),
            processes.ManifestEntries,
            entityEntries.Select(entry => new KeyValuePair<ResourceLocation, EntityCatalogEntry>(entry.Key,
                new EntityCatalogEntry(entry.Type.ConstructorProviderType ?? ResourceLocation.Parse("omniblock:player"),
                    entry.Type.Definition?.ComputeCanonicalHash() ?? "",
                    entry.ProtocolId,
                    entry.Type.Definition?.SpawnObjectId is > 0 ? entry.Type.Definition.SpawnObjectId : null,
                    entry.Type.Definition?.GlobalSpawnId is > 0 ? entry.Type.Definition.GlobalSpawnId : null))));
        BlockBehaviorProviders = blockBehaviorProviders;
        ItemBehaviorProviders = itemBehaviorProviders;
        ProcessProviders = processProviders;
        Processes = processes;
    }

    private ContentRuntime(ContentRuntime source, RuntimeProcessRegistry processes)
    {
        Blocks = source.Blocks;
        Items = source.Items;
        EntityTypes = source.EntityTypes;
        Manifest = new ContentCatalogManifest(source.Manifest.BlockIds, source.Manifest.ItemIds,
            processes.ManifestEntries, source.Manifest.Entities);
        BlockBehaviorProviders = source.BlockBehaviorProviders;
        ItemBehaviorProviders = source.ItemBehaviorProviders;
        ProcessProviders = source.ProcessProviders;
        Processes = processes;
    }

    public static ContentRuntime Current => Volatile.Read(ref s_current)
                                            ?? throw new InvalidOperationException("Content runtime has not been published. Run Bootstrap.Initialize() first.");

    internal static bool IsPublished => Volatile.Read(ref s_current) is not null;

    public RuntimeBlockRegistry Blocks { get; }
    public RuntimeItemRegistry Items { get; }
    public RuntimeEntityTypeRegistry EntityTypes { get; }
    public ContentCatalogManifest Manifest { get; }
    public IBlockBehaviorProviderRegistry BlockBehaviorProviders { get; }
    public IItemBehaviorProviderRegistry ItemBehaviorProviders { get; }
    public IProcessProviderRegistry ProcessProviders { get; }
    public RuntimeProcessRegistry Processes { get; }

    public ContentRuntime WithProcesses(IEnumerable<ProcessDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ProcessBuildContext context = new(Items, Blocks);
        var processes = RuntimeProcessRegistry.Compile(
            definitions, ProcessProviders, context);
        return new ContentRuntime(this, processes);
    }

    internal static bool TryGetCurrent(out ContentRuntime? runtime)
    {
        runtime = Volatile.Read(ref s_current);
        return runtime is not null;
    }

    internal static void Publish(ContentRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (Interlocked.CompareExchange(ref s_current, runtime, null) is not null)
        {
            throw new InvalidOperationException("A content runtime has already been published.");
        }
    }
}

/// <summary>
///     Immutable unified index over standalone and block-derived items. Standalone items win the
///     resource-key lookup when a legacy block and item share a name; both remain addressable by ID.
/// </summary>
public interface IItemRuntimeView
{
    Item Get(ResourceLocation key);
    Item GetByProtocolId(int protocolId);
    bool TryGet(ResourceLocation key, out Item? item);
    bool TryGetByProtocolId(int protocolId, out Item? item);

    bool TryParse(string input, [NotNullWhen(true)] out ItemStack? stack, int count = 1, int defaultMeta = 0)
    {
        stack = null;
        if (string.IsNullOrWhiteSpace(input)) return false;
        var name = input;
        var meta = defaultMeta;
        var separator = input.LastIndexOf(':');
        if (separator >= 0 && int.TryParse(input[(separator + 1)..], out var parsedMeta))
        {
            name = input[..separator];
            meta = parsedMeta;
        }

        Item? item;
        if (int.TryParse(name, out var protocolId)) TryGetByProtocolId(protocolId, out item);
        else if (ResourceLocation.TryParse(name, out var key)) TryGet(key, out item);
        else item = null;
        if (item is null) return false;
        stack = new ItemStack(item, count, meta);
        return true;
    }
}

public sealed class RuntimeItemRegistry : IItemRuntimeView
{
    private readonly FrozenDictionary<string, (Item Item, int Meta)> _aliases;
    private readonly FrozenDictionary<ResourceLocation, Item> _byKey;
    private readonly FrozenDictionary<int, Item> _byProtocolId;
    private readonly FrozenDictionary<int, string> _namesById;

    internal RuntimeItemRegistry(
        IEnumerable<(ResourceLocation Key, Item Item)> items,
        IEnumerable<(ResourceLocation Key, Item Item)> blockItems,
        RuntimeBlockRegistry blocks)
    {
        var byKey = new Dictionary<ResourceLocation, Item>();
        var byProtocolId = new Dictionary<int, Item>();
        var aliases = new Dictionary<string, (Item, int)>(StringComparer.OrdinalIgnoreCase);
        var namesById = new Dictionary<int, string>();
        Add(items, false);
        Add(blockItems, true);

        _byKey = byKey.ToFrozenDictionary();
        _byProtocolId = byProtocolId.ToFrozenDictionary();
        foreach (var (key, item) in byKey)
        {
            aliases.TryAdd(key.Path, (item, 0));
            aliases.TryAdd(key.ToString(), (item, 0));
            namesById.TryAdd(item.Id, key.Path);
            if (blocks.TryGetByProtocolId(item.Id, out var namedBlock) && namedBlock is not null)
            {
                aliases.TryAdd(key.Path.Replace("_", "", StringComparison.Ordinal), (item, 0));
                aliases.TryAdd($"{key.Namespace}:{key.Path.Replace("_", "", StringComparison.Ordinal)}", (item, 0));
            }

            AddAliases(item.GetItemAlias, key.Namespace, item);
            if (blocks.TryGetByProtocolId(item.Id, out var block) && block is not null)
                AddAliases(block.GetBlockAlias, key.Namespace, item);
        }

        _aliases = aliases.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _namesById = namesById.ToFrozenDictionary();

        void AddAliases(IEnumerable<string> values, Namespace itemNamespace, Item item)
        {
            foreach (var value in values)
            {
                var alias = value.ToLowerInvariant();
                var separator = alias.LastIndexOf(':');
                if (separator >= 0 && int.TryParse(alias[(separator + 1)..], out var meta))
                {
                    var name = alias[..separator];
                    aliases.TryAdd(name, (item, meta));
                    if (!name.Contains(':')) aliases.TryAdd($"{itemNamespace}:{name}", (item, meta));
                }
                else
                {
                    aliases.TryAdd(alias, (item, 0));
                    if (!alias.Contains(':')) aliases.TryAdd($"{itemNamespace}:{alias}", (item, 0));
                }
            }
        }

        void Add(IEnumerable<(ResourceLocation Key, Item Item)> entries, bool allowKeyCollision)
        {
            foreach (var (key, item) in entries)
            {
                if (!byKey.TryAdd(key, item) && !allowKeyCollision)
                    throw new ArgumentException($"Duplicate item key '{key}'.");
                if (item.Id is < 0 or >= 32000)
                    throw new ArgumentOutOfRangeException(nameof(entries), item.Id, "Item protocol id must be between 0 and 31999.");
                if (!byProtocolId.TryAdd(item.Id, item))
                    throw new ArgumentException($"Duplicate item protocol id {item.Id}.");
                if (!item.IsFrozen) throw new ArgumentException($"Item '{key}' was not finalized.");
            }
        }
    }

    public int Count => _byProtocolId.Count;
    public IEnumerable<ResourceLocation> Keys => _byKey.Keys;

    public Item Get(ResourceLocation key) => _byKey.TryGetValue(key, out var item)
        ? item
        : throw new KeyNotFoundException($"Unknown item '{key}'.");

    public Item GetByProtocolId(int protocolId) => _byProtocolId.TryGetValue(protocolId, out var item)
        ? item
        : throw new KeyNotFoundException($"Unknown item protocol id {protocolId}.");

    public bool TryGet(ResourceLocation key, out Item? item) => _byKey.TryGetValue(key, out item);
    public bool TryGetByProtocolId(int protocolId, out Item? item) => _byProtocolId.TryGetValue(protocolId, out item);

    public bool TryParse(string input, [NotNullWhen(true)] out ItemStack? stack, int count = 1, int defaultMeta = 0)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = input;
        var meta = defaultMeta;
        var separator = input.LastIndexOf(':');
        if (separator >= 0 && int.TryParse(input[(separator + 1)..], out var parsedMeta))
        {
            name = input[..separator];
            meta = parsedMeta;
        }

        Item? item = null;
        if (int.TryParse(name, out var protocolId)) _byProtocolId.TryGetValue(protocolId, out item);
        else if (_aliases.TryGetValue(name, out var alias))
        {
            item = alias.Item;
            if (meta == defaultMeta) meta = alias.Meta;
        }
        else if (ResourceLocation.TryParse(name, out var key)) _byKey.TryGetValue(key, out item);

        stack = item is null ? null : new ItemStack(item, count, meta);
        return stack is not null;
    }

    public string GetName(ItemStack stack) => _namesById.TryGetValue(stack.ItemId, out var name)
        ? name
        : stack.GetItemName();

    public IReadOnlyList<string> GetAvailableNames(string prefix = "") =>
        [.. _aliases.Keys.Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Order()];
}

/// <summary>Frozen key and protocol-ID indexes over the constructed block catalog.</summary>
public sealed class RuntimeBlockRegistry : IBlockRuntimeView
{
    public const int ProtocolIdCapacity = 256;
    private readonly FrozenDictionary<ResourceLocation, Block> _byKey;
    private readonly FrozenDictionary<int, Block> _byProtocolId;

    internal RuntimeBlockRegistry(IEnumerable<(ResourceLocation Key, Block Block)> entries)
    {
        var byKey = new Dictionary<ResourceLocation, Block>();
        var byProtocolId = new Dictionary<int, Block>();

        foreach (var (key, block) in entries)
        {
            if (!byKey.TryAdd(key, block))
            {
                throw new ArgumentException($"Duplicate block key '{key}'.", nameof(entries));
            }

            if (!byProtocolId.TryAdd(block.Id, block))
            {
                throw new ArgumentException(
                    $"Duplicate block protocol id {block.Id} for '{key}'.",
                    nameof(entries));
            }
        }

        _byKey = byKey.ToFrozenDictionary();
        _byProtocolId = byProtocolId.ToFrozenDictionary();
    }

    public int Count => _byKey.Count;
    public IEnumerable<ResourceLocation> Keys => _byKey.Keys;

    public Block Get(ResourceLocation key) =>
        _byKey.TryGetValue(key, out var block)
            ? block
            : throw new KeyNotFoundException($"Unknown block '{key}'.");

    public Block Get(string key) => Get(ResourceLocation.Parse(key));

    public Block GetByProtocolId(int protocolId) =>
        _byProtocolId.TryGetValue(protocolId, out var block)
            ? block
            : throw new KeyNotFoundException($"Unknown block protocol id {protocolId}.");

    public bool TryGetByProtocolId(int protocolId, out Block? block) => _byProtocolId.TryGetValue(protocolId, out block);

    public bool TryGet(ResourceLocation key, out Block? block) => _byKey.TryGetValue(key, out block);

    public bool IsOpaque(int protocolId) => TryGetByProtocolId(protocolId, out var block) && block.IsOpaque;

    public int GetLightEmission(int protocolId) =>
        TryGetByProtocolId(protocolId, out var block) ? block.LightEmission : 0;

    public bool AllowsVision(int protocolId) =>
        !TryGetByProtocolId(protocolId, out var block) || block.AllowsVision;
}
