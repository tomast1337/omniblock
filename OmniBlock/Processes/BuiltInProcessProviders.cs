using System.Text.Json;
using OmniBlock.Items;
using OmniBlock.Recipes;

namespace OmniBlock.Processes;

public static class BuiltInProcessProviders
{
    public static ProcessProviderRegistry CreateRegistry() => new(
    [
        Pair(ProcessTypes.CraftingShaped, new ShapedCraftingProcessProvider()),
        Pair(ProcessTypes.CraftingShapeless, new ShapelessCraftingProcessProvider()),
        Pair(ProcessTypes.Smelting, new SmeltingProcessProvider())
    ]);

    private static KeyValuePair<ResourceLocation, IProcessProvider> Pair(
        ResourceLocation type, IProcessProvider provider) => new(type, provider);
}

internal sealed record CompiledCraftingProcess(
    ResourceLocation Id,
    ResourceLocation ProviderType,
    IRecipe Recipe) : ICompiledProcess;

internal sealed record CompiledSmeltingProcess(
    ResourceLocation Id,
    ResourceLocation ProviderType,
    ItemStack Input,
    ItemStack Output) : ICompiledProcess;

internal sealed class ShapedCraftingProcessProvider : IProcessProvider
{
    public ICompiledProcess Build(
        ResourceLocation id,
        JsonElement definition,
        in ProcessBuildContext context)
    {
        ShapedCraftingDefinition schema = ProcessJson.Deserialize<ShapedCraftingDefinition>(definition, id);
        if (schema.Pattern.Length is < 1 or > 3)
            throw new ArgumentException("Pattern height must be between 1 and 3.");
        int width = schema.Pattern.Max(row => row.Length);
        if (width is < 1 or > 3) throw new ArgumentException("Pattern width must be between 1 and 3.");

        var keys = new Dictionary<char, ItemStack>();
        foreach ((string symbol, string reference) in schema.Key)
        {
            if (symbol.Length != 1 || symbol[0] == ' ')
                throw new ArgumentException($"Invalid shaped-recipe key '{symbol}'.");
            if (!keys.TryAdd(symbol[0], context.ResolveItemStack(reference, defaultMeta: -1)))
                throw new ArgumentException($"Duplicate shaped-recipe key '{symbol}'.");
        }

        var grid = new ItemStack?[width * schema.Pattern.Length];
        for (int row = 0; row < schema.Pattern.Length; row++)
        {
            string patternRow = schema.Pattern[row];
            for (int column = 0; column < patternRow.Length; column++)
            {
                char symbol = patternRow[column];
                if (symbol == ' ') continue;
                if (!keys.TryGetValue(symbol, out ItemStack? ingredient))
                    throw new ArgumentException($"Pattern uses undefined key '{symbol}'.");
                grid[column + row * width] = ingredient.Copy();
            }
        }

        ItemStack result = ProcessJson.ResolveResult(schema.Result, context);
        return new CompiledCraftingProcess(
            id, ProcessTypes.CraftingShaped,
            new ShapedRecipes(width, schema.Pattern.Length, grid, result));
    }

    public void Validate(IReadOnlyList<ICompiledProcess> processes) =>
        ProcessJson.ValidateEquivalentCraftingRecipes(processes);
}

internal sealed class ShapelessCraftingProcessProvider : IProcessProvider
{
    public ICompiledProcess Build(
        ResourceLocation id,
        JsonElement definition,
        in ProcessBuildContext context)
    {
        ShapelessCraftingDefinition schema = ProcessJson.Deserialize<ShapelessCraftingDefinition>(definition, id);
        if (schema.Ingredients.Length is < 1 or > 9)
            throw new ArgumentException("Shapeless recipes must contain between 1 and 9 ingredients.");
        ProcessBuildContext buildContext = context;
        List<ItemStack> ingredients = [.. schema.Ingredients.Select(reference =>
            buildContext.ResolveItemStack(reference, defaultMeta: -1))];
        return new CompiledCraftingProcess(
            id, ProcessTypes.CraftingShapeless,
            new ShapelessRecipes(ProcessJson.ResolveResult(schema.Result, context), ingredients));
    }

    public void Validate(IReadOnlyList<ICompiledProcess> processes) =>
        ProcessJson.ValidateEquivalentCraftingRecipes(processes);
}

internal sealed class SmeltingProcessProvider : IProcessProvider
{
    public ICompiledProcess Build(
        ResourceLocation id,
        JsonElement definition,
        in ProcessBuildContext context)
    {
        SmeltingDefinition schema = ProcessJson.Deserialize<SmeltingDefinition>(definition, id);
        if (string.IsNullOrWhiteSpace(schema.Input))
            throw new ArgumentException("Smelting process has no input.");
        return new CompiledSmeltingProcess(
            id,
            ProcessTypes.Smelting,
            context.ResolveItemStack(schema.Input, defaultMeta: -1),
            ProcessJson.ResolveResult(schema.Result, context));
    }

    public void Validate(IReadOnlyList<ICompiledProcess> processes)
    {
        var inputs = new Dictionary<int, ResourceLocation>();
        foreach (CompiledSmeltingProcess process in processes.Cast<CompiledSmeltingProcess>())
        {
            if (!inputs.TryAdd(process.Input.ItemId, process.Id))
                throw new InvalidOperationException(
                    $"Processes '{inputs[process.Input.ItemId]}' and '{process.Id}' overlap on input item {process.Input.ItemId}.");
        }
    }
}

internal static class ProcessJson
{
    public static T Deserialize<T>(JsonElement definition, ResourceLocation id) where T : class =>
        definition.Deserialize<T>()
        ?? throw new ArgumentException($"Process '{id}' has an empty provider definition.");

    public static ItemStack ResolveResult(
        ProcessItemStackDefinition result,
        in ProcessBuildContext context)
    {
        if (string.IsNullOrWhiteSpace(result.Id)) throw new ArgumentException("Process has no result item.");
        if (result.Count < 1) throw new ArgumentOutOfRangeException(nameof(result), "Result count must be positive.");
        return context.ResolveItemStack(result.Id, result.Count);
    }

    public static void ValidateEquivalentCraftingRecipes(IReadOnlyList<ICompiledProcess> processes)
    {
        for (int current = 0; current < processes.Count; current++)
        {
            CompiledCraftingProcess candidate = (CompiledCraftingProcess)processes[current];
            for (int previous = 0; previous < current; previous++)
            {
                CompiledCraftingProcess existing = (CompiledCraftingProcess)processes[previous];
                if (IRecipe.Equals(existing.Recipe, candidate.Recipe))
                    throw new InvalidOperationException(
                        $"Processes '{existing.Id}' and '{candidate.Id}' compile to equivalent recipes.");
            }
        }
    }
}
