using BetaSharp.Blocks.Materials;
using BetaSharp.Items;
using BetaSharp.Recipes;
using BetaSharp.Registries.Data;

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

    // Loaded once at Bootstrap.Initialize() into process-global canonical registries —
    // never per-world, never reloaded: static Block instances cannot re-resolve materials.
    public static readonly RegistryDefinition<MaterialDefinition> Materials =
        new(RegistryKeys.Materials, "material", LoadLocations.AllInit, isReloadable: false, serversideOnly: true);

    public static readonly RegistryDefinition<SoundGroupDefinition> SoundGroups =
        new(RegistryKeys.SoundGroups, "sound_group", LoadLocations.AllInit, isReloadable: false, serversideOnly: true);
}
