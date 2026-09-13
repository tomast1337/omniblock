using System.Security.Cryptography;
using System.Text;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities;
using OmniBlock.Server.Worlds;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Util.Maths;
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

[Collection(ChunkGeneratorCharacterizationCollection.Name)]
public sealed class InactiveGenerationWorkspaceTests
{
    public static TheoryData<string, string> GoldenInactiveNeighborhoods => new()
    {
        { "default", "3243eec63e6652fdb476fbad9f04049475e90c6afe688aedd696de1cd9fac709" },
        { "flat", "e10fc05d6ac7cbd6e614880cb404a2082f8aafcd286aa4368a1c8b0d25d06206" },
        { "sky", "df577d0336b57a4a68f5bef55735df492b45c3750b01dafd0a58bf1447106a2c" },
        { "nether", "dfcb7e8e43c3bae5b8ba44a904f182b2f08b854ace29cb51f8e77aa55c332bc5" }
    };

    public static TheoryData<string, string> GoldenOverlappingInactiveRegions => new()
    {
        { "default", "d8919fbab609fe7633917bd53fcf6e3a9ba8ba1eb682befd4ac128e9b682440f" },
        { "flat", "8d10226948fa2e9ca654fd08c185510a316ae92cea906030a6585b1164cf7dcb" },
        { "sky", "0ec98af116718511df4de18195352cfcac64631f46f36776f0c46acade3544e7" },
        { "nether", "d63ec9920646ffa8d9157685a198efcd286c241ad91cf55b2976b214b1a2d033" }
    };

    [Fact]
    public void Stored_ticks_do_not_reach_the_world_before_chunk_activation()
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("stone");
        var chunk = new Chunk(world, new byte[ChuckFormat.ChunkSize], 2, -3);

        chunk.QueueActivationTick(33, 64, -47, stone.Id, 7);

