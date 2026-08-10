using System.Collections.Generic;
using System.Linq;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Registries;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Covers how a mob's configuration is reached: through <see cref="EntityType.Definition"/> rather
/// than a static field, with protocol ids inside the signed byte the spawn packets transmit.
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

    /// <summary>
    /// Non-living entities that are nevertheless fully described by a JSON definition. They carry a
    /// definition but no spawn category, so they are excluded from the mob-only assertions (living
    /// base class, spawn budgets).
    /// </summary>
    private static readonly EntityType[] s_definedObjectTypes =
    [
        EntityRegistry.ByName("primedtnt"), EntityRegistry.ByName("fallingsand"), EntityRegistry.ByName("lightningbolt"),
        EntityRegistry.ByName("item"), EntityRegistry.ByName("snowball"), EntityRegistry.ByName("egg"),
        EntityRegistry.ByName("fireball"), EntityRegistry.ByName("arrow"), EntityRegistry.ByName("painting"),
        EntityRegistry.ByName("fishhook"), EntityRegistry.ByName("boat"), EntityRegistry.ByName("minecart")
    ];

    /// <summary>Only the player is left: every other entity in the game is now described by JSON.</summary>
    private static readonly EntityType[] s_nonMobTypes =
    [
        EntityRegistry.ByName("player")
    ];

    [Fact]
    public void Every_mob_type_carries_a_definition()
    {
        Assert.All(MobTypes, type => Assert.NotNull(type.Definition));
    }

    /// <summary>
    /// Defined objects are the same deal without a living body: configuration and behaviors from
    /// JSON, no spawn category (nothing spawns them naturally), and no mob class.
    /// </summary>
    [Fact]
    public void Defined_object_types_carry_a_definition_but_no_spawn_category()
    {
        Assert.All(s_definedObjectTypes, type =>
        {
            Assert.NotNull(type.Definition);
            Assert.Equal("", type.RequireDefinition().SpawnCategory);
        });
    }

    [Fact]
    public void Non_mob_types_carry_no_definition_and_say_so_clearly()
    {
        Assert.All(s_nonMobTypes, type => Assert.Null(type.Definition));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => EntityRegistry.ByName("player").RequireDefinition());
        Assert.Contains("Player", error.Message);
    }

    [Fact]
    public void Constructed_mobs_use_the_registered_definition_instance()
    {
        FakeWorldContext world = new();

        // Reference equality, not value equality: this is what proves the mob reads through the
        // registry, so replacing the registered definition actually reaches it.
        Assert.Same(EntityRegistry.ByName("zombie").Definition, ((EntityCreature)EntityRegistry.ByName("zombie").Create(world)).Definition);
        Assert.Same(EntityRegistry.ByName("wolf").Definition, ((EntityLiving)EntityRegistry.ByName("wolf").Create(world)).Definition);
        Assert.Same(EntityRegistry.ByName("ghast").Definition, ((EntityLiving)EntityRegistry.ByName("ghast").Create(world)).Definition);
        Assert.Same(EntityRegistry.ByName("pigzombie").Definition, ((EntityCreature)EntityRegistry.ByName("pigzombie").Create(world)).Definition);
    }

    /// <summary>
    /// Biome spawn lists reference entities too.
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
    /// Spawn category is declared, not inherited: no class in the hierarchy names one any more, so
    /// the only thing left to check it against is what the mob is composed of. A farm animal carries
    /// the grazing rules and a monster the hostile ones, and each must agree with the category its
    /// JSON declares. The squid is checked by name because nothing but its definition says it lives
    /// in water.
    /// </summary>
    [Fact]
    public void Spawn_category_agrees_with_the_behaviors_a_mob_carries()
    {
        string[] known = [CreatureKind.MonsterCategory, CreatureKind.CreatureCategory, CreatureKind.WaterCreatureCategory];

        foreach (EntityType type in MobTypes)
        {
            string category = type.RequireDefinition().SpawnCategory;
            Assert.Contains(category, known);

            if (type.Behaviors.Find<GrazingAnimalBehavior>() is not null)
            {
                Assert.Equal(CreatureKind.CreatureCategory, category);
            }

            if (type.Behaviors.Find<HostileMonsterBehavior>() is not null)
            {
                Assert.Equal(CreatureKind.MonsterCategory, category);
            }
        }

        Assert.Equal(CreatureKind.WaterCreatureCategory, EntityRegistry.ByName("squid").RequireDefinition().SpawnCategory);
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

        // No class left to count by, so every category is counted against what the definitions
        // declare, which is what proves the manager buckets a mob by its own category rather than by
        // anything it inherits.
        foreach (string category in new[] { CreatureKind.CreatureCategory, CreatureKind.MonsterCategory, CreatureKind.WaterCreatureCategory })
        {
            Assert.Equal(
                MobTypes.Count(type => type.RequireDefinition().SpawnCategory == category),
                world.Entities.CountEntitiesInCategory(category));
        }
    }

    [Fact]
    public void Every_registered_protocol_id_fits_in_a_signed_byte()
    {
        IRegistry<EntityType> registry = DefaultRegistries.EntityTypes;

        foreach (EntityType type in MobTypes.Concat(s_definedObjectTypes).Concat(s_nonMobTypes))
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
