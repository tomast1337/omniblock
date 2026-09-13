using OmniBlock.Entities;
using OmniBlock.Server.Worlds;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Chunks.Storage;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Dimensions;
using OmniBlock.Worlds.Storage;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Tests.Worlds;

[Collection(ChunkGeneratorCharacterizationCollection.Name)]
public sealed class InactiveGenerationWorkspaceTests
{
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

    private sealed class SourceWorld : World
    {
        public SourceWorld(long seed)
            : base(new MemoryStorage(), "inactive-source",
                new WorldSettings(seed, ContentRuntime.Current.WorldTypes.Get("default")),
                null, ContentRuntime.Current)
        {
            Generator = Dimension.CreateChunkGenerator();
        }

        public MemorySource Chunks { get; private set; } = null!;
        public IChunkSource Generator { get; }
        protected override IChunkSource CreateChunkCache() => Chunks = new MemorySource(this);
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
}
