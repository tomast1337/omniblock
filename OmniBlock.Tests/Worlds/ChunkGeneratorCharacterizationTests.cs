using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OmniBlock.Entities;
using OmniBlock.Server.Worlds;
using OmniBlock.Worlds;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Chunks.Storage;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Dimensions;
using OmniBlock.Worlds.Generation;
using OmniBlock.Worlds.Storage;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Tests.Worlds;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ChunkGeneratorCharacterizationCollection
{
    public const string Name = "ChunkGeneratorCharacterization";
}

/// <summary>
///     Compatibility tripwires for the terrain pipeline. These intentionally fingerprint semantic
///     chunk state rather than NBT or region bytes, so storage refactors remain free to change.
/// </summary>
[Collection(ChunkGeneratorCharacterizationCollection.Name)]
public sealed class ChunkGeneratorCharacterizationTests
{
    public static TheoryData<string, long, int, int, string> GoldenTerrain => new()
    {
        { "default", 1L, 0, 0, "4e94085a62c9090fefb8f83c0d26f3341499a50c73e770ba4c0efef8261e62a7" },
        { "default", 987654321L, 12, -7, "7624e29fe1e5b381551048ad8214c072187d2a4fef7b0f55ad2ee87f56063f09" },
        { "flat", 1L, 0, 0, "066c1e0ae098ff0c745d789b5b8bb009e24f2e794b71bba3a2549ed263f9cfee" },
        { "flat", 987654321L, 12, -7, "066c1e0ae098ff0c745d789b5b8bb009e24f2e794b71bba3a2549ed263f9cfee" },
        { "sky", 1L, 0, 0, "c3b4dabdc61e6246be0c237ca5e70917b03ce4b56b2d6ef463c26eb935a062b7" },
        { "sky", 987654321L, 12, -7, "973c0d62712d2f90a42ea13b68913ce9571970a035e61ca94d49688a2d5b8903" },
        { "nether", 1L, 0, 0, "576c8d52b344dd3d7540f934c45b64e12ab5847e51868aa869cb33d672052922" },
        { "nether", 987654321L, 12, -7, "b450e52898dc6184e2df870ca6abe096a83187aef1c1a249942265bfd198bb2a" }
    };

    public static TheoryData<string, long, int, int, string> GoldenCompletedNeighborhoods => new()
    {
        { "default", 246813579L, -33, 31, "3243eec63e6652fdb476fbad9f04049475e90c6afe688aedd696de1cd9fac709" },
        { "flat", 246813579L, -33, 31, "e10fc05d6ac7cbd6e614880cb404a2082f8aafcd286aa4368a1c8b0d25d06206" },
        { "sky", 246813579L, -33, 31, "df577d0336b57a4a68f5bef55735df492b45c3750b01dafd0a58bf1447106a2c" },
        { "nether", 246813579L, -33, 31, "dfcb7e8e43c3bae5b8ba44a904f182b2f08b854ace29cb51f8e77aa55c332bc5" }
    };

