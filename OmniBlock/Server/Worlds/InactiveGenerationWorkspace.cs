using OmniBlock.Entities;
using OmniBlock.NBT;
using OmniBlock.Registries;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Chunks.Storage;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Dimensions;
using OmniBlock.Worlds.Storage;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Server.Worlds;

/// <summary>
///     Isolated terrain/decorating state. Its chunks, entities, scheduled effects, lighting, and
///     event listeners belong to a private world and cannot enter the live world until a snapshot
///     is explicitly materialized and published on the server thread.
/// </summary>
public sealed class InactiveGenerationWorkspace
{
    private readonly WorkspaceWorld _world;
    private readonly IChunkSource _generator;
    private int _started;

    public InactiveGenerationWorkspace(IWorldContext source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var dimension = source.Dimension.Id == -1
            ? Dimension.FromId(-1, source.Content)
            : null;
        _world = new WorkspaceWorld(
            source.Seed,
            source.Properties.TerrainType,
            source.Properties.GeneratorOptions,
            dimension,
            source.Content);
        _generator = _world.Dimension.CreateChunkGenerator();
    }

    /// <summary>
    ///     Produces the legacy 4-by-4 decoration dependency neighborhood in deterministic order.
    ///     Cancellation is cooperative between indivisible legacy stages.
    /// </summary>
    public InactiveGenerationBatch GenerateCompletedNeighborhood(
        int chunkX,
        int chunkZ,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("An inactive generation workspace is single-use.");

        for (var x = chunkX - 1; x <= chunkX + 2; x++)
        for (var z = chunkZ - 1; z <= chunkZ + 2; z++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _world.Chunks.Store(_generator.GetChunk(x, z));
        }

        foreach (var chunk in _world.Chunks.All
                     .OrderBy(static chunk => chunk.X)
                     .ThenBy(static chunk => chunk.Z))
        {
            cancellationToken.ThrowIfCancellationRequested();
            chunk.PopulateBlockLight();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var decorated = _world.Chunks.GetChunk(chunkX, chunkZ);
        decorated.TerrainPopulated = true;
        _generator.DecorateTerrain(_world.Chunks, chunkX, chunkZ);

        while (_world.Lighting.DoLightingUpdates())
            cancellationToken.ThrowIfCancellationRequested();

        return new InactiveGenerationBatch(
            _world.Chunks.All
                .OrderBy(static chunk => chunk.X)
                .ThenBy(static chunk => chunk.Z)
                .Select(chunk => InactiveChunkSnapshot.Capture(chunk, _world))
                .ToArray());
    }

    private sealed class WorkspaceWorld : World
    {
        public WorkspaceWorld(long seed, WorldType type, string options, Dimension? dimension,
            ContentRuntime content)
            : base(new WorkspaceStorage(), "inactive-generation",
                new WorldSettings(seed, type, options), dimension, content)
        {
        }

        public WorkspaceChunkSource Chunks { get; private set; } = null!;

        protected override IChunkSource CreateChunkCache() => Chunks = new WorkspaceChunkSource(this);
    }

    private sealed class WorkspaceChunkSource(IWorldContext world) : IChunkSource
    {
        private readonly Dictionary<ChunkPos, Chunk> _chunks = [];
        public IEnumerable<Chunk> All => _chunks.Values;
        public bool IsChunkLoaded(int x, int z) => _chunks.ContainsKey(new ChunkPos(x, z));

        public Chunk GetChunk(int x, int z) =>
            _chunks.TryGetValue(new ChunkPos(x, z), out var chunk)
                ? chunk
                : new EmptyChunk(world, new byte[ChuckFormat.ChunkSize], x, z);

        public Chunk LoadChunk(int x, int z) => GetChunk(x, z);
        public void DecorateTerrain(IChunkSource source, int x, int z) { }
        public bool Save(bool saveEntities, LoadingDisplay display) => true;
        public bool Tick() => false;
        public bool CanSave() => false;
        public string GetDebugInfo() => nameof(InactiveGenerationWorkspace);
        public void Store(Chunk chunk) => _chunks[new ChunkPos(chunk.X, chunk.Z)] = chunk;
    }

    private sealed class WorkspaceStorage : IWorldStorage
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

public sealed record InactiveGenerationBatch(IReadOnlyList<InactiveChunkSnapshot> Chunks)
{
    public InactiveChunkSnapshot Get(int x, int z) =>
        Chunks.FirstOrDefault(chunk => chunk.X == x && chunk.Z == z)
        ?? throw new KeyNotFoundException($"Inactive batch does not contain chunk {x},{z}.");

    /// <summary>
    ///     Writes every snapshot and returns only after the storage has durably flushed the batch.
    ///     Callers may advance a persistent job checkpoint only after this method returns.
    /// </summary>
    public InactiveGenerationCommit SaveDurably(
        IWorldContext target,
        IChunkStorage storage,
        long firstSequence = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(storage);
        var saved = new List<InactiveChunkSave>(Chunks.Count);

        foreach (var snapshot in Chunks.OrderBy(static chunk => chunk.X).ThenBy(static chunk => chunk.Z))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var chunk = snapshot.Materialize(target);
                var result = storage.SaveChunk(target, chunk, null, firstSequence + saved.Count);
                saved.Add(new InactiveChunkSave(snapshot.X, snapshot.Z, result.SizeDeltaBytes));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new InactiveGenerationCommitException(
                    InactiveGenerationCommitStage.WriteChunk,
                    snapshot.X,
                    snapshot.Z,
                    saved.Count,
                    error);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            storage.FlushToDisk();
        }
        catch (Exception error)
        {
            throw new InactiveGenerationCommitException(
                InactiveGenerationCommitStage.Flush,
                null,
                null,
                saved.Count,
                error);
        }

        return new InactiveGenerationCommit(
            saved.AsReadOnly(),
            saved.Sum(static chunk => chunk.SizeDeltaBytes));
    }
}

public readonly record struct InactiveChunkSave(int X, int Z, long SizeDeltaBytes);

public sealed record InactiveGenerationCommit(
    IReadOnlyList<InactiveChunkSave> Chunks,
    long SizeDeltaBytes);

public enum InactiveGenerationCommitStage
{
    WriteChunk,
    Flush
}

public sealed class InactiveGenerationCommitException : IOException
{
    public InactiveGenerationCommitException(
        InactiveGenerationCommitStage stage,
        int? chunkX,
        int? chunkZ,
        int completedWrites,
        Exception innerException)
        : base(stage == InactiveGenerationCommitStage.WriteChunk
                ? $"Inactive generation failed while writing chunk {chunkX},{chunkZ} after {completedWrites} writes."
                : $"Inactive generation failed while flushing {completedWrites} written chunks.",
            innerException)
    {
        Stage = stage;
        ChunkX = chunkX;
        ChunkZ = chunkZ;
        CompletedWrites = completedWrites;
    }

