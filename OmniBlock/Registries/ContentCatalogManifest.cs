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
    private readonly FrozenDictionary<ResourceLocation, int> _itemIds;
    private readonly FrozenDictionary<ResourceLocation, ProcessCatalogEntry> _processes;

    public ContentCatalogManifest(IEnumerable<KeyValuePair<ResourceLocation, int>> blockIds)
        : this(blockIds, [], []) { }

    public ContentCatalogManifest(IEnumerable<KeyValuePair<ResourceLocation, int>> blockIds,
        IEnumerable<KeyValuePair<ResourceLocation, int>> itemIds)
        : this(blockIds, itemIds, []) { }

    public ContentCatalogManifest(IEnumerable<KeyValuePair<ResourceLocation, int>> blockIds,
        IEnumerable<KeyValuePair<ResourceLocation, int>> itemIds,
        IEnumerable<KeyValuePair<ResourceLocation, ProcessCatalogEntry>> processes)
    {
        _blockIds = Validate(blockIds, "block").ToFrozenDictionary();
        _itemIds = Validate(itemIds, "item").ToFrozenDictionary();
        _processes = processes.ToFrozenDictionary(
            pair => pair.Key, pair => pair.Value,
            EqualityComparer<ResourceLocation>.Default);
        Fingerprint = ComputeFingerprint(_blockIds, _itemIds, _processes);

        static Dictionary<ResourceLocation, int> Validate(IEnumerable<KeyValuePair<ResourceLocation, int>> entries, string kind)
        {
            var ids = new Dictionary<ResourceLocation, int>();
            var namesById = new Dictionary<int, ResourceLocation>();
            foreach ((ResourceLocation key, int id) in entries)
            {
                if (!ids.TryAdd(key, id)) throw new ArgumentException($"Duplicate {kind} catalog name '{key}'.");
                if (namesById.TryGetValue(id, out ResourceLocation existing))
                    throw new ArgumentException($"{kind} catalog ID {id} is assigned to both '{existing}' and '{key}'.");
                namesById.Add(id, key);
            }
            return ids;
        }
    }

    public IReadOnlyDictionary<ResourceLocation, int> BlockIds => _blockIds;
    public IReadOnlyDictionary<ResourceLocation, int> ItemIds => _itemIds;
    public IReadOnlyDictionary<ResourceLocation, ProcessCatalogEntry> Processes => _processes;
    public string Fingerprint { get; }

    public CatalogCompatibility CompareTo(ContentCatalogManifest required)
    {
        List<ResourceLocation> missingBlocks = [], addedBlocks = [], missingItems = [], addedItems = [];
        List<CatalogIdMismatch> mismatched = [];
        CompareKind(_blockIds, required._blockIds, CatalogEntryKind.Block, missingBlocks, addedBlocks, mismatched);
        CompareKind(_itemIds, required._itemIds, CatalogEntryKind.Item, missingItems, addedItems, mismatched);
        List<ResourceLocation> missingProcesses = [], addedProcesses = [];
        List<ProcessCatalogMismatch> changedProcesses = [];
        foreach ((ResourceLocation key, ProcessCatalogEntry wanted) in required._processes)
            if (!_processes.TryGetValue(key, out ProcessCatalogEntry? actual)) missingProcesses.Add(key);
            else if (actual != wanted) changedProcesses.Add(new(key, wanted, actual));
        foreach (ResourceLocation key in _processes.Keys)
            if (!required._processes.ContainsKey(key)) addedProcesses.Add(key);
        return new(Fingerprint == required.Fingerprint, missingBlocks, addedBlocks, missingItems,
            addedItems, mismatched, missingProcesses, addedProcesses, changedProcesses,
            [.. missingProcesses.Select(key => required._processes[key].ProviderType).Distinct()]);

        static void CompareKind(IReadOnlyDictionary<ResourceLocation, int> actual,
            IReadOnlyDictionary<ResourceLocation, int> wanted, CatalogEntryKind kind,
            List<ResourceLocation> missing, List<ResourceLocation> added, List<CatalogIdMismatch> mismatched)
        {
            foreach ((ResourceLocation key, int requiredId) in wanted)
                if (!actual.TryGetValue(key, out int actualId)) missing.Add(key);
                else if (actualId != requiredId) mismatched.Add(new(kind, key, requiredId, actualId));
            foreach (ResourceLocation key in actual.Keys)
                if (!wanted.ContainsKey(key)) added.Add(key);
        }
    }

    public NBTTagCompound ToNbt()
    {
        NBTTagCompound tag = new();
        tag.SetTag("Blocks", Write(_blockIds));
        tag.SetTag("Items", Write(_itemIds));
        NBTTagList processList = new();
        foreach ((ResourceLocation key, ProcessCatalogEntry process) in _processes.OrderBy(pair => pair.Key))
        {
            NBTTagCompound entry = new();
            entry.SetString("Name", key.ToString());
            entry.SetString("Type", process.ProviderType.ToString());
            entry.SetString("Hash", process.DefinitionHash);
            processList.SetTag(entry);
        }
        tag.SetTag("Processes", processList);
        tag.SetString("Fingerprint", Fingerprint);
        return tag;
        static NBTTagList Write(IReadOnlyDictionary<ResourceLocation, int> entries)
        {
            NBTTagList list = new();
            foreach ((ResourceLocation key, int id) in entries.OrderBy(static pair => pair.Key))
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
        new(Read(tag, "Blocks"), Read(tag, "Items"), ReadProcesses(tag));

    private static IEnumerable<KeyValuePair<ResourceLocation, ProcessCatalogEntry>> ReadProcesses(NBTTagCompound tag)
    {
        NBTTagList list = tag.GetTagList("Processes");
        for (int i = 0; i < list.TagCount(); i++)
        {
            NBTTagCompound entry = (NBTTagCompound)list.TagAt(i);
            yield return new(ResourceLocation.Parse(entry.GetString("Name")),
                new(ResourceLocation.Parse(entry.GetString("Type")), entry.GetString("Hash")));
        }
    }

    private static IEnumerable<KeyValuePair<ResourceLocation, int>> Read(NBTTagCompound tag, string name)
    {
        NBTTagList list = tag.GetTagList(name);
        for (int i = 0; i < list.TagCount(); i++)
        {
            NBTTagCompound entry = (NBTTagCompound)list.TagAt(i);
            yield return new(ResourceLocation.Parse(entry.GetString("Name")), entry.GetInteger("Id"));
        }
    }

    private static string ComputeFingerprint(IReadOnlyDictionary<ResourceLocation, int> blocks,
        IReadOnlyDictionary<ResourceLocation, int> items,
        IReadOnlyDictionary<ResourceLocation, ProcessCatalogEntry> processes)
    {
        StringBuilder canonical = new();
        Append("block", blocks);
        Append("item", items);
        foreach ((ResourceLocation key, ProcessCatalogEntry process) in processes.OrderBy(pair => pair.Key))
            canonical.Append("process:").Append(key).Append('=').Append(process.ProviderType)
                .Append('@').Append(process.DefinitionHash).Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
        void Append(string kind, IReadOnlyDictionary<ResourceLocation, int> entries)
        {
            foreach ((ResourceLocation key, int id) in entries.OrderBy(static pair => pair.Key.ToString(), StringComparer.Ordinal))
                canonical.Append(kind).Append(':').Append(key).Append('=').Append(id).Append('\n');
        }
    }
}

public sealed record ProcessCatalogEntry(ResourceLocation ProviderType, string DefinitionHash);
public sealed record ProcessCatalogMismatch(ResourceLocation Key, ProcessCatalogEntry Required, ProcessCatalogEntry Actual);

public enum CatalogEntryKind { Block, Item }
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
    IReadOnlyList<ResourceLocation> MissingProcessProviders)
{
    public IReadOnlyList<ResourceLocation> MissingEntries => [.. MissingBlocks, .. MissingItems, .. MissingProcesses];
    public IReadOnlyList<ResourceLocation> AdditionalEntries => [.. AdditionalBlocks, .. AdditionalItems, .. AdditionalProcesses];
    /// <summary>Additional content is safe for a save; missing or remapped content is not.</summary>
    public bool CanLoadWorld => MissingEntries.Count == 0 && IdMismatches.Count == 0 && ChangedProcesses.Count == 0;
    /// <summary>Network peers require precisely the same numeric catalog.</summary>
    public bool CanSynchronizeClient => IsExactMatch;
    public string Diagnostic => string.Join("; ", new[]
    {
        MissingBlocks.Count == 0 ? null : $"missing blocks: {string.Join(", ", MissingBlocks)}",
        MissingItems.Count == 0 ? null : $"missing items: {string.Join(", ", MissingItems)}",
        MissingProcesses.Count == 0 ? null : $"missing processes: {string.Join(", ", MissingProcesses)}",
        MissingProcessProviders.Count == 0 ? null : $"required process providers: {string.Join(", ", MissingProcessProviders)}",
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

    public static List<ItemDefinition> AssignItemIds(IEnumerable<ItemDefinition> definitions,
        ContentCatalogManifest? previous = null)
    {
        List<ItemDefinition> source = definitions.ToList();
        var keys = new HashSet<ResourceLocation>();
        var used = new Dictionary<int, ResourceLocation>();
        foreach (ItemDefinition definition in source)
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            if (!keys.Add(key)) throw new InvalidOperationException($"Duplicate item name '{key}'.");
            if (definition.ProtocolId < 0) continue;
            if (definition.ProtocolId is < 256 or >= 32000)
                throw new InvalidOperationException($"Item '{key}' protocol ID {definition.ProtocolId} is outside 256..31999.");
            if (used.TryGetValue(definition.ProtocolId, out ResourceLocation collision))
                throw new InvalidOperationException($"Items '{collision}' and '{key}' both request protocol ID {definition.ProtocolId}.");
            used.Add(definition.ProtocolId, key);
        }
        foreach (ItemDefinition definition in source.Where(static d => d.ProtocolId < 0)
                     .OrderBy(static d => $"{d.Namespace}:{d.Name}", StringComparer.Ordinal))
        {
            ResourceLocation key = new(definition.Namespace, definition.Name);
            int id = previous is not null && previous.ItemIds.TryGetValue(key, out int saved) && saved is >= 256 and < 32000 && !used.ContainsKey(saved) ? saved : -1;
            if (id < 0) for (int candidate = 256; candidate < 32000; candidate++)
                if (!used.ContainsKey(candidate)) { id = candidate; break; }
            if (id < 0) throw new InvalidOperationException($"No protocol IDs remain for item '{key}'.");
            definition.ProtocolId = id;
            used.Add(id, key);
        }
        return source;
    }
}
