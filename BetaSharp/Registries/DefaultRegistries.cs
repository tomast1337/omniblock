using BetaSharp.Blocks;
using BetaSharp.Blocks.Entities;
using BetaSharp.Blocks.Materials;
using BetaSharp.Diagnostics;
using BetaSharp.Entities;
using BetaSharp.Items;
using BetaSharp.Registries.Data;
using BetaSharp.Rules;
using BetaSharp.Worlds.Generation.Biomes;

namespace BetaSharp.Registries;

public static class DefaultRegistries
{
    public static readonly IRegistry<EntityType> EntityTypes =
        new IndexedRegistry<EntityType>(ResourceLocation.Parse("entity_types"));

    public static readonly IRegistry<Biome> Biomes =
        new IndexedRegistry<Biome>(ResourceLocation.Parse("biomes"));

    public static readonly IRegistry<BlockEntityType> BlockEntityTypes =
        new IndexedRegistry<BlockEntityType>(ResourceLocation.Parse("block_entity_types"));

    //TODO: Implement this properly
    public static readonly IRegistry<IGameRule> GameRules =
        new IndexedRegistry<IGameRule>(ResourceLocation.Parse("game_rules"));

    public static readonly IndexedRegistry<ItemDefinition> Items =
        new IndexedRegistry<ItemDefinition>(ResourceLocation.Parse("items"));

    public static void Initialize()
    {
        // Must load before BlockRegistry.Initialize() — blocks resolve their Material and
        // SoundGroup by name during construction.
        MaterialRegistry.Initialize();
        SoundGroupRegistry.Initialize();

        var toolMaterialBootLoader = new DataAssetLoader<ToolMaterialDefinition>(RegistryDefinitions.ToolMaterials.AssetPath, LoadLocations.Assets, allowUnhandled: false);
        toolMaterialBootLoader.LoadFromPaths(null, null, null);
        var armorMaterialBootLoader = new DataAssetLoader<ArmorMaterialDefinition>(RegistryDefinitions.ArmorMaterials.AssetPath, LoadLocations.Assets, allowUnhandled: false);
        armorMaterialBootLoader.LoadFromPaths(null, null, null);
        if (toolMaterialBootLoader.HasErrors || armorMaterialBootLoader.HasErrors)
        {
            throw new AssetLoadException(toolMaterialBootLoader.FirstErrorMessage ?? armorMaterialBootLoader.FirstErrorMessage ?? "Failed to load material definitions.");
        }

        ToolMaterialRegistry.LoadFrom(toolMaterialBootLoader);
        ArmorMaterialRegistry.LoadFrom(armorMaterialBootLoader);

        var itemBootLoader = new ItemDefinitionJsonLoader(RegistryDefinitions.Items.AssetPath, LoadLocations.Assets);
        itemBootLoader.LoadFromPaths(null, null, null);
        if (itemBootLoader.HasErrors)
        {
            throw new AssetLoadException(itemBootLoader.FirstErrorMessage ?? "Failed to load item definitions.");
        }

        foreach (ItemDefinition definition in itemBootLoader)
        {
            Items.Register(definition.ProtocolId, new ResourceLocation(definition.Namespace, definition.Name), definition);
            Item.Items[definition.ProtocolId] = ItemFactory.Create(definition);
        }

        foreach (ItemDefinition definition in itemBootLoader)
        {
            ItemFactory.ResolveCrossReferences(definition);
        }

        // Now safe: items are fully loaded, so loot-table/behavior lookups by item name inside
        // BlockRegistry.Initialize() will succeed. BlockRegistry, in turn, must run before Stats
        // below — Achievements references specific blocks by name.
        BlockRegistry.Initialize();

        Stats.Stats.InitializeItemStats();
        Stats.Stats.InitializeExtendedItemStats();

        // Must precede the Bootstrap below: EntityRegistry's static fields resolve each mob's
        // EntityDefinition from here as they run, and touching the class is what triggers them.
        EntityDefinitionRegistry.Initialize();

        EntityTypes.Bootstrap(typeof(EntityRegistry));
        Biomes.Bootstrap(typeof(Biome));

        // After both registries above: every spawn entry names an entity type that must already exist.
        var biomeSpawnLoader = new DataAssetLoader<BiomeSpawnDefinition>(RegistryDefinitions.BiomeSpawns.AssetPath, LoadLocations.Assets, allowUnhandled: false);
        biomeSpawnLoader.LoadFromPaths(null, null, null);
        if (biomeSpawnLoader.HasErrors)
        {
            throw new AssetLoadException(biomeSpawnLoader.FirstErrorMessage ?? "Failed to load biome spawn definitions.");
        }

        Biome.LoadSpawnLists(biomeSpawnLoader);
        BlockEntityTypes.Bootstrap(typeof(BlockEntity));

        MetricRegistry.Bootstrap(typeof(ServerMetrics));

        RegistryAccess.AddBuiltIn(RegistryKeys.EntityTypes, EntityTypes);
        RegistryAccess.AddBuiltIn(RegistryKeys.Biomes, Biomes);
        RegistryAccess.AddBuiltIn(RegistryKeys.BlockEntityTypes, BlockEntityTypes);
        RegistryAccess.AddBuiltIn(RegistryKeys.GameRules, GameRules);
        RegistryAccess.AddBuiltIn(RegistryKeys.Items, Items);
        RegistryAccess.AddDynamic(RegistryDefinitions.GameModes);
        RegistryAccess.AddDynamic(RegistryDefinitions.Recipes);
        RegistryAccess.AddDynamic(RegistryDefinitions.ToolMaterials);
        RegistryAccess.AddDynamic(RegistryDefinitions.ArmorMaterials);
        RegistryAccess.AddDynamic(RegistryDefinitions.Items);

        FreezeAll();
    }

    private static void FreezeAll()
    {
        EntityTypes.Freeze();
        Biomes.Freeze();
        BlockEntityTypes.Freeze();
        Items.Freeze();
    }
}
