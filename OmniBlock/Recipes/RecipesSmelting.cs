using OmniBlock.Items;
using OmniBlock.Registries;

namespace OmniBlock.Recipes;

public class SmeltingCraftingRegistry : ICraftingRegistry
{
    internal const string Name = "smelting";
    string ICraftingRegistry.Name => Name;
    public int Count => RecipesSmelting.Recipes.Count;
    public void Clear() => RecipesSmelting.Recipes.Clear();
    public void BuildRecipe(RecipeDefinition def, RuntimeItemRegistry items) => RecipesSmelting.BuildSmeltRecipe(def, items);
}

public static class RecipesSmelting
{
    public static Dictionary<int, ItemStack> Recipes { get; } = [];

    public static void BuildSmeltRecipe(RecipeDefinition def, RuntimeItemRegistry items)
    {
        if (string.IsNullOrEmpty(def.Input))
            throw new InvalidOperationException("Smelting recipe has no input.");

        if (!items.TryParse(def.Input, out ItemStack? input))
            throw new InvalidOperationException($"Unknown input '{def.Input}'.");

        if (!items.TryParse(def.Result.Id, out ItemStack? output, def.Result.Count))
            throw new InvalidOperationException($"Unknown result '{def.Result.Id}'.");

        if (!Recipes.TryAdd(input.ItemId, output))
            throw new OverlappingRecipeException(def.Input, SmeltingCraftingRegistry.Name);
    }

    public static ItemStack? Craft(int inputId)
    {
        Recipes.TryGetValue(inputId, out ItemStack? result);
        return result;
    }
}
