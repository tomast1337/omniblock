using System.Linq;
using BetaSharp;
using BetaSharp.Entities;
using BetaSharp.Worlds.Generation.Biomes;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Equivalence proof for moving biome spawn lists out of C# constructors and into
/// <c>assets/biome_spawn/*.json</c>: the expected values here are transcribed from the hardcoded
/// lists that <c>Biome</c> and its subclasses used to build.
/// </summary>
[Collection("EntityTests")]
public sealed class BiomeSpawnListTests
{
    private static readonly (string Mob, int Weight)[] s_baseMonsters =
        [("EntitySpider", 10), ("EntityZombie", 10), ("EntitySkeleton", 10), ("EntityCreeper", 10), ("EntitySlime", 10)];

    private static readonly (string Mob, int Weight)[] s_baseCreatures =
        [("EntitySheep", 12), ("EntityPig", 10), ("EntityChicken", 10), ("EntityCow", 8)];

    private static readonly (string Mob, int Weight)[] s_baseWater = [("EntitySquid", 10)];

    private static (string Mob, int Weight)[] Actual(Biome biome, CreatureKind kind)
    {
        FakeWorldContext world = new();
        return biome.GetSpawnableList(kind).Entries
            .Select(e => (e.Item.Factory(world).GetType().Name, e.Weight))
            .ToArray();
    }

    private static void AssertLists(
        Biome biome,
        (string Mob, int Weight)[] monsters,
        (string Mob, int Weight)[] creatures,
        (string Mob, int Weight)[] water)
    {
        Assert.Equal(monsters, Actual(biome, CreatureKind.Monster));
        Assert.Equal(creatures, Actual(biome, CreatureKind.Creature));
        Assert.Equal(water, Actual(biome, CreatureKind.WaterCreature));
    }

    [Theory]
    [InlineData("rainforest")]
    [InlineData("swampland")]
    [InlineData("seasonal_forest")]
    [InlineData("savanna")]
    [InlineData("shrubland")]
    [InlineData("desert")]
    [InlineData("plains")]
    [InlineData("ice_desert")]
    [InlineData("tundra")]
    public void Ordinary_biomes_keep_the_shared_spawn_list(string biomeName)
    {
        AssertLists(Get(biomeName), s_baseMonsters, s_baseCreatures, s_baseWater);
    }

    [Theory]
    [InlineData("forest")]
    [InlineData("taiga")]
    public void Wolf_biomes_add_a_wolf_to_the_shared_creature_list(string biomeName)
    {
        AssertLists(Get(biomeName), s_baseMonsters, [.. s_baseCreatures, ("EntityWolf", 2)], s_baseWater);
    }

    [Fact]
    public void Sky_spawns_only_chickens()
    {
        AssertLists(Get("sky"), [], [("EntityChicken", 10)], []);
    }

    [Fact]
    public void Hell_spawns_only_ghasts_and_zombie_pigmen()
    {
        AssertLists(Get("hell"), [("EntityGhast", 10), ("EntityPigZombie", 10)], [], []);
    }

    [Fact]
    public void Every_spawned_mob_belongs_to_the_list_it_was_declared_in()
    {
        FakeWorldContext world = new();

        foreach (ResourceLocation key in BetaSharp.Registries.DefaultRegistries.Biomes.Keys)
        {
            Biome biome = BetaSharp.Registries.DefaultRegistries.Biomes.Get(key)!;

            foreach (CreatureKind kind in CreatureKind.Values)
            {
                foreach ((SpawnListEntry entry, int _) in biome.GetSpawnableList(kind).Entries)
                {
                    EntityLiving mob = entry.Factory(world);
                    Assert.Equal(kind.Category, mob.Definition.SpawnCategory);
                }
            }
        }
    }

    private static Biome Get(string name) =>
        BetaSharp.Registries.DefaultRegistries.Biomes.Get(ResourceLocation.Parse(name))
        ?? throw new InvalidOperationException($"Unknown biome '{name}'.");
}
