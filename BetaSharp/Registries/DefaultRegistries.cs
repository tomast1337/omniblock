using BetaSharp.Blocks;
using BetaSharp.Blocks.Entities;
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
        _ = Block.Stone.id;

        string assetsPath = Path.Combine(AppContext.BaseDirectory, "assets");
        ToolMaterialRegistry.LoadFrom(assetsPath);
        ArmorMaterialRegistry.LoadFrom(assetsPath);

        var itemBootLoader = new ItemDefinitionJsonLoader(RegistryDefinitions.Items.AssetPath, LoadLocations.Assets);
        itemBootLoader.LoadFromPaths(null, null, null);
        if (itemBootLoader.HasErrors)
        {
            throw new AssetLoadException(itemBootLoader.FirstErrorMessage ?? "Failed to load item definitions.");
        }

        foreach (ItemDefinition definition in itemBootLoader)
        {
            Items.Register(definition.ProtocolId, new ResourceLocation(definition.Namespace, definition.Name), definition);
            Item.ITEMS[definition.ProtocolId] = ItemFactory.Create(definition);
        }

        Stats.Stats.InitializeItemStats();
        Stats.Stats.InitializeExtendedItemStats();

        EntityTypes.Bootstrap(typeof(EntityRegistry));
        Biomes.Bootstrap(typeof(Biome));
        BlockEntityTypes.Bootstrap(typeof(BlockEntity));

        MetricRegistry.Bootstrap(typeof(ServerMetrics));

        RegistryAccess.AddBuiltIn(RegistryKeys.EntityTypes, EntityTypes);
        RegistryAccess.AddBuiltIn(RegistryKeys.Biomes, Biomes);
        RegistryAccess.AddBuiltIn(RegistryKeys.BlockEntityTypes, BlockEntityTypes);
        RegistryAccess.AddBuiltIn(RegistryKeys.GameRules, GameRules);
        RegistryAccess.AddBuiltIn(RegistryKeys.Items, Items);
        RegistryAccess.AddDynamic(RegistryDefinitions.GameModes);
        RegistryAccess.AddDynamic(RegistryDefinitions.Recipes);
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