    [Theory]
    [MemberData(nameof(GoldenTerrain))]
    public void Base_terrain_matches_the_shipped_generator_snapshot(
        string profile,
        long seed,
        int chunkX,
        int chunkZ,
        string expected)
    {
        var fixture = GeneratorFixture.Create(profile, seed);

        var chunk = fixture.Generator.GetChunk(chunkX, chunkZ);
        var actual = Fingerprint(chunk);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Compiled_overworld_noise_settings_change_candidate_terrain()
    {
        var runtime = ContentRuntime.Current;
        var path = Path.Combine(AppContext.BaseDirectory, "assets", "world_type", "default.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var settings = root["GeneratorSettings"]!.AsObject();
        settings["HorizontalNoiseScale"] = 512.0D;
        var compiled = runtime.WorldGeneratorProviders.Compile(
            BuiltInWorldGeneratorProviders.Overworld,
            "example:scaled_overworld",
            JsonSerializer.SerializeToElement(settings),
            new WorldGeneratorCompileContext(runtime.Blocks));
        var world = new GenerationTestWorld(
            987654321L,
            runtime.WorldTypes.Get("omniblock:default"),
            "",
            null);
        WorldGeneratorBuildContext context = new(world, world.Seed, "");

        var actual = Fingerprint(compiled.Create(context).GetChunk(12, -7));

        Assert.NotEqual(
            "7624e29fe1e5b381551048ad8214c072187d2a4fef7b0f55ad2ee87f56063f09",
            actual);
    }

    [Theory]
    [InlineData("default", 987654321L, 12, -7, "66,66,65,67")]
    [InlineData("flat", 987654321L, 12, -7, "4,4,4,4")]
    [InlineData("sky", 987654321L, 12, -7, "63,60,55,61")]
    [InlineData("nether", 987654321L, 12, -7, "0,0,0,0")]
    public void Representative_height_samples_remain_stable(
        string profile,
        long seed,
        int chunkX,
        int chunkZ,
        string expected)
    {
        var fixture = GeneratorFixture.Create(profile, seed);
        var chunk = fixture.Generator.GetChunk(chunkX, chunkZ);

        var actual = string.Join(',',
            chunk.HeightMap[0],
            chunk.HeightMap[(7 << 4) | 5],
            chunk.HeightMap[(12 << 4) | 3],
            chunk.HeightMap[(15 << 4) | 15]);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("flat")]
    [InlineData("sky")]
    [InlineData("nether")]
    public void Chunk_output_does_not_depend_on_generation_order_or_worker_instance(string profile)
    {
        var fixture = GeneratorFixture.Create(profile, 0x1234_5678_9ABCDEFL);
        var primary = fixture.Generator;
        var worker = primary.CreateParallelInstance();

        var primaryA = Fingerprint(primary.GetChunk(7, -11));
        _ = primary.GetChunk(-3, 5);
        var primaryB = Fingerprint(primary.GetChunk(19, 2));

        var workerB = Fingerprint(worker.GetChunk(19, 2));
        _ = worker.GetChunk(-3, 5);
        var workerA = Fingerprint(worker.GetChunk(7, -11));

        Assert.Equal(primaryA, workerA);
        Assert.Equal(primaryB, workerB);
    }

    [Fact]
    public void Parallel_sky_decoration_preserves_the_fixed_sky_biome_source()
    {
        const long seed = 0x1234_5678_9ABCDEFL;
        var primary = GeneratorFixture.Create("sky", seed);
        var parallel = GeneratorFixture.Create("sky", seed);
        var worker = parallel.Generator.CreateParallelInstance();

        PopulateDecorationNeighborhood(primary.World, primary.Generator);
        PopulateDecorationNeighborhood(parallel.World, worker);
        primary.Generator.DecorateTerrain(primary.World.Chunks, 0, 0);
        worker.DecorateTerrain(parallel.World.Chunks, 0, 0);

        Assert.Equal(Fingerprint(primary.World.Chunks.All), Fingerprint(parallel.World.Chunks.All));
    }

    [Theory]
    [InlineData("default", 246813579L, "0292e5defd7f632606cec1273bf2ca8b27db3c9d677b30f6d73e715da9f167eb")]
    [InlineData("flat", 246813579L, "74c35f9e5910ed073e7bea236d440567f18c1396195e47d815d8653a056b0a5b")]
    [InlineData("sky", 246813579L, "6e47e61eb3234c6f15890dae2522c0bf32c1c2d446f097093007b6fc90af21a1")]
    [InlineData("nether", 246813579L, "4237e309e1b31c1faa9a2ebbb8928fb22078203585d674a63c9a96b4f3210de8")]
    public void Decorated_neighborhood_matches_the_shipped_feature_snapshot(
        string profile,
        long seed,
        string expected)
    {
        var fixture = GeneratorFixture.Create(profile, seed);
        for (var chunkX = -1; chunkX <= 2; chunkX++)
        for (var chunkZ = -1; chunkZ <= 2; chunkZ++)
            fixture.World.Chunks.Store(fixture.Generator.GetChunk(chunkX, chunkZ));

        fixture.Generator.DecorateTerrain(fixture.World.Chunks, 0, 0);

        var actual = Fingerprint(fixture.World.Chunks.All);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(GoldenCompletedNeighborhoods))]
    public void Completed_negative_region_boundary_neighborhood_matches_the_shipped_snapshot(
        string profile,
        long seed,
        int centerX,
        int centerZ,
        string expected)
    {
        var fixture = GeneratorFixture.Create(profile, seed);
        PopulateCompletedNeighborhood(fixture, centerX, centerZ);

        var actual = CompletedFingerprint(fixture.World.Chunks.All);
        Assert.True(expected == actual, $"Expected {expected}; actual {actual}");
    }

    [Theory]
    [InlineData("default")]
    [InlineData("flat")]
    [InlineData("sky")]
    [InlineData("nether")]
    public void Worker_completion_order_does_not_change_a_prescribed_history(string profile)
    {
        const long seed = 0x1357_2468_1234_567L;
        var canonical = GeneratorFixture.Create(profile, seed);
        var reversed = GeneratorFixture.Create(profile, seed);
        var canonicalWorker = canonical.Generator.CreateParallelInstance();
        var reversedWorker = reversed.Generator.CreateParallelInstance();
        var positions = NeighborhoodPositions(0, 0).ToArray();

        var canonicalChunks = positions
            .Select(position => canonicalWorker.GetChunk(position.X, position.Z))
            .ToArray();
        var completedChunks = positions
            .Select(position => reversedWorker.GetChunk(position.X, position.Z))
            .ToArray();
        foreach (var chunk in canonicalChunks)
            canonical.World.Chunks.Store(chunk);
        foreach (var chunk in completedChunks.Reverse())
            reversed.World.Chunks.Store(chunk);

        InitializeLighting(canonical);
        InitializeLighting(reversed);
        CompleteDecoration(canonical, 0, 0);
        CompleteDecoration(reversed, 0, 0);

        Assert.Equal(
            CompletedFingerprint(canonical.World.Chunks.All),
            CompletedFingerprint(reversed.World.Chunks.All));
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("flat", true)]
    [InlineData("sky", true)]
    [InlineData("nether", true)]
    public void Generator_traversal_order_dependency_is_explicit(string profile, bool expectedEqual)
    {
        const long seed = 0x1357_2468_1234_567L;
        var forward = GeneratorFixture.Create(profile, seed);
        var reverse = GeneratorFixture.Create(profile, seed);
        var positions = NeighborhoodPositions(0, 0).ToArray();

        foreach (var (x, z) in positions)
            forward.World.Chunks.Store(forward.Generator.GetChunk(x, z));
        foreach (var (x, z) in positions.Reverse())
            reverse.World.Chunks.Store(reverse.Generator.GetChunk(x, z));

        Assert.Equal(expectedEqual,
            Fingerprint(forward.World.Chunks.All) == Fingerprint(reverse.World.Chunks.All));
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("flat", true)]
    [InlineData("sky", true)]
    [InlineData("nether", true)]
    public void Parallel_generator_instance_dependency_is_explicit(string profile, bool expectedEqual)
    {
        const long seed = 0x1357_2468_1234_567L;
        var primary = GeneratorFixture.Create(profile, seed);
        var parallel = GeneratorFixture.Create(profile, seed);
        var worker = parallel.Generator.CreateParallelInstance();

        foreach (var (x, z) in NeighborhoodPositions(0, 0))
        {
            primary.World.Chunks.Store(primary.Generator.GetChunk(x, z));
            parallel.World.Chunks.Store(worker.GetChunk(x, z));
        }

        Assert.Equal(expectedEqual,
            Fingerprint(primary.World.Chunks.All) == Fingerprint(parallel.World.Chunks.All));
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("flat", true)]
    [InlineData("sky", true)]
    [InlineData("nether", false)]
    public void Decoration_dependency_on_previous_generator_terrain_calls_is_explicit(
        string profile,
        bool expectedEqual)
    {
        const long seed = 0x1357_2468_1234_567L;
        var coupled = GeneratorFixture.Create(profile, seed);
        var isolated = GeneratorFixture.Create(profile, seed);
        var isolatedWorker = isolated.Generator.CreateParallelInstance();

        foreach (var (x, z) in NeighborhoodPositions(0, 0))
        {
            coupled.World.Chunks.Store(coupled.Generator.GetChunk(x, z));
            isolated.World.Chunks.Store(isolatedWorker.GetChunk(x, z));
        }

        InitializeLighting(coupled);
        InitializeLighting(isolated);
        CompleteDecoration(coupled, 0, 0);
        CompleteDecoration(isolated, 0, 0);

        Assert.Equal(expectedEqual,
            CompletedFingerprint(coupled.World.Chunks.All) ==
            CompletedFingerprint(isolated.World.Chunks.All));
    }

    [Theory]
    [InlineData("default", true)]
    [InlineData("flat", true)]
    [InlineData("sky", true)]
    [InlineData("nether", false)]
    public void Decoration_traversal_order_dependency_is_explicit(string profile, bool expectedEqual)
    {
        const long seed = 0x2468_1357_7654_321L;
        var forward = GeneratorFixture.Create(profile, seed);
        var reverse = GeneratorFixture.Create(profile, seed);
        var positions = NeighborhoodPositions(0, 0)
            .Concat(NeighborhoodPositions(1, 0))
            .Distinct()
            .ToArray();
        foreach (var (x, z) in positions)
        {
            forward.World.Chunks.Store(forward.Generator.GetChunk(x, z));
            reverse.World.Chunks.Store(reverse.Generator.GetChunk(x, z));
        }

        InitializeLighting(forward);
        InitializeLighting(reverse);
        CompleteDecoration(forward, 0, 0);
        CompleteDecoration(forward, 1, 0);
        CompleteDecoration(reverse, 1, 0);
        CompleteDecoration(reverse, 0, 0);

        Assert.Equal(expectedEqual,
            CompletedFingerprint(forward.World.Chunks.All) ==
            CompletedFingerprint(reverse.World.Chunks.All));
    }

    [Theory]
    [InlineData(31, -33)]
    [InlineData(32, -32)]
    public void Region_storage_round_trip_preserves_generated_boundary_chunks(int chunkX, int chunkZ)
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-generation-region-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var fixture = GeneratorFixture.Create("default", 987654321L);
            var original = fixture.Generator.GetChunk(chunkX, chunkZ);
            original.PopulateBlockLight();
            var storage = new RegionChunkStorage(root);

            storage.SaveChunk(fixture.World, original, () => { }, 0);
            var loaded = storage.LoadChunk(fixture.World, chunkX, chunkZ);

            Assert.NotNull(loaded);
            Assert.Equal(CompletedFingerprint([original]), CompletedFingerprint([loaded!]));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("default", 1L, 0, 0, "Desert")]
    [InlineData("default", 987654321L, 197, -113, "Shrubland")]
    [InlineData("flat", 1L, 0, 0, "Desert")]
    [InlineData("sky", 1L, 0, 0, "Sky")]
    [InlineData("nether", 1L, 0, 0, "Hell")]
    public void Biome_selection_matches_the_shipped_profile(
        string profile,
        long seed,
        int blockX,
        int blockZ,
        string expected)
    {
        var fixture = GeneratorFixture.Create(profile, seed);

        var biome = fixture.World.Dimension.BiomeSource.GetBiome(blockX, blockZ);

        Assert.Equal(expected, biome.Name);
    }

