using System.Collections.Frozen;
using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Items;

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
        IEnumerable<(ResourceLocation Key, Item Item)> blockItems,
        IBlockBehaviorProviderRegistry blockBehaviorProviders)
    {
        ArgumentNullException.ThrowIfNull(blockBehaviorProviders);
        Blocks = new RuntimeBlockRegistry(blocks);
        BlockItems = new RuntimeBlockItemRegistry(blockItems);
        BlockBehaviorProviders = blockBehaviorProviders;
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
    public RuntimeBlockItemRegistry BlockItems { get; }
    public IBlockBehaviorProviderRegistry BlockBehaviorProviders { get; }

    internal static void Publish(ContentRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (Interlocked.CompareExchange(ref s_current, runtime, null) is not null)
        {
            throw new InvalidOperationException("A content runtime has already been published.");
        }
    }
}

/// <summary>Immutable index of the item representations derived from runtime blocks.</summary>
public sealed class RuntimeBlockItemRegistry
{
    private readonly FrozenDictionary<ResourceLocation, Item> _byKey;
    private readonly Item?[] _byProtocolId = new Item?[BlockRegistry.ProtocolIdCapacity];

    internal RuntimeBlockItemRegistry(IEnumerable<(ResourceLocation Key, Item Item)> entries)
    {
        var byKey = new Dictionary<ResourceLocation, Item>();
        foreach ((ResourceLocation key, Item item) in entries)
        {
            if (!byKey.TryAdd(key, item)) throw new ArgumentException($"Duplicate block-item key '{key}'.");
            if (item.Id is < 0 or >= BlockRegistry.ProtocolIdCapacity)
                throw new ArgumentOutOfRangeException(nameof(entries), item.Id,
                    $"Block-item protocol id must be between 0 and {BlockRegistry.ProtocolIdCapacity - 1}.");
            if (_byProtocolId[item.Id] is not null) throw new ArgumentException($"Duplicate block-item protocol id {item.Id}.");
            _byProtocolId[item.Id] = item;
        }

        _byKey = byKey.ToFrozenDictionary();
    }

    public int Count => _byKey.Count;
    public Item Get(ResourceLocation key) => _byKey.TryGetValue(key, out Item? item)
        ? item
        : throw new KeyNotFoundException($"Unknown block item '{key}'.");
    public Item GetByProtocolId(int protocolId) =>
        protocolId is >= 0 and < BlockRegistry.ProtocolIdCapacity && _byProtocolId[protocolId] is { } item
            ? item
            : throw new KeyNotFoundException($"Unknown block-item protocol id {protocolId}.");
}

/// <summary>Frozen key and protocol-ID indexes over the constructed block catalog.</summary>
public sealed class RuntimeBlockRegistry : IBlockRuntimeView
{
    private readonly FrozenDictionary<ResourceLocation, Block> _byKey;
    private readonly Block?[] _byProtocolId;

    internal RuntimeBlockRegistry(IEnumerable<(ResourceLocation Key, Block Block)> entries)
    {
        var byKey = new Dictionary<ResourceLocation, Block>();
        _byProtocolId = new Block?[256];

        foreach ((ResourceLocation key, Block block) in entries)
        {
            if (!byKey.TryAdd(key, block))
            {
                throw new ArgumentException($"Duplicate block key '{key}'.", nameof(entries));
            }

            if (_byProtocolId[block.Id] is { } existing)
            {
                throw new ArgumentException(
                    $"Duplicate block protocol id {block.Id} for '{key}' and block {existing.Id}.",
                    nameof(entries));
            }

            _byProtocolId[block.Id] = block;
        }

        _byKey = byKey.ToFrozenDictionary();
    }

    public int Count => _byKey.Count;
    public IEnumerable<ResourceLocation> Keys => _byKey.Keys;

    public Block Get(ResourceLocation key) =>
        _byKey.TryGetValue(key, out Block? block)
            ? block
            : throw new KeyNotFoundException($"Unknown block '{key}'.");

    public Block GetByProtocolId(int protocolId) =>
        protocolId is >= 0 and < 256 && _byProtocolId[protocolId] is { } block
            ? block
            : throw new KeyNotFoundException($"Unknown block protocol id {protocolId}.");

    public bool TryGetByProtocolId(int protocolId, out Block? block)
    {
        block = protocolId is >= 0 and < 256 ? _byProtocolId[protocolId] : null;
        return block is not null;
    }

    public bool TryGet(ResourceLocation key, out Block? block) => _byKey.TryGetValue(key, out block);
}
