using OmniBlock.Blocks.Entities;
using OmniBlock.Server;

namespace OmniBlock.Tests.Worlds;

[Collection(ChunkGeneratorCharacterizationCollection.Name)]
public sealed class WorstSeedEverCompatibilityTests
{
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

    private sealed class FixtureServer(string root, ContentRuntime content)
        : OmniBlockServer(new FixtureConfiguration(), content)
    {
        public bool Initialize()
        {
            DefaultRegistries.RegisterDynamicDefinitions();
            RegistryAccess = RegistryAccess.Build(AppContext.BaseDirectory);
            return Init();
        }

        public override FileInfo GetFile(string path) => new(Path.Combine(root, path));
    }

    private sealed class FixtureConfiguration : IServerConfiguration
    {
        public string GetServerIp(string fallback) => fallback;
        public int GetServerPort(int fallback) => fallback;
        public bool GetDualStack(bool fallback) => fallback;
        public bool GetOnlineMode(bool fallback) => false;
        public bool GetSpawnAnimals(bool fallback) => false;
        public bool GetPvpEnabled(bool fallback) => false;
        public bool GetAllowFlight(bool fallback) => true;
        public string GetLevelName(string fallback) => "worstseedever";
        public string GetLevelType(string fallback) => "default";
        public string GetLevelSeed(string fallback) => "worstseedever";
        public string GetLevelOptions(string fallback) => "";
        public bool GetSpawnMonsters(bool fallback) => false;
        public bool GetAllowNether(bool fallback) => false;
        public int GetMaxPlayers(int fallback) => 1;
        public int GetViewDistance(int fallback) => 4;
        public int GetSimulationDistance(int fallback) => 4;
        public bool GetWhiteList(bool fallback) => false;
        public int GetSpawnRegionSize(int fallback) => 64;
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
