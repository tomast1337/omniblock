using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Recipes;
using OmniBlock.Rules;
using OmniBlock.Worlds.Generation.Biomes;


namespace OmniBlock.Registries;

/// <summary>
/// Well-known <see cref="RegistryKey{T}"/> constants for all built-in registry types.
/// </summary>
public static class RegistryKeys
{
    public static readonly RegistryKey<EntityType> EntityTypes = new("omniblock:entity_type");
    public static readonly RegistryKey<Biome> Biomes = new("omniblock:biome");
    public static readonly RegistryKey<BiomeSpawnDefinition> BiomeSpawns = new("omniblock:biome_spawn");
    public static readonly RegistryKey<BlockEntityType> BlockEntityTypes = new("omniblock:block_entity_type");
    public static readonly RegistryKey<BlockDefinition> Blocks = new("omniblock:block");
    public static readonly RegistryKey<IGameRule> GameRules = new("omniblock:game_rule");
    public static readonly RegistryKey<GameMode> GameModes = new("omniblock:game_mode");
    public static readonly RegistryKey<RecipeDefinition> Recipes = new("omniblock:recipe");
    public static readonly RegistryKey<ItemDefinition> Items = new("omniblock:item");
    public static readonly RegistryKey<EntityDefinition> Entities = new("omniblock:entity");
    public static readonly RegistryKey<ToolMaterialDefinition> ToolMaterials = new("omniblock:item_material");
    public static readonly RegistryKey<ArmorMaterialDefinition> ArmorMaterials = new("omniblock:armor_material");
    public static readonly RegistryKey<MaterialDefinition> Materials = new("omniblock:material");
    public static readonly RegistryKey<SoundGroupDefinition> SoundGroups = new("omniblock:sound_group");
}
