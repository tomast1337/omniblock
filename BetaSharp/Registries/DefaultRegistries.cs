using BetaSharp.Blocks.Entities;
using BetaSharp.Diagnostics;
using BetaSharp.Entities;
using BetaSharp.Items;
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

    /// <summary>
    /// Item definition registry. Each <see cref="ItemDefinition"/> registers itself here as a
    /// side effect of <see cref="ItemFactory.Create"/>, so the static fields on <see cref="Item"/>
    /// remain the single source of truth — there is no separate, duplicated registration list.
    /// </summary>
    public static readonly IndexedRegistry<ItemDefinition> Items =
        new IndexedRegistry<ItemDefinition>(ResourceLocation.Parse("items"));

    public static void Initialize()
    {
        EntityTypes.Bootstrap(typeof(EntityRegistry));
        Biomes.Bootstrap(typeof(Biome));
        BlockEntityTypes.Bootstrap(typeof(BlockEntity));

        MetricRegistry.Bootstrap(typeof(ServerMetrics));

        // Force Item's static field initializers to run now, so every ItemDefinition has
        // registered into Items before it gets frozen below.
        _ = Item.Stick.id;

        RegistryAccess.AddBuiltIn(RegistryKeys.EntityTypes, EntityTypes);
        RegistryAccess.AddBuiltIn(RegistryKeys.Biomes, Biomes);
        RegistryAccess.AddBuiltIn(RegistryKeys.BlockEntityTypes, BlockEntityTypes);
        RegistryAccess.AddBuiltIn(RegistryKeys.GameRules, GameRules);
        RegistryAccess.AddBuiltIn(RegistryKeys.Items, Items);
        RegistryAccess.AddDynamic(RegistryDefinitions.GameModes);
        RegistryAccess.AddDynamic(RegistryDefinitions.Recipes);

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
