using System.Collections.Frozen;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.Registries;

namespace OmniBlock.Processes;

/// <summary>Immutable generic index plus optimized views for built-in process families.</summary>
public sealed class RuntimeProcessRegistry
{
    private readonly FrozenDictionary<ResourceLocation, ICompiledProcess> _byId;
    private readonly FrozenDictionary<ResourceLocation, IReadOnlyList<ICompiledProcess>> _byType;
    private readonly FrozenDictionary<ResourceLocation, ProcessCatalogEntry> _manifestEntries;

    internal RuntimeProcessRegistry(
        IEnumerable<ICompiledProcess> processes,
        IProcessProviderRegistry providers,
        IEnumerable<KeyValuePair<ResourceLocation, ProcessCatalogEntry>>? manifestEntries = null)
    {
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(providers);
        var byId = new Dictionary<ResourceLocation, ICompiledProcess>();
        foreach (ICompiledProcess process in processes)
        {
            if (!byId.TryAdd(process.Id, process))
                throw new InvalidOperationException($"Duplicate process id '{process.Id}'.");
        }

        ICompiledProcess[] entries = [.. byId.Values.OrderBy(process => process.Id)];
        providers.Validate(entries);
        _byId = byId.ToFrozenDictionary();
        _byType = entries.GroupBy(process => process.ProviderType).ToFrozenDictionary(
            group => group.Key,
            group => (IReadOnlyList<ICompiledProcess>)Array.AsReadOnly(group.ToArray()));
        Crafting = new RuntimeCraftingProcessView(entries.OfType<CompiledCraftingProcess>());
        Smelting = new RuntimeSmeltingProcessView(entries.OfType<CompiledSmeltingProcess>());
        _manifestEntries = (manifestEntries ?? entries.Select(process =>
            new KeyValuePair<ResourceLocation, ProcessCatalogEntry>(process.Id,
                new(process.ProviderType, "")))).ToFrozenDictionary();
    }

    internal static RuntimeProcessRegistry Compile(
        IEnumerable<ProcessDefinition> definitions,
        IProcessProviderRegistry providers,
        in ProcessBuildContext context)
    {
        var ids = new HashSet<ResourceLocation>();
        var compiled = new List<ICompiledProcess>();
        var manifestEntries = new List<KeyValuePair<ResourceLocation, ProcessCatalogEntry>>();
        foreach (ProcessDefinition definition in definitions)
        {
            ResourceLocation id = definition.GetProcessId();
            if (!ids.Add(id)) throw new InvalidOperationException($"Duplicate process id '{id}'.");
            ResourceLocation type = definition.GetProviderType();
            compiled.Add(providers.Build(type, id, definition.GetProviderDefinition(), context));
            manifestEntries.Add(new(id, new ProcessCatalogEntry(type, definition.ComputeCanonicalHash())));
        }
        return new RuntimeProcessRegistry(compiled, providers, manifestEntries);
    }

    public static RuntimeProcessRegistry Empty { get; } = new(
        [], new ProcessProviderRegistry([]));

    public int Count => _byId.Count;
    public IEnumerable<ResourceLocation> Keys => _byId.Keys;
    public IEnumerable<ICompiledProcess> Values => _byId.Values;
    public IReadOnlyDictionary<ResourceLocation, ProcessCatalogEntry> ManifestEntries => _manifestEntries;
    public RuntimeCraftingProcessView Crafting { get; }
    public RuntimeSmeltingProcessView Smelting { get; }

    public ICompiledProcess Get(ResourceLocation id) =>
        _byId.TryGetValue(id, out ICompiledProcess? process)
            ? process
            : throw new KeyNotFoundException($"Unknown process '{id}'.");

    public bool TryGet(ResourceLocation id, out ICompiledProcess? process) =>
        _byId.TryGetValue(id, out process);

    public IReadOnlyList<ICompiledProcess> GetByType(ResourceLocation providerType) =>
        _byType.TryGetValue(providerType, out IReadOnlyList<ICompiledProcess>? processes)
            ? processes
            : Array.Empty<ICompiledProcess>();
}

public sealed class RuntimeCraftingProcessView
{
    private readonly ICompiledCraftingProcess[] _recipes;

    internal RuntimeCraftingProcessView(IEnumerable<CompiledCraftingProcess> recipes) =>
        _recipes = [.. recipes];

    public int Count => _recipes.Length;

    public ICompiledCraftingProcess? Find(InventoryCrafting input)
    {
        ArgumentNullException.ThrowIfNull(input);
        foreach (ICompiledCraftingProcess process in _recipes)
            if (process.Matches(input)) return process;
        return null;
    }

    public ItemStack? Craft(InventoryCrafting input) => Find(input)?.CreateResult();
}

public sealed class RuntimeSmeltingProcessView
{
    private readonly FrozenDictionary<int, ICompiledSmeltingProcess> _byInputItemId;

    internal RuntimeSmeltingProcessView(IEnumerable<CompiledSmeltingProcess> processes) =>
        _byInputItemId = processes.ToFrozenDictionary(
            process => process.Input.Item.Id,
            process => (ICompiledSmeltingProcess)process);

    public int Count => _byInputItemId.Count;

    public ICompiledSmeltingProcess? Find(ItemStack input) =>
        _byInputItemId.TryGetValue(input.ItemId, out ICompiledSmeltingProcess? process)
        && process.Matches(input) ? process : null;

    public ICompiledSmeltingProcess? Find(int inputItemId) =>
        _byInputItemId.GetValueOrDefault(inputItemId);

    public ItemStack? Smelt(ItemStack input) => Find(input)?.CreateResult();
}
