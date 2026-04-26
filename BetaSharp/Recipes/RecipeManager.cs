using BetaSharp.Registries;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Recipes;

public class RecipeManager : IRegistryReloadListener
{
    private static readonly ILogger<RecipeManager> s_logger = Log.Instance.For<RecipeManager>();

    public void OnRegistriesRebuilt(RegistryAccess registryAccess)
    {
        ClearRecipes();
        BuildRecipes(registryAccess.GetOrThrow(RegistryKeys.Recipes));
    }

    private static void BuildRecipes(IReadableRegistry<RecipeDefinition> registry)
    {
        ItemLookup.Initialize();

        foreach (RecipeDefinition def in registry)
        {
            try
            {
                if (string.Equals(def.Type, "shapeless", StringComparison.OrdinalIgnoreCase))
                {
                    RecipesCrafting.BuildShapelessRecipe(def);
                }
                else if (string.Equals(def.Type, "shaped", StringComparison.OrdinalIgnoreCase))
                {
                    RecipesCrafting.BuildShapedRecipe(def);
                }
                else if (string.Equals(def.Type, "smelting", StringComparison.OrdinalIgnoreCase))
                {
                    RecipesSmelting.BuildSmeltRecipe(def);
                }
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "Failed to load recipe '{Name}'", def.Name);
            }
        }

        s_logger.LogInformation("{Count} crafting recipes loaded.", RecipesCrafting.Recipes.Count);
        s_logger.LogInformation("{Count} smelting recipes loaded.", RecipesSmelting.Recipes.Count);
    }

    private static void ClearRecipes()
    {
        RecipesCrafting.Recipes.Clear();
        RecipesSmelting.Recipes.Clear();
    }
}
