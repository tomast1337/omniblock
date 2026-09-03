using System.Collections.Frozen;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using System.Diagnostics.CodeAnalysis;

namespace OmniBlock.Registries;

/// <summary>
/// Immutable content snapshot published after bootstrap has constructed and validated every entry.
/// Worlds will eventually receive this object directly; <see cref="Current" /> is the transitional
/// process publication point while legacy static consumers are migrated.
/// </summary>
public sealed class ContentRuntime
{
    private static ContentRuntime? s_current;

    internal ContentRuntime(
        IEnumerable<(ResourceLocation Key, Block Block)> blocks,
        IEnumerable<(ResourceLocation Key, Item Item)> items,
        IEnumerable<(ResourceLocation Key, Item Item)> blockItems,
        IBlockBehaviorProviderRegistry blockBehaviorProviders,
        IItemBehaviorProviderRegistry itemBehaviorProviders)
    {
        ArgumentNullException.ThrowIfNull(blockBehaviorProviders);
        Blocks = new RuntimeBlockRegistry(blocks);
        Items = new RuntimeItemRegistry(items, blockItems, Blocks);
        Manifest = new ContentCatalogManifest(Blocks.Keys.Select(key =>
            new KeyValuePair<ResourceLocation, int>(key, Blocks.Get(key).Id)));
        BlockBehaviorProviders = blockBehaviorProviders;
        ItemBehaviorProviders = itemBehaviorProviders;
    }

    public static ContentRuntime Current => Volatile.Read(ref s_current)
        ?? throw new InvalidOperationException("Content runtime has not been published. Run Bootstrap.Initialize() first.");

    internal static bool IsPublished => Volatile.Read(ref s_current) is not null;

    internal static bool TryGetCurrent(out ContentRuntime? runtime)
    {
        runtime = Volatile.Read(ref s_current);
        return runtime is not null;
    }

    public RuntimeBlockRegistry Blocks { get; }
    public RuntimeItemRegistry Items { get; }
    public ContentCatalogManifest Manifest { get; }
    public IBlockBehaviorProviderRegistry BlockBehaviorProviders { get; }
    public IItemBehaviorProviderRegistry ItemBehaviorProviders { get; }

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
/// Immutable unified index over standalone and block-derived items. Standalone items win the
/// resource-key lookup when a legacy block and item share a name; both remain addressable by ID.
/// </summary>
public interface IItemRuntimeView
{
    Item Get(ResourceLocation key);
    Item GetByProtocolId(int protocolId);
    bool TryGet(ResourceLocation key, out Item? item);
    bool TryGetByProtocolId(int protocolId, out Item? item);
}

public sealed class RuntimeItemRegistry : IItemRuntimeView
{
    private readonly FrozenDictionary<ResourceLocation, Item> _byKey;
    private readonly FrozenDictionary<int, Item> _byProtocolId;
    private readonly FrozenDictionary<string, (Item Item, int Meta)> _aliases;
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
        Add(items, allowKeyCollision: false);
        Add(blockItems, allowKeyCollision: true);

        _byKey = byKey.ToFrozenDictionary();
        _byProtocolId = byProtocolId.ToFrozenDictionary();
        foreach ((ResourceLocation key, Item item) in byKey)
        {
            aliases.TryAdd(key.Path, (item, 0));
            aliases.TryAdd(key.ToString(), (item, 0));
            namesById.TryAdd(item.Id, key.Path);
            if (blocks.TryGetByProtocolId(item.Id, out Block? namedBlock) && namedBlock is not null)
            {
                aliases.TryAdd(key.Path.Replace("_", "", StringComparison.Ordinal), (item, 0));
                aliases.TryAdd($"{key.Namespace}:{key.Path.Replace("_", "", StringComparison.Ordinal)}", (item, 0));
            }
            AddAliases(item.GetItemAlias, key.Namespace, item);
            if (blocks.TryGetByProtocolId(item.Id, out Block? block) && block is not null)
                AddAliases(block.GetBlockAlias, key.Namespace, item);
        }
        _aliases = aliases.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _namesById = namesById.ToFrozenDictionary();

        void AddAliases(IEnumerable<string> values, Namespace itemNamespace, Item item)
        {
            foreach (string value in values)
            {
                string alias = value.ToLowerInvariant();
                int separator = alias.LastIndexOf(':');
                if (separator >= 0 && int.TryParse(alias[(separator + 1)..], out int meta))
                {
                    string name = alias[..separator];
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
            foreach ((ResourceLocation key, Item item) in entries)
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
    public Item Get(ResourceLocation key) => _byKey.TryGetValue(key, out Item? item)
        ? item : throw new KeyNotFoundException($"Unknown item '{key}'.");
    public Item GetByProtocolId(int protocolId) => _byProtocolId.TryGetValue(protocolId, out Item? item)
        ? item : throw new KeyNotFoundException($"Unknown item protocol id {protocolId}.");
    public bool TryGet(ResourceLocation key, out Item? item) => _byKey.TryGetValue(key, out item);
    public bool TryGetByProtocolId(int protocolId, out Item? item) => _byProtocolId.TryGetValue(protocolId, out item);

    public bool TryParse(string input, [NotNullWhen(true)] out ItemStack? stack, int count = 1, int defaultMeta = 0)
    {
        ArgumentNullException.ThrowIfNull(input);
        string name = input;
        int meta = defaultMeta;
        int separator = input.LastIndexOf(':');
        if (separator >= 0 && int.TryParse(input[(separator + 1)..], out int parsedMeta))
        {
            name = input[..separator];
            meta = parsedMeta;
        }

        Item? item = null;
        if (int.TryParse(name, out int protocolId)) _byProtocolId.TryGetValue(protocolId, out item);
        else if (_aliases.TryGetValue(name, out (Item Item, int Meta) alias))
        {
            item = alias.Item;
            if (meta == defaultMeta) meta = alias.Meta;
        }
        else if (ResourceLocation.TryParse(name, out ResourceLocation? key)) _byKey.TryGetValue(key, out item);

        stack = item is null ? null : new ItemStack(item, count, meta);
        return stack is not null;
    }

    public string GetName(ItemStack stack) => _namesById.TryGetValue(stack.ItemId, out string? name)
        ? name : stack.GetItemName();

    public IReadOnlyList<string> GetAvailableNames(string prefix = "") =>
        [.. _aliases.Keys.Where(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Order()];
}

/// <summary>Frozen key and protocol-ID indexes over the constructed block catalog.</summary>
public sealed class RuntimeBlockRegistry : IBlockRuntimeView
{
    private readonly FrozenDictionary<ResourceLocation, Block> _byKey;
    private readonly FrozenDictionary<int, Block> _byProtocolId;

    internal RuntimeBlockRegistry(IEnumerable<(ResourceLocation Key, Block Block)> entries)
    {
        var byKey = new Dictionary<ResourceLocation, Block>();
        var byProtocolId = new Dictionary<int, Block>();

        foreach ((ResourceLocation key, Block block) in entries)
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
        _byKey.TryGetValue(key, out Block? block)
            ? block
            : throw new KeyNotFoundException($"Unknown block '{key}'.");

    public Block GetByProtocolId(int protocolId) =>
        _byProtocolId.TryGetValue(protocolId, out Block? block)
            ? block
            : throw new KeyNotFoundException($"Unknown block protocol id {protocolId}.");

    public bool TryGetByProtocolId(int protocolId, out Block? block)
    {
        return _byProtocolId.TryGetValue(protocolId, out block);
    }

    public bool TryGet(ResourceLocation key, out Block? block) => _byKey.TryGetValue(key, out block);
}
