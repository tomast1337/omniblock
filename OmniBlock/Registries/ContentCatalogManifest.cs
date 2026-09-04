using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using OmniBlock.Blocks;
using OmniBlock.Items;
using OmniBlock.NBT;

namespace OmniBlock.Registries;

/// <summary>A persistable name-to-ID contract for saves and client/server negotiation.</summary>
public sealed class ContentCatalogManifest
{
    private readonly FrozenDictionary<ResourceLocation, int> _blockIds;
    private readonly FrozenDictionary<ResourceLocation, EntityCatalogEntry> _entities;
    private readonly FrozenDictionary<ResourceLocation, int> _itemIds;
    private readonly FrozenDictionary<ResourceLocation, ProcessCatalogEntry> _processes;

    public ContentCatalogManifest(IEnumerable<KeyValuePair<ResourceLocation, int>> blockIds)
        : this(blockIds, [], [], [])
    {
    }

    public ContentCatalogManifest(IEnumerable<KeyValuePair<ResourceLocation, int>> blockIds,
        IEnumerable<KeyValuePair<ResourceLocation, int>> itemIds)
        : this(blockIds, itemIds, [], [])
    {
    }

    public ContentCatalogManifest(IEnumerable<KeyValuePair<ResourceLocation, int>> blockIds,
        IEnumerable<KeyValuePair<ResourceLocation, int>> itemIds,
        IEnumerable<KeyValuePair<ResourceLocation, ProcessCatalogEntry>> processes)
        : this(blockIds, itemIds, processes, [])
    {
    }

    public ContentCatalogManifest(IEnumerable<KeyValuePair<ResourceLocation, int>> blockIds,
        IEnumerable<KeyValuePair<ResourceLocation, int>> itemIds,
        IEnumerable<KeyValuePair<ResourceLocation, ProcessCatalogEntry>> processes,
        IEnumerable<KeyValuePair<ResourceLocation, EntityCatalogEntry>> entities)
    {
        _blockIds = Validate(blockIds, "block").ToFrozenDictionary();
        _itemIds = Validate(itemIds, "item").ToFrozenDictionary();
        _processes = processes.ToFrozenDictionary(
            pair => pair.Key, pair => pair.Value,
            EqualityComparer<ResourceLocation>.Default);
        _entities = ValidateEntities(entities).ToFrozenDictionary();
        Fingerprint = ComputeFingerprint(_blockIds, _itemIds, _processes, _entities);

        static Dictionary<ResourceLocation, int> Validate(IEnumerable<KeyValuePair<ResourceLocation, int>> entries, string kind)
        {
            var ids = new Dictionary<ResourceLocation, int>();
            var namesById = new Dictionary<int, ResourceLocation>();
            foreach (var (key, id) in entries)
            {
                if (!ids.TryAdd(key, id)) throw new ArgumentException($"Duplicate {kind} catalog name '{key}'.");
                if (namesById.TryGetValue(id, out var existing))
                    throw new ArgumentException($"{kind} catalog ID {id} is assigned to both '{existing}' and '{key}'.");
                namesById.Add(id, key);
            }

            return ids;
        }

        static Dictionary<ResourceLocation, EntityCatalogEntry> ValidateEntities(
            IEnumerable<KeyValuePair<ResourceLocation, EntityCatalogEntry>> entries)
        {
            var result = new Dictionary<ResourceLocation, EntityCatalogEntry>();
            var protocolIds = new Dictionary<int, ResourceLocation>();
            var objectIds = new Dictionary<int, ResourceLocation>();
            var globalIds = new Dictionary<int, ResourceLocation>();
            foreach (var (key, entry) in entries)
            {
                if (!result.TryAdd(key, entry)) throw new ArgumentException($"Duplicate entity catalog name '{key}'.");
                Add(protocolIds, entry.ProtocolId, "protocol", key);
                if (entry.ObjectSpawnId is { } objectId) Add(objectIds, objectId, "object-spawn", key);
                if (entry.GlobalSpawnId is { } globalId) Add(globalIds, globalId, "global-spawn", key);
            }

            return result;

            static void Add(Dictionary<int, ResourceLocation> ids, int id, string kind, ResourceLocation key)
            {
                if (ids.TryGetValue(id, out var existing))
                    throw new ArgumentException($"Entity {kind} ID {id} is assigned to both '{existing}' and '{key}'.");
                ids.Add(id, key);
            }
        }
    }