    public InactiveGenerationCommitStage Stage { get; }
    public int? ChunkX { get; }
    public int? ChunkZ { get; }
    public int CompletedWrites { get; }
}

/// <summary>An immutable serialized boundary between an isolated workspace and a live world.</summary>
public sealed class InactiveChunkSnapshot
{
    private readonly byte[] _nbt;

    private InactiveChunkSnapshot(int x, int z, byte[] nbt)
    {
        X = x;
        Z = z;
        _nbt = nbt;
    }

    public int X { get; }
    public int Z { get; }

    internal static InactiveChunkSnapshot Capture(Chunk chunk, IWorldContext world)
    {
        NBTTagCompound root = new();
        NBTTagCompound level = new();
        root.SetTag("Level", level);
        RegionChunkStorage.storeChunkInCompound(chunk, world, level);
        using MemoryStream output = new();
        NbtIo.Write(root, output);
        return new InactiveChunkSnapshot(chunk.X, chunk.Z, output.ToArray());
    }

    public Chunk Materialize(IWorldContext target)
    {
        using MemoryStream input = new(_nbt, writable: false);
        var root = NbtIo.Read(input);
        var chunk = RegionChunkStorage.LoadChunkFromNbt(target, root.GetCompoundTag("Level"));
        if (!chunk.ChunkPosEquals(X, Z))
            throw new InvalidDataException(
                $"Inactive snapshot for {X},{Z} materialized as {chunk.X},{chunk.Z}.");
        return chunk;
    }
}