    [Theory]
    [InlineData("default", false)]
    [InlineData("flat", true)]
    [InlineData("sky", false)]
    [InlineData("nether", false)]
    public void Spawn_suitability_remains_stable(string profile, bool expected)
    {
        var fixture = GeneratorFixture.Create(profile, 1L);
        fixture.World.Chunks.Store(fixture.Generator.GetChunk(0, 0));

        Assert.Equal(expected, fixture.World.Dimension.IsValidSpawnPoint(0, 0));
    }

    [Fact]
    public void Flat_generator_options_preserve_layer_depth_ids_and_metadata()
    {
        var blocks = ContentRuntime.Current.Blocks;
        var bedrock = blocks.Get("omniblock:bedrock");
        var dirt = blocks.Get("omniblock:dirt");
        var wool = blocks.Get("omniblock:wool");
        var options = $"2;1x{bedrock.Id},2x{dirt.Id},1x{wool.Id}:5;8;";
        var fixture = GeneratorFixture.Create("flat", 42L, options);

        var chunk = fixture.Generator.GetChunk(-4, 9);

        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        {
            Assert.Equal(bedrock.Id, chunk[x, 0, z]);
            Assert.Equal(dirt.Id, chunk[x, 1, z]);
            Assert.Equal(dirt.Id, chunk[x, 2, z]);
            Assert.Equal(wool.Id, chunk[x, 3, z]);
            Assert.Equal(5, chunk.GetBlockMeta(x, 3, z));
            Assert.Equal(0, chunk[x, 4, z]);
            Assert.Equal(4, chunk.HeightMap[(z << 4) | x]);
        }
    }