    public IReadOnlyDictionary<ResourceLocation, int> BlockIds => _blockIds;
    public IReadOnlyDictionary<ResourceLocation, int> ItemIds => _itemIds;
    public IReadOnlyDictionary<ResourceLocation, ProcessCatalogEntry> Processes => _processes;
    public IReadOnlyDictionary<ResourceLocation, EntityCatalogEntry> Entities => _entities;
    public string Fingerprint { get; }

    public CatalogCompatibility CompareTo(ContentCatalogManifest required)
    {
        List<ResourceLocation> missingBlocks = [], addedBlocks = [], missingItems = [], addedItems = [];
        List<CatalogIdMismatch> mismatched = [];
        CompareKind(_blockIds, required._blockIds, CatalogEntryKind.Block, missingBlocks, addedBlocks, mismatched);
        CompareKind(_itemIds, required._itemIds, CatalogEntryKind.Item, missingItems, addedItems, mismatched);
        List<ResourceLocation> missingProcesses = [], addedProcesses = [];
        List<ProcessCatalogMismatch> changedProcesses = [];
        foreach (var (key, wanted) in required._processes)
        {
            if (!_processes.TryGetValue(key, out var actual)) missingProcesses.Add(key);
            else if (actual != wanted) changedProcesses.Add(new ProcessCatalogMismatch(key, wanted, actual));
        }

        foreach (var key in _processes.Keys)
            if (!required._processes.ContainsKey(key))
                addedProcesses.Add(key);
        List<ResourceLocation> missingEntities = [], addedEntities = [];
        List<EntityCatalogMismatch> changedEntities = [];
        foreach (var (key, wanted) in required._entities)
        {
            if (!_entities.TryGetValue(key, out var actual)) missingEntities.Add(key);
            else if (actual != wanted) changedEntities.Add(new EntityCatalogMismatch(key, wanted, actual));
        }

        foreach (var key in _entities.Keys)
            if (!required._entities.ContainsKey(key))
                addedEntities.Add(key);
        return new CatalogCompatibility(Fingerprint == required.Fingerprint, missingBlocks, addedBlocks, missingItems,
            addedItems, mismatched, missingProcesses, addedProcesses, changedProcesses,
            [.. missingProcesses.Select(key => required._processes[key].ProviderType).Distinct()],
            missingEntities, addedEntities, changedEntities,
            [.. missingEntities.Select(key => required._entities[key].ConstructorProviderType).Distinct()]);

        static void CompareKind(IReadOnlyDictionary<ResourceLocation, int> actual,
            IReadOnlyDictionary<ResourceLocation, int> wanted, CatalogEntryKind kind,
            List<ResourceLocation> missing, List<ResourceLocation> added, List<CatalogIdMismatch> mismatched)
        {
            foreach (var (key, requiredId) in wanted)
            {
                if (!actual.TryGetValue(key, out var actualId)) missing.Add(key);
                else if (actualId != requiredId) mismatched.Add(new CatalogIdMismatch(kind, key, requiredId, actualId));
            }

            foreach (var key in actual.Keys)
                if (!wanted.ContainsKey(key))
                    added.Add(key);
        }
    }

    public NBTTagCompound ToNbt()
    {
        NBTTagCompound tag = new();
        tag.SetTag("Blocks", Write(_blockIds));
        tag.SetTag("Items", Write(_itemIds));
        NBTTagList processList = new();
        foreach (var (key, process) in _processes.OrderBy(pair => pair.Key))
        {
            NBTTagCompound entry = new();
            entry.SetString("Name", key.ToString());
            entry.SetString("Type", process.ProviderType.ToString());
            entry.SetString("Hash", process.DefinitionHash);
            processList.SetTag(entry);
        }

        tag.SetTag("Processes", processList);
        NBTTagList entityList = new();
        foreach (var (key, entity) in _entities.OrderBy(pair => pair.Key))
        {
            NBTTagCompound entry = new();
            entry.SetString("Name", key.ToString());
            entry.SetString("Constructor", entity.ConstructorProviderType.ToString());
            entry.SetString("Hash", entity.DefinitionHash);
            entry.SetInteger("ProtocolId", entity.ProtocolId);
            if (entity.ObjectSpawnId is { } objectId) entry.SetInteger("ObjectSpawnId", objectId);
            if (entity.GlobalSpawnId is { } globalId) entry.SetInteger("GlobalSpawnId", globalId);
            entityList.SetTag(entry);
        }

        tag.SetTag("Entities", entityList);
        tag.SetString("Fingerprint", Fingerprint);
        return tag;

        static NBTTagList Write(IReadOnlyDictionary<ResourceLocation, int> entries)
        {
            NBTTagList list = new();
            foreach (var (key, id) in entries.OrderBy(static pair => pair.Key))
            {
                NBTTagCompound entry = new();
                entry.SetString("Name", key.ToString());
                entry.SetInteger("Id", id);
                list.SetTag(entry);
            }

            return list;
        }
    }

