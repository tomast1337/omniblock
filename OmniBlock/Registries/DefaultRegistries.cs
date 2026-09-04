using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;
using OmniBlock.Blocks.Materials;
using OmniBlock.Diagnostics;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Processes;
using OmniBlock.Registries.Data;
using OmniBlock.Rules;
using OmniBlock.Worlds.Generation.Biomes;

namespace OmniBlock.Registries;

public static class DefaultRegistries
{
    public static readonly IRegistry<Biome> Biomes =
        new IndexedRegistry<Biome>(ResourceLocation.Parse("biomes"));

    public static readonly IRegistry<BlockEntityType> BlockEntityTypes =
        new IndexedRegistry<BlockEntityType>(ResourceLocation.Parse("block_entity_types"));

    //TODO: Implement this properly
    public static readonly IRegistry<IGameRule> GameRules =
        new IndexedRegistry<IGameRule>(ResourceLocation.Parse("game_rules"));

    public static void Initialize(ContentRuntimeBuilder content)
    {
        ArgumentNullException.ThrowIfNull(content);
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

        foreach (ItemDefinition definition in ContentIdAllocator.AssignItemIds(itemBootLoader))
        {
            content.AddItemDefinition(definition);
        }

        // The builder creates all drafts first, validates behavior/crafting references in a second
        // pass, freezes the complete item catalog, and only then exposes the transitional array.
        content.BuildItemsForBootstrap();

        // Now safe: items are fully loaded, so loot-table/behavior lookups by item name inside
        // BlockRegistry.Initialize() will succeed. BlockRegistry, in turn, must run before Stats
        // below — Achievements references specific blocks by name.
        BlockRegistry.Initialize(content);

        Stats.Stats.InitializeItemStats(content);
        Stats.Stats.InitializeExtendedItemStats(content);

        var entityLoader = new EntityDefinitionJsonLoader(RegistryDefinitions.Entities.AssetPath, LoadLocations.Assets);
        entityLoader.LoadFromPaths(null, null, null);
        if (entityLoader.HasErrors)
            throw new AssetLoadException(entityLoader.FirstErrorMessage ?? "Failed to load entity definitions.");
        foreach (EntityDefinition definition in entityLoader) content.AddEntityDefinition(definition);

        // Blocks and entity definitions now exist, so every item cross-reference can be resolved
        // and the item catalog frozen before entity constructors consume item behaviors.
        content.FinalizeItemsForBootstrap();
        content.BuildEntitiesForBootstrap();

        // Compile the built-in process catalog as part of the same atomic content snapshot. The
        // legacy dynamic recipe registry remains registered below until its runtime consumers move.
        var processLoader = new DataAssetLoader<ProcessDefinition>("recipe", LoadLocations.Assets, allowUnhandled: false);
        processLoader.LoadFromPaths(null, null, null);
        if (processLoader.HasErrors)
            throw new AssetLoadException(processLoader.FirstErrorMessage ?? "Failed to load process definitions.");
        foreach (ProcessDefinition definition in processLoader) content.AddProcessDefinition(definition);

        Biomes.Bootstrap(typeof(Biome));

        // After both registries above: every spawn entry names an entity type that must already exist.
        var biomeSpawnLoader = new DataAssetLoader<BiomeSpawnDefinition>(RegistryDefinitions.BiomeSpawns.AssetPath, LoadLocations.Assets, allowUnhandled: false);
        biomeSpawnLoader.LoadFromPaths(null, null, null);
        if (biomeSpawnLoader.HasErrors)
        {
            throw new AssetLoadException(biomeSpawnLoader.FirstErrorMessage ?? "Failed to load biome spawn definitions.");
        }

        Biome.LoadSpawnLists(biomeSpawnLoader, content);
        BlockEntityTypes.Bootstrap(typeof(BlockEntity));

        MetricRegistry.Bootstrap(typeof(ServerMetrics));

        RegistryAccess.AddBuiltIn(RegistryKeys.Biomes, Biomes);
        RegistryAccess.AddBuiltIn(RegistryKeys.BlockEntityTypes, BlockEntityTypes);
        RegistryAccess.AddBuiltIn(RegistryKeys.GameRules, GameRules);
        RegistryAccess.AddDynamic(RegistryDefinitions.GameModes);
        RegistryAccess.AddDynamic(RegistryDefinitions.Recipes);
        RegistryAccess.AddDynamic(RegistryDefinitions.ToolMaterials);
        RegistryAccess.AddDynamic(RegistryDefinitions.ArmorMaterials);
        RegistryAccess.AddDynamic(RegistryDefinitions.Items);

        FreezeAll();
    }

    private static void FreezeAll()
    {
        Biomes.Freeze();
        BlockEntityTypes.Freeze();
    }
}
