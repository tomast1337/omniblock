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
    private static readonly EntityType[] s_mobTypes =
    [
        EntityRegistry.Creeper, EntityRegistry.Skeleton, EntityRegistry.Spider, EntityRegistry.Giant,
        EntityRegistry.Zombie, EntityRegistry.Slime, EntityRegistry.Ghast, EntityRegistry.PigZombie,
        EntityRegistry.Pig, EntityRegistry.Sheep, EntityRegistry.Cow, EntityRegistry.Chicken,
        EntityRegistry.Squid, EntityRegistry.Wolf
    ];

    private static readonly EntityType[] s_nonMobTypes =
    [
        EntityRegistry.Arrow, EntityRegistry.Snowball, EntityRegistry.Item, EntityRegistry.Painting,
        EntityRegistry.PrimedTnt, EntityRegistry.FallingSand, EntityRegistry.Minecart, EntityRegistry.Boat,
        EntityRegistry.Egg, EntityRegistry.Fireball, EntityRegistry.FishHook, EntityRegistry.LightningBolt,
        EntityRegistry.Player
    ];

    [Fact]
    public void Every_mob_type_carries_a_definition()
    {
        Assert.All(s_mobTypes, type => Assert.NotNull(type.Definition));
    }

    [Fact]
    public void Non_mob_types_carry_no_definition_and_say_so_clearly()
    {
        Assert.All(s_nonMobTypes, type => Assert.Null(type.Definition));

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => EntityRegistry.Arrow.RequireDefinition());
        Assert.Contains("Arrow", error.Message);
    }

    [Fact]
    public void Constructed_mobs_use_the_registered_definition_instance()
    {
        FakeWorldContext world = new();

        // Reference equality, not value equality: this is what proves the mob reads through the
        // registry, so replacing the registered definition in Phase 4 actually reaches it.
        Assert.Same(EntityRegistry.Zombie.Definition, new EntityZombie(world).Definition);
        Assert.Same(EntityRegistry.Wolf.Definition, new EntityWolf(world).Definition);
        Assert.Same(EntityRegistry.Ghast.Definition, new EntityGhast(world).Definition);
        Assert.Same(EntityRegistry.PigZombie.Definition, new EntityPigZombie(world).Definition);
    }

    [Fact]
    public void Every_registered_protocol_id_fits_in_a_signed_byte()
    {
        IRegistry<EntityType> registry = DefaultRegistries.EntityTypes;

        foreach (EntityType type in s_mobTypes.Concat(s_nonMobTypes))
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
            [EntityRegistry.Creeper] = 50,
            [EntityRegistry.Skeleton] = 51,
            [EntityRegistry.Spider] = 52,
            [EntityRegistry.Giant] = 53,
            [EntityRegistry.Zombie] = 54,
            [EntityRegistry.Slime] = 55,
            [EntityRegistry.Ghast] = 56,
            [EntityRegistry.PigZombie] = 57,
            [EntityRegistry.Pig] = 90,
            [EntityRegistry.Sheep] = 91,
            [EntityRegistry.Cow] = 92,
            [EntityRegistry.Chicken] = 93,
            [EntityRegistry.Squid] = 94,
            [EntityRegistry.Wolf] = 95
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

        Assert.IsType<EntityZombie>(byName);
        Assert.IsType<EntityZombie>(byRawId);
        Assert.Equal(54, EntityRegistry.GetRawId(byName));
        Assert.Equal("zombie", EntityRegistry.GetId(byRawId));
    }
}