    public static ContentCatalogManifest FromNbt(NBTTagCompound tag) =>
        new(Read(tag, "Blocks"), Read(tag, "Items"), ReadProcesses(tag), ReadEntities(tag));

    private static IEnumerable<KeyValuePair<ResourceLocation, EntityCatalogEntry>> ReadEntities(NBTTagCompound tag)
    {
        var list = tag.GetTagList("Entities");
        for (var i = 0; i < list.TagCount(); i++)
        {
            var entry = (NBTTagCompound)list.TagAt(i);
            yield return new KeyValuePair<ResourceLocation, EntityCatalogEntry>(ResourceLocation.Parse(entry.GetString("Name")), new EntityCatalogEntry(
                ResourceLocation.Parse(entry.GetString("Constructor")), entry.GetString("Hash"),
                entry.GetInteger("ProtocolId"),
                entry.HasKey("ObjectSpawnId") ? entry.GetInteger("ObjectSpawnId") : null,
                entry.HasKey("GlobalSpawnId") ? entry.GetInteger("GlobalSpawnId") : null));
        }
    }

    private static IEnumerable<KeyValuePair<ResourceLocation, ProcessCatalogEntry>> ReadProcesses(NBTTagCompound tag)
    {
        var list = tag.GetTagList("Processes");
        for (var i = 0; i < list.TagCount(); i++)
        {
            var entry = (NBTTagCompound)list.TagAt(i);
            yield return new KeyValuePair<ResourceLocation, ProcessCatalogEntry>(ResourceLocation.Parse(entry.GetString("Name")),
                new ProcessCatalogEntry(ResourceLocation.Parse(entry.GetString("Type")), entry.GetString("Hash")));
        }
    }

    private static IEnumerable<KeyValuePair<ResourceLocation, int>> Read(NBTTagCompound tag, string name)
    {
        var list = tag.GetTagList(name);
        for (var i = 0; i < list.TagCount(); i++)
        {
            var entry = (NBTTagCompound)list.TagAt(i);
            yield return new KeyValuePair<ResourceLocation, int>(ResourceLocation.Parse(entry.GetString("Name")), entry.GetInteger("Id"));
        }
    }

    private static string ComputeFingerprint(IReadOnlyDictionary<ResourceLocation, int> blocks,
        IReadOnlyDictionary<ResourceLocation, int> items,
        IReadOnlyDictionary<ResourceLocation, ProcessCatalogEntry> processes,
        IReadOnlyDictionary<ResourceLocation, EntityCatalogEntry> entities)
    {
        StringBuilder canonical = new();
        Append("block", blocks);
        Append("item", items);
        foreach (var (key, process) in processes.OrderBy(pair => pair.Key))
        {
            canonical.Append("process:").Append(key).Append('=').Append(process.ProviderType)
                .Append('@').Append(process.DefinitionHash).Append('\n');
        }

        foreach (var (key, entity) in entities.OrderBy(pair => pair.Key))
        {
            canonical.Append("entity:").Append(key).Append('=').Append(entity.ConstructorProviderType)
                .Append('@').Append(entity.DefinitionHash).Append(':').Append(entity.ProtocolId)
                .Append(':').Append(entity.ObjectSpawnId?.ToString() ?? "-")
                .Append(':').Append(entity.GlobalSpawnId?.ToString() ?? "-").Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));

