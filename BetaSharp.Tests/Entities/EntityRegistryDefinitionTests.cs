using System.Collections.Generic;
using System.Linq;
using BetaSharp.Entities;
using BetaSharp.Registries;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers Phase 3 of the mob data-driven migration: every mob's configuration is reached through
/// <see cref="EntityType.Definition"/> rather than a static field, and protocol ids stay inside the
/// signed byte the spawn packets transmit.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityRegistryDefinitionTests
{
    /// <summary>Shared with <see cref="EntityJsonDumperTests"/>, which dumps exactly these.</summary>
    internal static readonly EntityType[] MobTypes =
    [
        EntityRegistry.ByName("creeper"), EntityRegistry.ByName("skeleton"), EntityRegistry.ByName("spider"), EntityRegistry.ByName("giant"),
        EntityRegistry.ByName("zombie"), EntityRegistry.ByName("slime"), EntityRegistry.ByName("ghast"), EntityRegistry.ByName("pigzombie"),
        EntityRegistry.ByName("pig"), EntityRegistry.ByName("sheep"), EntityRegistry.ByName("cow"), EntityRegistry.ByName("chicken"),
        EntityRegistry.ByName("squid"), EntityRegistry.ByName("wolf")
    ];

    private static readonly EntityType[] s_nonMobTypes =
    [
        EntityRegistry.ByName("arrow"), EntityRegistry.ByName("snowball"), EntityRegistry.ByName("item"), EntityRegistry.ByName("painting"),
        EntityRegistry.ByName("primedtnt"), EntityRegistry.ByName("fallingsand"), EntityRegistry.ByName("minecart"), EntityRegistry.ByName("boat"),
        EntityRegistry.ByName("egg"), EntityRegistry.ByName("fireball"), EntityRegistry.ByName("fishhook"), EntityRegistry.ByName("lightningbolt"),
        EntityRegistry.ByName("player")
    ];

    [Fact]
    public void Every_mob_type_carries_a_definition()
    {
        Assert.All(MobTypes, type => Assert.NotNull(type.Definition));
    }

    [Fact]
    public void Non_mob_types_carry_no_definition_and_say_so_clearly()
    {
        Assert.All(s_nonMobTypes, type => Assert.Null(type.Definition));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => EntityRegistry.ByName("arrow").RequireDefinition());
        Assert.Contains("Arrow", error.Message);
    }

    [Fact]
    public void Constructed_mobs_use_the_registered_definition_instance()
    {
        FakeWorldContext world = new();

        // Reference equality, not value equality: this is what proves the mob reads through the
        // registry, so replacing the registered definition in Phase 4 actually reaches it.
        Assert.Same(EntityRegistry.ByName("zombie").Definition, ((EntityMonster)EntityRegistry.ByName("zombie").Create(world)).Definition);
        Assert.Same(EntityRegistry.ByName("wolf").Definition, new EntityWolf(world).Definition);
        Assert.Same(EntityRegistry.ByName("ghast").Definition, new EntityGhast(world).Definition);
        Assert.Same(EntityRegistry.ByName("pigzombie").Definition, ((EntityMonster)EntityRegistry.ByName("pigzombie").Create(world)).Definition);
    }

    /// <summary>
    /// The migration doc's "Biome spawn lists reference entities too" gotcha, made executable.
    /// <c>Biome</c>'s spawn lists and <c>NaturalSpawner.Monsters</c> construct mobs through raw
    /// <c>w =&gt; new EntityXxx(w)</c> lambdas that never touch <see cref="EntityRegistry"/>, so they
    /// would not surface a broken definition lookup as a compile error. This exercises that same
    /// direct-construction path for every mob and asserts it resolves the registered definition.
    /// </summary>
    [Fact]
    public void Direct_construction_resolves_definitions_for_every_mob()
    {
        FakeWorldContext world = new();

        foreach (EntityType type in MobTypes)
        {
            Entity entity = type.Create(world);
            EntityLiving mob = Assert.IsAssignableFrom<EntityLiving>(entity);
            Assert.Same(type.Definition, mob.Definition);
        }
    }

    /// <summary>
    /// Equivalence proof for moving spawn category out of the class hierarchy and into JSON:
    /// every mob's declared <see cref="EntityDefinition.SpawnCategory"/> must match the
    /// <c>Monster</c> / <see cref="EntityAnimal"/> / <see cref="EntityWaterMob"/> test that
    /// <c>CreatureKind</c> used to perform. Keeping the old predicate here is what makes this a
    /// comparison rather than a restatement.
    /// </summary>
    [Fact]
    public void Spawn_category_matches_the_type_hierarchy_it_replaced()
    {
        FakeWorldContext world = new();

        foreach (EntityType type in MobTypes)
        {
            Entity entity = type.Create(world);
            string expected = entity switch
            {
                Monster => CreatureKind.MonsterCategory,
                EntityAnimal => CreatureKind.CreatureCategory,
                EntityWaterMob => CreatureKind.WaterCreatureCategory,
                _ => ""
            };

            Assert.Equal(expected, type.RequireDefinition().SpawnCategory);
        }
    }

    [Fact]
    public void Category_counting_matches_counting_by_type()
    {
        FakeWorldContext world = new();
        foreach (EntityType type in MobTypes)
        {
            Entity entity = type.Create(world);
            entity.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
            Assert.True(world.Entities.SpawnEntity(entity));
        }

        Assert.Equal(
            world.Entities.CountEntitiesOfType(typeof(Monster)),
            world.Entities.CountEntitiesInCategory(CreatureKind.MonsterCategory));
        Assert.Equal(
            world.Entities.CountEntitiesOfType(typeof(EntityAnimal)),
            world.Entities.CountEntitiesInCategory(CreatureKind.CreatureCategory));
        Assert.Equal(
            world.Entities.CountEntitiesOfType(typeof(EntityWaterMob)),
            world.Entities.CountEntitiesInCategory(CreatureKind.WaterCreatureCategory));
    }

    [Fact]
    public void Every_registered_protocol_id_fits_in_a_signed_byte()
    {
        IRegistry<EntityType> registry = DefaultRegistries.EntityTypes;

        foreach (EntityType type in MobTypes.Concat(s_nonMobTypes))
        {
            int rawId = registry.GetId(type);
            Assert.InRange(rawId, sbyte.MinValue, sbyte.MaxValue);
            Assert.Equal(rawId, (sbyte)rawId);
        }
    }

    [Fact]
    public void Protocol_ids_keep_their_vanilla_values()
    {
        IRegistry<EntityType> registry = DefaultRegistries.EntityTypes;
        Dictionary<EntityType, int> expected = new()
        {
            [EntityRegistry.ByName("creeper")] = 50,
            [EntityRegistry.ByName("skeleton")] = 51,
            [EntityRegistry.ByName("spider")] = 52,
            [EntityRegistry.ByName("giant")] = 53,
            [EntityRegistry.ByName("zombie")] = 54,
            [EntityRegistry.ByName("slime")] = 55,
            [EntityRegistry.ByName("ghast")] = 56,
            [EntityRegistry.ByName("pigzombie")] = 57,
            [EntityRegistry.ByName("pig")] = 90,
            [EntityRegistry.ByName("sheep")] = 91,
            [EntityRegistry.ByName("cow")] = 92,
            [EntityRegistry.ByName("chicken")] = 93,
            [EntityRegistry.ByName("squid")] = 94,
            [EntityRegistry.ByName("wolf")] = 95
        };

        foreach ((EntityType type, int rawId) in expected)
        {
            Assert.Equal(rawId, registry.GetId(type));
        }
    }

    [Fact]
    public void Lookup_by_name_and_by_raw_id_resolve_the_same_type()
    {
        FakeWorldContext world = new();

        Assert.True(EntityRegistry.TryCreate("zombie", world, out Entity byName));
        Assert.True(EntityRegistry.TryCreate(54, world, out Entity byRawId));

        // Both routes land on the same registered type, though neither has a class of its own.
        Assert.Same(EntityRegistry.ByName("zombie"), byName.Type);
        Assert.Same(EntityRegistry.ByName("zombie"), byRawId.Type);
        Assert.Equal(54, EntityRegistry.GetRawId(byName));
        Assert.Equal("zombie", EntityRegistry.GetId(byRawId));
    }
}
