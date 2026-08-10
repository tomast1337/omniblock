using OmniBlock.Blocks;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Recipes;
using OmniBlock.Registries.Data;
using OmniBlock.Worlds.Generation.Biomes;

namespace OmniBlock.Registries;

internal static class RegistryDefinitions
{
    public static readonly RegistryDefinition<GameMode> GameModes =
        new(RegistryKeys.GameModes, "gamemode");

    public static readonly RegistryDefinition<RecipeDefinition> Recipes =
        new(RegistryKeys.Recipes, "recipe");

    public static readonly RegistryDefinition<ItemDefinition> Items =
        new(RegistryKeys.Items, "item", loaderFactory: (path, locations) => new ItemDefinitionJsonLoader(path, locations));

    public static readonly RegistryDefinition<BiomeSpawnDefinition> BiomeSpawns =
        new(RegistryKeys.BiomeSpawns, "biome_spawn");

    // Boot-time-only for the same reason as Blocks below: EntityType instances are registered once
    // and captured by every spawned mob, so a /reload cannot retroactively re-point them.
    public static readonly RegistryDefinition<EntityDefinition> Entities =
        new(RegistryKeys.Entities, "entity", loaderFactory: (path, locations) => new EntityDefinitionJsonLoader(path, locations));

    // Not registered via RegistryAccess.AddDynamic — same treatment as Materials/SoundGroups
    // below, not Items: blocks are even more hot-path-sensitive (renderer/lighting arrays cache
    // constructed Block instances directly), so this is boot-time-only, no /reload support.
    public static readonly RegistryDefinition<BlockDefinition> Blocks =
        new(RegistryKeys.Blocks, "block", loaderFactory: (path, locations) => new BlockDefinitionJsonLoader(path, locations));

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
