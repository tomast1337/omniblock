using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using OmniBlock.Blocks;

namespace OmniBlock.Registries;

/// <summary>A persistable name-to-ID contract for saves and client/server negotiation.</summary>
public sealed class ContentCatalogManifest
{
    private readonly FrozenDictionary<ResourceLocation, int> _blockIds;

    public ContentCatalogManifest(IEnumerable<KeyValuePair<ResourceLocation, int>> blockIds)
    {
        var ids = new Dictionary<ResourceLocation, int>();
        var namesById = new Dictionary<int, ResourceLocation>();
        foreach ((ResourceLocation key, int id) in blockIds)
        {
            if (!ids.TryAdd(key, id)) throw new ArgumentException($"Duplicate catalog name '{key}'.", nameof(blockIds));
            if (namesById.TryGetValue(id, out ResourceLocation existing))
                throw new ArgumentException($"Catalog ID {id} is assigned to both '{existing}' and '{key}'.", nameof(blockIds));
            namesById.Add(id, key);
        }

        _blockIds = ids.ToFrozenDictionary();
        Fingerprint = ComputeFingerprint(ids);
    }

    public IReadOnlyDictionary<ResourceLocation, int> BlockIds => _blockIds;
    public string Fingerprint { get; }

    public CatalogCompatibility CompareTo(ContentCatalogManifest required)
    {
        List<ResourceLocation> missing = [];
        List<ResourceLocation> added = [];
        List<CatalogIdMismatch> mismatched = [];
        foreach ((ResourceLocation key, int requiredId) in required._blockIds)
        {
            if (!_blockIds.TryGetValue(key, out int actualId)) missing.Add(key);
            else if (actualId != requiredId) mismatched.Add(new(key, requiredId, actualId));
        }
        foreach (ResourceLocation key in _blockIds.Keys)
        {
            if (!required._blockIds.ContainsKey(key)) added.Add(key);
        }

        return new(Fingerprint == required.Fingerprint, missing, added, mismatched);
    }

    private static string ComputeFingerprint(Dictionary<ResourceLocation, int> entries)
    {
        StringBuilder canonical = new();
        foreach ((ResourceLocation key, int id) in entries.OrderBy(static pair => pair.Key.ToString(), StringComparer.Ordinal))
            canonical.Append(key).Append('=').Append(id).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}

public sealed record CatalogIdMismatch(ResourceLocation Key, int RequiredId, int ActualId);

public sealed record CatalogCompatibility(
    bool IsExactMatch,
    IReadOnlyList<ResourceLocation> MissingEntries,
    IReadOnlyList<ResourceLocation> AdditionalEntries,
    IReadOnlyList<CatalogIdMismatch> IdMismatches)
{
    /// <summary>Additional content is safe for a save; missing or remapped content is not.</summary>
    public bool CanLoadWorld => MissingEntries.Count == 0 && IdMismatches.Count == 0;
    /// <summary>Network peers require precisely the same numeric catalog.</summary>
    public bool CanSynchronizeClient => IsExactMatch;
}

public static class ContentIdAllocator
{
    public static List<BlockDefinition> AssignBlockIds(
        IEnumerable<BlockDefinition> definitions,
        ContentCatalogManifest? previous = null)
    {
        List<BlockDefinition> source = definitions.ToList();
        var keys = new HashSet<ResourceLocation>();
        var used = new Dictionary<int, ResourceLocation>();
        foreach (BlockDefinition definition in source)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            if (!keys.Add(key)) throw new InvalidOperationException($"Duplicate block name '{key}'.");
            if (definition.ProtocolId < 0) continue;
            if (definition.ProtocolId >= BlockRegistry.ProtocolIdCapacity)
                throw new InvalidOperationException($"Block '{key}' protocol ID {definition.ProtocolId} exceeds the current save/wire limit.");
            if (used.TryGetValue(definition.ProtocolId, out ResourceLocation collision))
                throw new InvalidOperationException($"Blocks '{collision}' and '{key}' both request protocol ID {definition.ProtocolId}.");
            used.Add(definition.ProtocolId, key);
        }

        var assigned = new Dictionary<ResourceLocation, int>();
        foreach (BlockDefinition definition in source.Where(static definition => definition.ProtocolId < 0)
                     .OrderBy(static definition => $"{definition.Namespace}:{definition.Name}", StringComparer.Ordinal))
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            int id = -1;
            if (previous is not null && previous.BlockIds.TryGetValue(key, out int savedId)
                                     && savedId >= 0 && savedId < BlockRegistry.ProtocolIdCapacity
                                     && !used.ContainsKey(savedId))
                id = savedId;
            if (id < 0)
                for (int candidate = 0; candidate < BlockRegistry.ProtocolIdCapacity; candidate++)
                    if (!used.ContainsKey(candidate)) { id = candidate; break; }
            if (id < 0) throw new InvalidOperationException($"No protocol IDs remain for block '{key}'.");
            used.Add(id, key);
            assigned.Add(key, id);
        }

        return source.Select(definition => definition.ProtocolId >= 0
                ? definition
                : definition with { ProtocolId = assigned[new(definition.Namespace, definition.Name)] })
            .ToList();
    }
}
