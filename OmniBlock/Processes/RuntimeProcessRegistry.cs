using System.Collections.Frozen;
using OmniBlock.Inventories;
using OmniBlock.Items;

namespace OmniBlock.Processes;

/// <summary>Immutable generic index plus optimized views for built-in process families.</summary>
public sealed class RuntimeProcessRegistry
{
    private readonly FrozenDictionary<ResourceLocation, ICompiledProcess> _byId;
    private readonly FrozenDictionary<ResourceLocation, IReadOnlyList<ICompiledProcess>> _byType;

    internal RuntimeProcessRegistry(
        IEnumerable<ICompiledProcess> processes,
        IProcessProviderRegistry providers)
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
    }

    public static RuntimeProcessRegistry Empty { get; } = new(
        [], new ProcessProviderRegistry([]));

    public int Count => _byId.Count;
    public IEnumerable<ResourceLocation> Keys => _byId.Keys;
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
    private readonly CompiledCraftingProcess[] _recipes;

    internal RuntimeCraftingProcessView(IEnumerable<CompiledCraftingProcess> recipes) =>
        _recipes = [.. recipes];

    public int Count => _recipes.Length;

    public ItemStack? Craft(InventoryCrafting input)
    {
        ArgumentNullException.ThrowIfNull(input);
        foreach (CompiledCraftingProcess process in _recipes)
            if (process.Recipe.Matches(input)) return process.Recipe.GetCraftingResult(input);
        return null;
    }
}

public sealed class RuntimeSmeltingProcessView
{
    private readonly FrozenDictionary<int, CompiledSmeltingProcess> _byInputItemId;

    internal RuntimeSmeltingProcessView(IEnumerable<CompiledSmeltingProcess> processes) =>
        _byInputItemId = processes.ToFrozenDictionary(process => process.Input.ItemId);

    public int Count => _byInputItemId.Count;

    public ItemStack? Find(ItemStack input) => Find(input.ItemId);

    public ItemStack? Find(int inputItemId) =>
        _byInputItemId.TryGetValue(inputItemId, out CompiledSmeltingProcess? process)
            ? process.Output.Copy()
            : null;
}