        void Append(string kind, IReadOnlyDictionary<ResourceLocation, int> entries)
        {
            foreach (var (key, id) in entries.OrderBy(static pair => pair.Key.ToString(), StringComparer.Ordinal))
                canonical.Append(kind).Append(':').Append(key).Append('=').Append(id).Append('\n');
        }
    }
}

public sealed record ProcessCatalogEntry(ResourceLocation ProviderType, string DefinitionHash);

public sealed record ProcessCatalogMismatch(ResourceLocation Key, ProcessCatalogEntry Required, ProcessCatalogEntry Actual);

public sealed record EntityCatalogEntry(
    ResourceLocation ConstructorProviderType,
    string DefinitionHash,
    int ProtocolId,
    int? ObjectSpawnId,
    int? GlobalSpawnId);

public sealed record EntityCatalogMismatch(ResourceLocation Key, EntityCatalogEntry Required, EntityCatalogEntry Actual)
{
    public bool DefinitionChanged => Required.ConstructorProviderType != Actual.ConstructorProviderType
                                     || Required.DefinitionHash != Actual.DefinitionHash;

    public bool TransportMappingChanged => Required.ProtocolId != Actual.ProtocolId
                                           || Required.ObjectSpawnId != Actual.ObjectSpawnId
                                           || Required.GlobalSpawnId != Actual.GlobalSpawnId;
}

public enum CatalogEntryKind
{
    Block,
    Item
}

public sealed record CatalogIdMismatch(CatalogEntryKind Kind, ResourceLocation Key, int RequiredId, int ActualId);

public sealed record CatalogCompatibility(
    bool IsExactMatch,
    IReadOnlyList<ResourceLocation> MissingBlocks,
    IReadOnlyList<ResourceLocation> AdditionalBlocks,
    IReadOnlyList<ResourceLocation> MissingItems,
    IReadOnlyList<ResourceLocation> AdditionalItems,
    IReadOnlyList<CatalogIdMismatch> IdMismatches,
    IReadOnlyList<ResourceLocation> MissingProcesses,
    IReadOnlyList<ResourceLocation> AdditionalProcesses,
    IReadOnlyList<ProcessCatalogMismatch> ChangedProcesses,
    IReadOnlyList<ResourceLocation> MissingProcessProviders,
    IReadOnlyList<ResourceLocation> MissingEntities,
    IReadOnlyList<ResourceLocation> AdditionalEntities,
    IReadOnlyList<EntityCatalogMismatch> ChangedEntities,
    IReadOnlyList<ResourceLocation> MissingEntityConstructorProviders)
{
    public IReadOnlyList<ResourceLocation> MissingEntries => [.. MissingBlocks, .. MissingItems, .. MissingProcesses, .. MissingEntities];
    public IReadOnlyList<ResourceLocation> AdditionalEntries => [.. AdditionalBlocks, .. AdditionalItems, .. AdditionalProcesses, .. AdditionalEntities];

    /// <summary>Additional content is safe for a save; missing or remapped content is not.</summary>
    public bool CanLoadWorld => MissingEntries.Count == 0 && IdMismatches.Count == 0
                                                          && ChangedProcesses.Count == 0
                                                          && ChangedEntities.All(static mismatch => !mismatch.DefinitionChanged);

    /// <summary>Network peers require precisely the same numeric catalog.</summary>
    public bool CanSynchronizeClient => IsExactMatch;

    public string Diagnostic => string.Join("; ",
        new[]
        {
            MissingBlocks.Count == 0 ? null : $"missing blocks: {string.Join(", ", MissingBlocks)}", MissingItems.Count == 0 ? null : $"missing items: {string.Join(", ", MissingItems)}",
            MissingProcesses.Count == 0 ? null : $"missing processes: {string.Join(", ", MissingProcesses)}", MissingProcessProviders.Count == 0 ? null : $"required process providers: {string.Join(", ", MissingProcessProviders)}",
            MissingEntities.Count == 0 ? null : $"missing entity types (required mods may be absent): {string.Join(", ", MissingEntities)}",
            MissingEntityConstructorProviders.Count == 0 ? null : $"required entity constructor providers: {string.Join(", ", MissingEntityConstructorProviders)}",
            ChangedEntities.Count == 0 ? null : $"changed entity definitions: {string.Join(", ", ChangedEntities.Select(m => m.Key))}",
            ChangedProcesses.Count == 0 ? null : $"changed processes: {string.Join(", ", ChangedProcesses.Select(m => $"{m.Key} ({m.Required.ProviderType} -> {m.Actual.ProviderType})"))}",
            IdMismatches.Count == 0 ? null : $"remapped entries: {string.Join(", ", IdMismatches.Select(m => $"{m.Kind.ToString().ToLowerInvariant()} {m.Key} ({m.RequiredId} -> {m.ActualId})"))}"
        }.Where(static value => value is not null));
}

