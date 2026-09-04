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
        TestEntityCatalog.ByName("creeper"), TestEntityCatalog.ByName("skeleton"), TestEntityCatalog.ByName("spider"), TestEntityCatalog.ByName("giant"),
        TestEntityCatalog.ByName("zombie"), TestEntityCatalog.ByName("slime"), TestEntityCatalog.ByName("ghast"), TestEntityCatalog.ByName("pigzombie"),
        TestEntityCatalog.ByName("pig"), TestEntityCatalog.ByName("sheep"), TestEntityCatalog.ByName("cow"), TestEntityCatalog.ByName("chicken"),
        TestEntityCatalog.ByName("squid"), TestEntityCatalog.ByName("wolf")
    ];

    /// <summary>
    /// Non-living entities that are nevertheless fully described by a JSON definition. They carry a
    /// definition but no spawn category, so they are excluded from the mob-only assertions (living
    /// base class, spawn budgets).
    /// </summary>
    private static readonly EntityType[] s_definedObjectTypes =
    [
        TestEntityCatalog.ByName("primedtnt"), TestEntityCatalog.ByName("fallingsand"), TestEntityCatalog.ByName("lightningbolt"),
        TestEntityCatalog.ByName("item"), TestEntityCatalog.ByName("snowball"), TestEntityCatalog.ByName("egg"),
        TestEntityCatalog.ByName("fireball"), TestEntityCatalog.ByName("arrow"), TestEntityCatalog.ByName("painting"),
        TestEntityCatalog.ByName("fishhook"), TestEntityCatalog.ByName("boat"), TestEntityCatalog.ByName("minecart")
    ];

    /// <summary>Only the player is left: every other entity in the game is now described by JSON.</summary>
    private static readonly EntityType[] s_nonMobTypes =
    [
        TestEntityCatalog.ByName("player")
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

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => TestEntityCatalog.ByName("player").RequireDefinition());
        Assert.Contains("Player", error.Message);
    }

    [Fact]
    public void Constructed_mobs_use_the_registered_definition_instance()
    {
        FakeWorldContext world = new();

        // Reference equality, not value equality: this is what proves the mob reads through the
        // registry, so replacing the registered definition actually reaches it.
        Assert.Same(TestEntityCatalog.ByName("zombie").Definition, ((EntityCreature)TestEntityCatalog.ByName("zombie").Create(world)).Definition);
        Assert.Same(TestEntityCatalog.ByName("wolf").Definition, ((EntityLiving)TestEntityCatalog.ByName("wolf").Create(world)).Definition);
        Assert.Same(TestEntityCatalog.ByName("ghast").Definition, ((EntityLiving)TestEntityCatalog.ByName("ghast").Create(world)).Definition);
        Assert.Same(TestEntityCatalog.ByName("pigzombie").Definition, ((EntityCreature)TestEntityCatalog.ByName("pigzombie").Create(world)).Definition);
    }

    /// <summary>
    /// Biome spawn lists reference entities too.
    /// <c>Biome</c>'s spawn lists and <c>NaturalSpawner.Monsters</c> construct mobs through raw
    /// <c>w =&gt; new EntityXxx(w)</c> lambdas that never touch <see cref="TestEntityCatalog"/>, so they
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

        Assert.Equal(CreatureKind.WaterCreatureCategory, TestEntityCatalog.ByName("squid").RequireDefinition().SpawnCategory);
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
        RuntimeEntityTypeRegistry registry = ContentRuntime.Current.EntityTypes;

        foreach (EntityType type in MobTypes.Concat(s_definedObjectTypes).Concat(s_nonMobTypes))
        {
            int rawId = registry.GetProtocolId(type);
            Assert.InRange(rawId, sbyte.MinValue, sbyte.MaxValue);
            Assert.Equal(rawId, (sbyte)rawId);
        }
    }

    [Fact]
    public void Protocol_ids_keep_their_vanilla_values()
    {
        RuntimeEntityTypeRegistry registry = ContentRuntime.Current.EntityTypes;
        Dictionary<EntityType, int> expected = new()
        {
            [TestEntityCatalog.ByName("creeper")] = 50,
            [TestEntityCatalog.ByName("skeleton")] = 51,
            [TestEntityCatalog.ByName("spider")] = 52,
            [TestEntityCatalog.ByName("giant")] = 53,
            [TestEntityCatalog.ByName("zombie")] = 54,
            [TestEntityCatalog.ByName("slime")] = 55,
            [TestEntityCatalog.ByName("ghast")] = 56,
            [TestEntityCatalog.ByName("pigzombie")] = 57,
            [TestEntityCatalog.ByName("pig")] = 90,
            [TestEntityCatalog.ByName("sheep")] = 91,
            [TestEntityCatalog.ByName("cow")] = 92,
            [TestEntityCatalog.ByName("chicken")] = 93,
            [TestEntityCatalog.ByName("squid")] = 94,
            [TestEntityCatalog.ByName("wolf")] = 95
        };

        foreach ((EntityType type, int rawId) in expected)
        {
            Assert.Equal(rawId, registry.GetProtocolId(type));
        }
    }

    [Fact]
    public void Lookup_by_name_and_by_raw_id_resolve_the_same_type()
    {
        FakeWorldContext world = new();

        Assert.True(TestEntityCatalog.TryCreate("zombie", world, out Entity byName));
        Assert.True(TestEntityCatalog.TryCreate(54, world, out Entity byRawId));

        // Both routes land on the same registered type, though neither has a class of its own.
        Assert.Same(TestEntityCatalog.ByName("zombie"), byName.Type);
        Assert.Same(TestEntityCatalog.ByName("zombie"), byRawId.Type);
        Assert.Equal(54, TestEntityCatalog.GetRawId(byName));
        Assert.Equal("zombie", TestEntityCatalog.GetId(byRawId));
    }
}
