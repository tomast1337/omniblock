using BetaSharp.Items;
using BetaSharp.Recipes;

namespace BetaSharp.Registries;

internal static class RegistryDefinitions
{
    public static readonly RegistryDefinition<GameMode> GameModes =
        new(RegistryKeys.GameModes, "gamemode");

    public static readonly RegistryDefinition<RecipeDefinition> Recipes =
        new(RegistryKeys.Recipes, "recipe");

    public static readonly RegistryDefinition<ItemDefinition> Items =
        new(RegistryKeys.Items, "item", loaderFactory: (path, locations) => new ItemDefinitionJsonLoader(path, locations));
}
