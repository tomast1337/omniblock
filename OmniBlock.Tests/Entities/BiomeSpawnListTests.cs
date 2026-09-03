using System.Linq;
using OmniBlock;
using OmniBlock.Entities;
using OmniBlock.Registries;
using OmniBlock.Worlds.Generation.Biomes;
using OmniBlock.Registries.Data;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Equivalence proof for moving biome spawn lists out of C# constructors and into
/// <c>assets/biome_spawn/*.json</c>: the expected values here are transcribed from the hardcoded
/// lists that <c>Biome</c> and its subclasses used to build.
/// </summary>
[Collection("EntityTests")]
public sealed class BiomeSpawnListTests
{
    private static readonly (string Mob, int Weight)[] s_baseMonsters =
        [("spider", 10), ("zombie", 10), ("skeleton", 10), ("creeper", 10), ("slime", 10)];

    private static readonly (string Mob, int Weight)[] s_baseCreatures =
        [("sheep", 12), ("pig", 10), ("chicken", 10), ("cow", 8)];

    private static readonly (string Mob, int Weight)[] s_baseWater = [("squid", 10)];

    /// <summary>
    ///     Identifies each spawned mob by its registry id rather than its C# class. The class stopped
    ///     being an identity once several registered types began sharing one — a cow and a sheep are
    ///     both an <c>EntityCreature</c> — and the id is what the biome JSON names in the first place.
    /// </summary>
    private static (string Mob, int Weight)[] Actual(Biome biome, CreatureKind kind)
    {
        FakeWorldContext world = new();
        return biome.GetSpawnableList(kind).Entries
            .Select(e => (EntityRegistry.GetId(e.Item.Factory(world))!, e.Weight))
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
        AssertLists(Get(biomeName), s_baseMonsters, [.. s_baseCreatures, ("wolf", 2)], s_baseWater);
    }

    [Fact]
    public void Sky_spawns_only_chickens()
    {
        AssertLists(Get("sky"), [], [("chicken", 10)], []);
    }

    [Fact]
    public void Hell_spawns_only_ghasts_and_zombie_pigmen()
    {
        AssertLists(Get("hell"), [("ghast", 10), ("pigzombie", 10)], [], []);
    }

    [Fact]
    public void Every_spawned_mob_belongs_to_the_list_it_was_declared_in()
    {
        FakeWorldContext world = new();

        foreach (ResourceLocation key in OmniBlock.Registries.DefaultRegistries.Biomes.Keys)
        {
            Biome biome = OmniBlock.Registries.DefaultRegistries.Biomes.GetOrThrow(key);

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

    [Fact]
    public void Unknown_spawn_entry_identifies_the_referenced_entity_before_publication()
    {
        var invalid = new BiomeSpawnDefinition
        {
            Name = "sky",
            Creatures = [new BiomeSpawnEntry("example:missing_mob", 10)]
        };

        try
        {
            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                Biome.LoadSpawnLists([invalid], ContentRuntime.Current.EntityTypes));
            Assert.Contains("example:missing_mob", error.Message);
        }
        finally
        {
            var shipped = new DataAssetLoader<BiomeSpawnDefinition>(
                RegistryDefinitions.BiomeSpawns.AssetPath, LoadLocations.Assets, allowUnhandled: false);
            shipped.LoadFromPaths(null, null, null);
            Assert.False(shipped.HasErrors, shipped.FirstErrorMessage);
            Biome.LoadSpawnLists(shipped, ContentRuntime.Current.EntityTypes);
        }
    }

    private static Biome Get(string name) =>
        OmniBlock.Registries.DefaultRegistries.Biomes.GetOrThrow(ResourceLocation.Parse(name));
}