        Assert.Equal(0, world.TickScheduler.Count);
        Assert.Equal(1, chunk.PendingActivationTickCount);
        chunk.Load();
        Assert.Equal(1, world.TickScheduler.Count);
        Assert.Equal(0, chunk.PendingActivationTickCount);
    }

    [Fact]
    public void Completed_workspace_does_not_publish_entities_ticks_or_chunks_to_source_world()
    {
        var source = new SourceWorld(246813579L);
        var entityCount = source.Entities.Entities.Count;
        var tickCount = source.TickScheduler.Count;
        var workspace = new InactiveGenerationWorkspace(source);

        var batch = workspace.GenerateCompletedNeighborhood(0, 0);

        Assert.Equal(16, batch.Chunks.Count);
        Assert.Equal(entityCount, source.Entities.Entities.Count);
        Assert.Equal(tickCount, source.TickScheduler.Count);
        Assert.False(source.Chunks.IsChunkLoaded(0, 0));

        var materialized = batch.Get(0, 0).Materialize(source);
        Assert.False(materialized.Loaded);
        Assert.Same(source, materialized.World);
        Assert.False(source.Chunks.IsChunkLoaded(0, 0));
        Assert.Equal(entityCount, source.Entities.Entities.Count);
        Assert.Equal(tickCount, source.TickScheduler.Count);
    }

    [Fact]
    public void Workspace_honors_cancellation_before_starting_an_indivisible_stage()
    {
        var source = new SourceWorld(1L);
        var workspace = new InactiveGenerationWorkspace(source);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            workspace.GenerateCompletedNeighborhood(0, 0, cancellation.Token));
        Assert.False(source.Chunks.IsChunkLoaded(0, 0));
    }

    [Fact]
    public void Region_save_acknowledges_only_after_the_output_stream_is_committed()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-save-ack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var world = new SourceWorld(1L);
            var chunk = world.Generator.GetChunk(0, 0);
            var callbackCalled = false;
            var storage = new RegionChunkStorage(root);

            var result = storage.SaveChunk(world, chunk, () => callbackCalled = true, 1);

            Assert.True(callbackCalled);
            Assert.True(result.SizeDeltaBytes > 0);
            Assert.NotNull(storage.LoadChunk(world, 0, 0));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Completed_batch_is_durably_saved_reopened_and_stays_inactive_until_load()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-inactive-commit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var world = new SourceWorld(246813579L);
            var batch = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(0, 0);
            var expected = batch.Get(0, 0).Materialize(world);
            var storage = new RegionChunkStorage(root);

            var commit = batch.SaveDurably(world, storage, firstSequence: 100);

            Assert.Equal(16, commit.Chunks.Count);
            Assert.True(commit.SizeDeltaBytes > 0);
            var reopened = new RegionChunkStorage(root).LoadChunk(world, 0, 0);
            Assert.NotNull(reopened);
            Assert.False(reopened.Loaded);
            Assert.Equal(expected.Blocks, reopened.Blocks);
            Assert.Equal(expected.Meta.Bytes, reopened.Meta.Bytes);
            Assert.Equal(expected.HeightMap, reopened.HeightMap);
            Assert.Equal(expected.TerrainPopulated, reopened.TerrainPopulated);

            reopened.Load();
            Assert.True(reopened.Loaded);
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Failed_batch_write_identifies_chunk_and_never_reports_a_durable_commit()
    {
        var world = new SourceWorld(1L);
        var batch = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(0, 0);
        var storage = new RecordingChunkStorage(failWriteNumber: 2);

        var error = Assert.Throws<InactiveGenerationCommitException>(() =>
            batch.SaveDurably(world, storage));

        Assert.Equal(InactiveGenerationCommitStage.WriteChunk, error.Stage);
        Assert.Equal(-1, error.ChunkX);
        Assert.Equal(0, error.ChunkZ);
        Assert.Equal(1, error.CompletedWrites);
        Assert.False(storage.FlushedToDisk);
    }

    [Fact]
    public void Failed_durable_flush_is_distinct_from_completed_chunk_writes()
    {
        var world = new SourceWorld(1L);
        var batch = new InactiveGenerationWorkspace(world).GenerateCompletedNeighborhood(0, 0);
        var storage = new RecordingChunkStorage(failFlush: true);

        var error = Assert.Throws<InactiveGenerationCommitException>(() =>
            batch.SaveDurably(world, storage));

        Assert.Equal(InactiveGenerationCommitStage.Flush, error.Stage);
        Assert.Null(error.ChunkX);
        Assert.Null(error.ChunkZ);
        Assert.Equal(16, error.CompletedWrites);
        Assert.True(storage.FlushedToDisk);
    }

    [Theory]
    [MemberData(nameof(GoldenInactiveNeighborhoods))]
    public void Single_target_inactive_decoration_preserves_the_shipped_fingerprint(
        string profile,
        string expected)
    {
        var world = new SourceWorld(246813579L, profile);
        var batch = new InactiveGenerationWorkspace(world)
            .GenerateCompletedNeighborhood(-33, 31);

        Assert.Equal(expected, Fingerprint(batch, world));
    }

    [Theory]
    [MemberData(nameof(GoldenOverlappingInactiveRegions))]
    public void Overlapping_targets_share_dependencies_and_ignore_input_completion_order(
        string profile,
        string expected)
    {
        ChunkPos[] forwardTargets = [new(0, 0), new(1, 0), new(0, 0)];
        ChunkPos[] reversedTargets = [new(1, 0), new(0, 0)];
        var forwardWorld = new SourceWorld(0x2468_1357_7654_321L, profile);
        var reversedWorld = new SourceWorld(0x2468_1357_7654_321L, profile);

        var forward = new InactiveGenerationWorkspace(forwardWorld)
            .GenerateCompletedRegion(forwardTargets);
        var reversed = new InactiveGenerationWorkspace(reversedWorld)
            .GenerateCompletedRegion(reversedTargets);

        Assert.Equal([new ChunkPos(0, 0), new ChunkPos(1, 0)], forward.DecoratedTargets);
        Assert.Equal(20, forward.Chunks.Count);
        var actual = Fingerprint(forward, forwardWorld);
        Assert.Equal(actual, Fingerprint(reversed, reversedWorld));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Unknown_generator_provider_fails_before_inactive_work_starts()
    {
        var source = new FakeWorldContext();
        source.Properties.TerrainType = new WorldType("example:custom", "example:custom");

        var error = Assert.Throws<NotSupportedException>(() =>
            new InactiveGenerationWorkspace(source));

        Assert.Contains("example:custom", error.Message);
        Assert.Contains("deterministic inactive-decoration", error.Message);
    }

    [Fact]
    public void Instant_fall_scope_is_nested_and_restored()
    {
        FallingBlockBehavior.FallInstantly = false;
        using (FallingBlockBehavior.BeginInstantFallScope())
        {
            Assert.True(FallingBlockBehavior.FallInstantly);
            using (FallingBlockBehavior.BeginInstantFallScope())
                Assert.True(FallingBlockBehavior.FallInstantly);
            Assert.True(FallingBlockBehavior.FallInstantly);
        }

        Assert.False(FallingBlockBehavior.FallInstantly);
    }

    [Fact]
    public void Instant_fall_scope_is_restored_when_decoration_throws()
    {
        FallingBlockBehavior.FallInstantly = false;

        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var scope = FallingBlockBehavior.BeginInstantFallScope();
            Assert.True(FallingBlockBehavior.FallInstantly);
            throw new InvalidOperationException("fixture");
        }));

        Assert.False(FallingBlockBehavior.FallInstantly);
    }

    private static string Fingerprint(InactiveGenerationBatch batch, IWorldContext world)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var snapshot in batch.Chunks.OrderBy(static chunk => chunk.X).ThenBy(static chunk => chunk.Z))
        {
            var chunk = snapshot.Materialize(world);
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

    private sealed class SourceWorld : World
    {
        public SourceWorld(long seed, string profile = "default")
            : base(new MemoryStorage(), "inactive-source",
                new WorldSettings(seed, ResolveWorldType(profile)),
                ResolveDimension(profile), ContentRuntime.Current)
        {
            Generator = Dimension.CreateChunkGenerator();
        }

        public MemorySource Chunks { get; private set; } = null!;
        public IChunkSource Generator { get; }
        protected override IChunkSource CreateChunkCache() => Chunks = new MemorySource(this);

        private static WorldType ResolveWorldType(string profile) =>
            ContentRuntime.Current.WorldTypes.Get(profile == "nether" ? "default" : profile);

        private static Dimension? ResolveDimension(string profile) =>
            profile == "nether" ? Dimension.FromId(-1, ContentRuntime.Current) : null;
    }

    private sealed class MemorySource(IWorldContext world) : IChunkSource
    {
        private readonly Dictionary<(int X, int Z), Chunk> _chunks = [];
        public bool IsChunkLoaded(int x, int z) => _chunks.ContainsKey((x, z));
        public Chunk GetChunk(int x, int z) => _chunks.TryGetValue((x, z), out var chunk)
            ? chunk
            : new EmptyChunk(world, new byte[ChuckFormat.ChunkSize], x, z);
        public Chunk LoadChunk(int x, int z) => GetChunk(x, z);
        public void DecorateTerrain(IChunkSource source, int x, int z) { }
        public bool Save(bool saveEntities, LoadingDisplay display) => true;
        public bool Tick() => false;
        public bool CanSave() => false;
        public string GetDebugInfo() => nameof(MemorySource);
    }

    private sealed class MemoryStorage : IWorldStorage
    {
        public WorldProperties? LoadProperties() => null;
        public void CheckSessionLock() { }
        public IChunkStorage? GetChunkStorage(Dimension dimension) => null;
        public void Save(WorldProperties properties, List<EntityPlayer> players) { }
        public void Save(WorldProperties properties) { }
        public void ForceSave() { }
        public IPlayerStorage? GetPlayerStorage() => null;
        public FileInfo? GetWorldPropertiesFile(string name) => null;
    }

    private sealed class RecordingChunkStorage(int failWriteNumber = -1, bool failFlush = false)
        : IChunkStorage
    {
        private int _writes;
        public bool FlushedToDisk { get; private set; }

        public Chunk? LoadChunk(IWorldContext world, int chunkX, int chunkZ) => null;

        public ChunkSaveResult SaveChunk(IWorldContext world, Chunk chunk, Action? onSave, long sequence)
        {
            _writes++;
            if (_writes == failWriteNumber) throw new IOException("Injected write failure.");
            onSave?.Invoke();
            return new ChunkSaveResult(1);
        }

        public void SaveEntities(IWorldContext world, Chunk chunk) { }
        public void Tick() { }
        public void Flush() { }

        public void FlushToDisk()
        {
            FlushedToDisk = true;
            if (failFlush) throw new IOException("Injected flush failure.");
        }
    }
}