public static class ContentIdAllocator
{
    public static List<BlockDefinition> AssignBlockIds(
        IEnumerable<BlockDefinition> definitions,
        ContentCatalogManifest? previous = null)
    {
        var source = definitions.ToList();
        var keys = new HashSet<ResourceLocation>();
        var used = new Dictionary<int, ResourceLocation>();
        foreach (var definition in source)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            if (!keys.Add(key)) throw new InvalidOperationException($"Duplicate block name '{key}'.");
            if (definition.ProtocolId < 0) continue;
            if (definition.ProtocolId >= BlockRegistry.ProtocolIdCapacity)
                throw new InvalidOperationException($"Block '{key}' protocol ID {definition.ProtocolId} exceeds the current save/wire limit.");
            if (used.TryGetValue(definition.ProtocolId, out var collision))
                throw new InvalidOperationException($"Blocks '{collision}' and '{key}' both request protocol ID {definition.ProtocolId}.");
            used.Add(definition.ProtocolId, key);
        }

        var assigned = new Dictionary<ResourceLocation, int>();
        foreach (var definition in source.Where(static definition => definition.ProtocolId < 0)
                     .OrderBy(static definition => $"{definition.Namespace}:{definition.Name}", StringComparer.Ordinal))
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            var id = -1;
            if (previous is not null && previous.BlockIds.TryGetValue(key, out var savedId)
                                     && savedId >= 0 && savedId < BlockRegistry.ProtocolIdCapacity
                                     && !used.ContainsKey(savedId))
                id = savedId;
            if (id < 0)
            {
                for (var candidate = 0; candidate < BlockRegistry.ProtocolIdCapacity; candidate++)
                    if (!used.ContainsKey(candidate))
                    {
                        id = candidate;
                        break;
                    }
            }

            if (id < 0) throw new InvalidOperationException($"No protocol IDs remain for block '{key}'.");
            used.Add(id, key);
            assigned.Add(key, id);
        }

        return source.Select(definition => definition.ProtocolId >= 0
                ? definition
                : definition with
                {
                    ProtocolId = assigned[new ResourceLocation(definition.Namespace, definition.Name)]
                })
            .ToList();
    }

    public static List<ItemDefinition> AssignItemIds(IEnumerable<ItemDefinition> definitions,
        ContentCatalogManifest? previous = null)
    {
        var source = definitions.ToList();
        var keys = new HashSet<ResourceLocation>();
        var used = new Dictionary<int, ResourceLocation>();
        foreach (var definition in source)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            if (!keys.Add(key)) throw new InvalidOperationException($"Duplicate item name '{key}'.");
            if (definition.ProtocolId < 0) continue;
            if (definition.ProtocolId is < 256 or >= 32000)
                throw new InvalidOperationException($"Item '{key}' protocol ID {definition.ProtocolId} is outside 256..31999.");
            if (used.TryGetValue(definition.ProtocolId, out var collision))
                throw new InvalidOperationException($"Items '{collision}' and '{key}' both request protocol ID {definition.ProtocolId}.");
            used.Add(definition.ProtocolId, key);
        }

        foreach (var definition in source.Where(static d => d.ProtocolId < 0)
                     .OrderBy(static d => $"{d.Namespace}:{d.Name}", StringComparer.Ordinal))
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            var id = previous is not null && previous.ItemIds.TryGetValue(key, out var saved) && saved is >= 256 and < 32000 && !used.ContainsKey(saved) ? saved : -1;
            if (id < 0)
            {
                for (var candidate = 256; candidate < 32000; candidate++)
                    if (!used.ContainsKey(candidate))
                    {
                        id = candidate;
                        break;
                    }
            }

            if (id < 0) throw new InvalidOperationException($"No protocol IDs remain for item '{key}'.");
            definition.ProtocolId = id;
            used.Add(id, key);
        }

        return source;
    }
}