    private static string Fingerprint(Chunk chunk)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(chunk.Blocks);
        hash.AppendData(chunk.Meta.Bytes);
        hash.AppendData(chunk.HeightMap);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void PopulateDecorationNeighborhood(GenerationTestWorld world, IChunkSource generator)
    {
        for (var chunkX = -1; chunkX <= 2; chunkX++)
        for (var chunkZ = -1; chunkZ <= 2; chunkZ++)
            world.Chunks.Store(generator.GetChunk(chunkX, chunkZ));
    }

    private static void PopulateCompletedNeighborhood(GeneratorFixture fixture, int centerX, int centerZ)
    {
        foreach (var (x, z) in NeighborhoodPositions(centerX, centerZ))
            fixture.World.Chunks.Store(fixture.Generator.GetChunk(x, z));
        InitializeLighting(fixture);
        CompleteDecoration(fixture, centerX, centerZ);
    }

    private static IEnumerable<(int X, int Z)> NeighborhoodPositions(int centerX, int centerZ)
    {
        for (var x = centerX - 1; x <= centerX + 2; x++)
        for (var z = centerZ - 1; z <= centerZ + 2; z++)
            yield return (x, z);
    }

    private static void InitializeLighting(GeneratorFixture fixture)
    {
        // Completion timing may alter insertion order; the prescribed publication history does
        // not. Initialize in coordinate order so this test varies worker completion only.
        foreach (var chunk in fixture.World.Chunks.All
                     .OrderBy(static chunk => chunk.X)
                     .ThenBy(static chunk => chunk.Z))
            chunk.PopulateBlockLight();
    }

