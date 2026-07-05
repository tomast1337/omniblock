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

    public static readonly RegistryDefinition<ToolMaterialDefinition> ToolMaterials =
        new(RegistryKeys.ToolMaterials, "item_material");

    public static readonly RegistryDefinition<ArmorMaterialDefinition> ArmorMaterials =
        new(RegistryKeys.ArmorMaterials, "armor_material");
}
