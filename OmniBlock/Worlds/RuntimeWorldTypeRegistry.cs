using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace OmniBlock.Worlds;

/// <summary>Immutable world-type catalog belonging to one content runtime.</summary>
public sealed class RuntimeWorldTypeRegistry
{
    private readonly FrozenDictionary<ResourceLocation, WorldType> _byKey;
    private readonly FrozenDictionary<string, WorldType> _byLegacyName;

    internal RuntimeWorldTypeRegistry(IEnumerable<WorldType> types)
    {
        var byKey = new Dictionary<ResourceLocation, WorldType>();
        var byLegacyName = new Dictionary<string, WorldType>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (!byKey.TryAdd(type.Key, type))
                throw new InvalidOperationException($"Duplicate world type '{type.Key}'.");
            if (!byLegacyName.TryAdd(type.Name, type))
                throw new InvalidOperationException($"Duplicate legacy world-type name '{type.Name}'.");
            byLegacyName.TryAdd(type.Key.ToString(), type);
        }

        _byKey = byKey.ToFrozenDictionary();
        _byLegacyName = byLegacyName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        All = Array.AsReadOnly(
            byKey.Values.OrderBy(static type => type.Key.ToString(), StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<WorldType> All { get; }

    public WorldType Get(ResourceLocation key) => _byKey.TryGetValue(key, out var type)
        ? type
        : throw new KeyNotFoundException($"Unknown world type '{key}'.");

    public WorldType Get(string key) => TryGet(key, out var type)
        ? type
        : throw new KeyNotFoundException($"Unknown world type '{key}'.");

    public bool TryGet(ResourceLocation key, [NotNullWhen(true)] out WorldType? type) =>
        _byKey.TryGetValue(key, out type);

    public bool TryGet(string key, [NotNullWhen(true)] out WorldType? type) =>
        _byLegacyName.TryGetValue(key, out type);
}