    private static void CompleteDecoration(GeneratorFixture fixture, int x, int z)
    {
        fixture.World.Chunks.GetChunk(x, z).TerrainPopulated = true;
        fixture.Generator.DecorateTerrain(fixture.World.Chunks, x, z);
        var passes = 0;
        while (fixture.World.Lighting.DoLightingUpdates())
        {
            passes++;
            Assert.True(passes < 10000, "Lighting did not converge in the characterization fixture.");
        }
    }

    private static string CompletedFingerprint(IEnumerable<Chunk> chunks)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var chunk in chunks.OrderBy(static chunk => chunk.X).ThenBy(static chunk => chunk.Z))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(
                $"{chunk.X},{chunk.Z}:{chunk.TerrainPopulated}:{chunk.BlockEntities.Count}:"));
            hash.AppendData(chunk.Blocks);
            hash.AppendData(chunk.Meta.Bytes);
            hash.AppendData(chunk.HeightMap);
            hash.AppendData(chunk.SkyLight.Bytes);
            hash.AppendData(chunk.BlockLight.Bytes);
            foreach (var blockEntity in chunk.BlockEntities.Values
                         .OrderBy(static entity => entity.X)
                         .ThenBy(static entity => entity.Y)
                         .ThenBy(static entity => entity.Z))
                hash.AppendData(Encoding.UTF8.GetBytes(
                    $"{blockEntity.GetType().FullName}@{blockEntity.X},{blockEntity.Y},{blockEntity.Z};"));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string Fingerprint(IEnumerable<Chunk> chunks)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var chunk in chunks.OrderBy(static chunk => chunk.X).ThenBy(static chunk => chunk.Z))
        {
            hash.AppendData(Encoding.UTF8.GetBytes($"{chunk.X},{chunk.Z}:"));
            hash.AppendData(chunk.Blocks);
            hash.AppendData(chunk.Meta.Bytes);
            hash.AppendData(chunk.HeightMap);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed class GeneratorFixture
    {
        private GeneratorFixture(GenerationTestWorld world)
        {
            World = world;
            Generator = world.Dimension.CreateChunkGenerator();
        }

        public GenerationTestWorld World { get; }
        public IChunkSource Generator { get; }

        public static GeneratorFixture Create(string profile, long seed, string options = "")
        {
            var types = ContentRuntime.Current.WorldTypes;
            var worldType = profile == "nether" ? types.Get("default") : types.Get(profile);
            var dimension = profile == "nether"
                ? Dimension.FromId(-1, ContentRuntime.Current)
                : null;
            return new GeneratorFixture(new GenerationTestWorld(seed, worldType, options, dimension));
        }
    }

    private sealed class GenerationTestWorld : World
    {
        public GenerationTestWorld(long seed, WorldType type, string options, Dimension? dimension)
            : base(
                new MemoryWorldStorage(),
                "generation-characterization",
                new WorldSettings(seed, type, options),
                dimension,
                ContentRuntime.Current)
        {
        }

        public MemoryChunkSource Chunks { get; private set; } = null!;

        protected override IChunkSource CreateChunkCache() => Chunks = new MemoryChunkSource(this);
    }

    private sealed class MemoryChunkSource(IWorldContext world) : IChunkSource
    {
        private readonly Dictionary<(int X, int Z), Chunk> _chunks = [];

        public IEnumerable<Chunk> All => _chunks.Values;

        public bool IsChunkLoaded(int x, int z) => _chunks.ContainsKey((x, z));

        public Chunk GetChunk(int x, int z)
        {
            if (_chunks.TryGetValue((x, z), out var chunk)) return chunk;
            chunk = new Chunk(world, new byte[ChuckFormat.ChunkSize], x, z);
            _chunks[(x, z)] = chunk;
            return chunk;
        }

        public Chunk LoadChunk(int x, int z) => GetChunk(x, z);

        public void DecorateTerrain(IChunkSource source, int x, int z)
        {
        }

        public bool Save(bool saveEntities, LoadingDisplay? display) => true;
        public bool Tick() => false;
        public bool CanSave() => false;
        public string GetDebugInfo() => nameof(MemoryChunkSource);

        public void Store(Chunk chunk) => _chunks[(chunk.X, chunk.Z)] = chunk;
    }

    private sealed class MemoryWorldStorage : IWorldStorage
    {
        public WorldProperties? LoadProperties() => null;

        public void CheckSessionLock()
        {
        }

        public IChunkStorage? GetChunkStorage(Dimension dimension) => null;

        public void Save(WorldProperties properties, List<EntityPlayer> players)
        {
        }

        public void Save(WorldProperties properties)
        {
        }

        public void ForceSave()
        {
        }

        public IPlayerStorage? GetPlayerStorage() => null;
        public FileInfo? GetWorldPropertiesFile(string name) => null;
    }
}
