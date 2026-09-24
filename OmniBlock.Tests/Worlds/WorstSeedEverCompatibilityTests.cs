using OmniBlock.Blocks.Entities;
using OmniBlock.Server;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;
using Xunit.Abstractions;

namespace OmniBlock.Tests.Worlds;

[Collection(ChunkGeneratorCharacterizationCollection.Name)]
public sealed class WorstSeedEverCompatibilityTests
{
    private readonly ITestOutputHelper _output;

    public WorstSeedEverCompatibilityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Initial_spawn_search_stops_after_512_unsuitable_steps()
    {
        var first = World.FindInitialSpawnCandidate(new JavaRandom(12345), static (_, _) => false);
        var repeated = World.FindInitialSpawnCandidate(new JavaRandom(12345), static (_, _) => false);

        Assert.Equal(first, repeated);
        Assert.Equal(512, first.Attempts);
        Assert.False(first.Valid);
    }

    [Theory]
    [InlineData("GLACIER", 826164623L, 0,
        "0,64,0|valid=True|0,0:75:2:Shrubland|16,0:75:2:Shrubland|0,16:75:2:Shrubland|-16,0:77:2:Savanna|0,-16:74:2:Savanna")]
    [InlineData("GARGAMEL", -841147678L, 0,
        "0,64,0|valid=True|0,0:65:2:Forest|16,0:63:9:Forest|0,16:63:2:Forest|-16,0:63:2:Forest|0,-16:64:2:Forest")]
    [InlineData("1", 1L, 1,
        "40,64,0|valid=True|0,0:66:12:Desert|16,0:68:2:Savanna|0,16:65:12:Desert|-16,0:63:9:Desert|0,-16:64:12:Desert")]
    public void Historical_seed_fresh_server_spawn_and_nearby_surface_are_stable(
        string seedText, long seed, int expectedSearchAttempts, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-seed-spawn-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var server = new FixtureServer(root, ContentRuntime.Current, seedText, spawnRegionSize: 0);
            Assert.True(server.Initialize());
            var world = server.worlds[0];
            Assert.Equal(seed, world.Seed);
            Assert.Equal(expectedSearchAttempts, world.InitialSpawnSearchAttempts);
            Assert.False(world.InitialSpawnUsedFallback);
            var x = world.Properties.SpawnX;
            var z = world.Properties.SpawnZ;
            var validSpawn = world.Dimension.IsValidSpawnPoint(x, z);

            // These are actual server-world samples, not historical descriptions of the seed.
            var surface = new List<string>();
            foreach (var (dx, dz) in new[] { (0, 0), (16, 0), (0, 16), (-16, 0), (0, -16) })
            {
                var sampleX = x + dx;
                var sampleZ = z + dz;
                world.ChunkCache.LoadChunk(sampleX >> 4, sampleZ >> 4);
                var y = world.Reader.GetTopY(sampleX, sampleZ) - 1;
                var id = world.Reader.GetBlockId(sampleX, y, sampleZ);
                surface.Add($"{dx},{dz}:{y}:{id}:{world.Dimension.BiomeSource.GetBiome(sampleX, sampleZ).Name}");
            }

            var actual = $"{x},{world.Properties.SpawnY},{z}|valid={validSpawn}|{string.Join('|', surface)}";
            _output.WriteLine($"{seedText}: {actual}");
            Assert.Equal(expected, actual);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Fresh_server_world_preserves_the_near_spawn_dungeon_and_loot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-worstseedever-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var server = new FixtureServer(root, ContentRuntime.Current);
            Assert.True(server.Initialize());
            var world = server.worlds[0];

            Assert.Equal(-1446162294L, world.Properties.RandomSeed);
            Assert.Equal((0, 64, 0),
                (world.Properties.SpawnX, world.Properties.SpawnY, world.Properties.SpawnZ));

            var blocks = world.Content.Blocks;
            Assert.Equal(blocks.Get("spawner").Id, world.Reader.GetBlockId(-3, 59, 11));
            Assert.Equal(blocks.Get("chest").Id, world.Reader.GetBlockId(-4, 59, 14));

            var spawner = world.Entities.GetBlockEntity<BlockEntityMobSpawner>(-3, 59, 11);
            var chest = world.Entities.GetBlockEntity<BlockEntityChest>(-4, 59, 14);
            Assert.NotNull(spawner);
            Assert.NotNull(chest);
            Assert.Equal("Skeleton", spawner.GetSpawnedEntityId());

            var loot = Enumerable.Range(0, chest.Size)
                .Where(slot => chest.GetStack(slot) is not null)
                .Select(slot =>
                {
                    var stack = chest.GetStack(slot)!;
                    return $"{slot}:{world.Content.Items.GetName(stack)}x{stack.Count}:{stack.GetDamage()}";
                })
                .ToArray();
            Assert.Equal(
            [
                "3:gunpowderx3:0",
                "13:wheatx2:0",
                "14:saddlex1:0",
                "16:stringx1:0",
                "18:stringx4:0",
                "25:wheatx1:0",
                "26:ingot_ironx3:0"
            ], loot);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class FixtureServer(
        string root, ContentRuntime content, string seed = "worstseedever", int spawnRegionSize = 64)
        : OmniBlockServer(new FixtureConfiguration(seed, spawnRegionSize), content)
    {
        public bool Initialize()
        {
            DefaultRegistries.RegisterDynamicDefinitions();
            RegistryAccess = RegistryAccess.Build(AppContext.BaseDirectory);
            return Init();
        }

        public override FileInfo GetFile(string path) => new(Path.Combine(root, path));
    }

    private sealed class FixtureConfiguration(string seed, int spawnRegionSize) : IServerConfiguration
    {
        public string GetServerIp(string fallback) => fallback;
        public int GetServerPort(int fallback) => fallback;
        public bool GetDualStack(bool fallback) => fallback;
        public bool GetOnlineMode(bool fallback) => false;
        public bool GetSpawnAnimals(bool fallback) => false;
        public bool GetPvpEnabled(bool fallback) => false;
        public bool GetAllowFlight(bool fallback) => true;
        public string GetLevelName(string fallback) => seed;
        public string GetLevelType(string fallback) => "default";
        public string GetLevelSeed(string fallback) => seed;
        public string GetLevelOptions(string fallback) => "";
        public bool GetSpawnMonsters(bool fallback) => false;
        public bool GetAllowNether(bool fallback) => false;
        public int GetMaxPlayers(int fallback) => 1;
        public int GetViewDistance(int fallback) => 4;
        public int GetSimulationDistance(int fallback) => 4;
        public bool GetWhiteList(bool fallback) => false;
        public int GetSpawnRegionSize(int fallback) => spawnRegionSize;
        public string GetDefaultGamemode(string fallback) => fallback;

        public void Save()
        {
        }

        public bool GetProperty(string property, bool fallback) => fallback;
        public int GetProperty(string property, int fallback) => fallback;
        public string GetProperty(string property, string fallback) => fallback;

        public void SetProperty(string property, bool value)
        {
        }
    }
}
