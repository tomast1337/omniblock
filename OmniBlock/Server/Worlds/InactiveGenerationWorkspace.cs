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
using OmniBlock.Worlds.Generation;
using OmniBlock.Worlds.Lod;
using OmniBlock.Worlds.Storage;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Server.Worlds;

/// <summary>
///     Isolated terrain/decorating state. Its chunks, entities, scheduled effects, lighting, and
///     event listeners belong to a private world and cannot enter the live world until a snapshot
///     is explicitly materialized and published on the server thread.
/// </summary>
internal sealed class InactiveGenerationWorkspace
{
    private readonly WorkspaceWorld _world;
    private readonly IChunkSource _generator;
    private readonly IChunkStorage? _storage;
    private readonly HashSet<ChunkPos> _storedDependencies = [];
    private readonly HashSet<ChunkPos> _writableStoredTargets = [];
    private int _started;

    public InactiveGenerationWorkspace(IWorldContext source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var (providerType, decorationPolicy) = ResolveProvider(source);
        EnsureProviderSupported(providerType, decorationPolicy);
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
        _storage = (source as World)?.GetWorldStorage().GetChunkStorage(source.Dimension);
    }

    /// <summary>
    ///     Produces the legacy 4-by-4 decoration dependency neighborhood in deterministic order.
    ///     Cancellation is cooperative between indivisible legacy stages.
    /// </summary>
    public InactiveGenerationBatch GenerateCompletedNeighborhood(
        int chunkX,
        int chunkZ,
        CancellationToken cancellationToken = default) =>
        GenerateCompletedRegion([new ChunkPos(chunkX, chunkZ)], cancellationToken);

    /// <summary>
    ///     Completes a set of decoration targets as one deterministic transaction. Dependency
    ///     chunks are generated once, and all mutating decoration is deliberately serialized.
    /// </summary>
    public InactiveGenerationBatch GenerateCompletedRegion(
        IEnumerable<ChunkPos> targets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targets);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException("An inactive generation workspace is single-use.");

        var orderedTargets = targets
            .Distinct()
            .OrderBy(static target => target.X)
            .ThenBy(static target => target.Z)
            .ToArray();
        if (orderedTargets.Length == 0)
            throw new ArgumentException("At least one decoration target is required.", nameof(targets));

        var dependencies = new HashSet<ChunkPos>();
        foreach (var target in orderedTargets)
        for (var x = target.X - 1; x <= target.X + 2; x++)
        for (var z = target.Z - 1; z <= target.Z + 2; z++)
            dependencies.Add(new ChunkPos(x, z));

        foreach (var position in dependencies
                     .OrderBy(static position => position.X)
                     .ThenBy(static position => position.Z))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Chunk? chunk = null;
            if (_storage?.ContainsChunk(position.X, position.Z) == true)
            {
                chunk = _storage.LoadChunk(_world, position.X, position.Z);
                if (chunk is null)
                    throw new InvalidDataException(
                        $"Stored dependency chunk {position.X},{position.Z} exists but could not be decoded; " +
                        "inactive generation will not replace it.");
                _storedDependencies.Add(position);
            }

            _world.Chunks.Store(chunk ?? _generator.GetChunk(position.X, position.Z));
        }

        foreach (var chunk in _world.Chunks.All
                     .OrderBy(static chunk => chunk.X)
                     .ThenBy(static chunk => chunk.Z))
        {
            cancellationToken.ThrowIfCancellationRequested();
            chunk.PopulateBlockLight();
        }

        foreach (var target in orderedTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decorated = _world.Chunks.GetChunk(target.X, target.Z);
            if (decorated.TerrainPopulated) continue;

            if (_storedDependencies.Remove(target))
                _writableStoredTargets.Add(target);
            decorated.TerrainPopulated = true;
            _generator.DecorateTerrain(_world.Chunks, target.X, target.Z);

            while (_world.Lighting.DoLightingUpdates())
                cancellationToken.ThrowIfCancellationRequested();
        }

        return new InactiveGenerationBatch(
            _world.Chunks.All
                .Where(chunk => !_storedDependencies.Contains(new ChunkPos(chunk.X, chunk.Z)))
                .OrderBy(static chunk => chunk.X)
                .ThenBy(static chunk => chunk.Z)
                .Select(chunk => InactiveChunkSnapshot.Capture(chunk, _world))
                .ToArray())
        {
            DecoratedTargets = orderedTargets,
            SkippedTargets = orderedTargets
                .Where(target => !_writableStoredTargets.Contains(target) &&
                                 _storedDependencies.Contains(target))
                .ToArray(),
            WritableStoredTargets = _writableStoredTargets.ToArray()
        };
    }

    private static (ResourceLocation ProviderType, InactiveDecorationPolicy Policy) ResolveProvider(
        IWorldContext source)
    {
        if (source.Dimension.Id == -1)
        {
            var profile = source.Content.DimensionGeneratorProfiles.GetByDimensionId(source.Dimension.Id);
            return (profile.GeneratorProviderType, profile.InactiveDecorationPolicy);
        }

        var worldType = source.Properties.TerrainType;
        return (worldType.GeneratorProviderType, worldType.InactiveDecorationPolicy);
    }

    private static void EnsureProviderSupported(
        ResourceLocation providerType,
        InactiveDecorationPolicy policy)
    {
        if (policy == InactiveDecorationPolicy.DeterministicSerial) return;
        throw new NotSupportedException(
            $"World-generator provider '{providerType}' has not declared deterministic inactive-decoration support.");
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
    public IReadOnlyList<ChunkPos> DecoratedTargets { get; init; } = [];
    public IReadOnlyList<ChunkPos> SkippedTargets { get; init; } = [];
    public IReadOnlyList<ChunkPos> WritableStoredTargets { get; init; } = [];
    public long RetainedBytes => Chunks.Sum(static chunk => (long)chunk.SerializedLength);
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
    private readonly bool _hasSkyLight;

    private InactiveChunkSnapshot(int x, int z, byte[] nbt, bool hasSkyLight)
    {
        X = x;
        Z = z;
        _nbt = nbt;
        _hasSkyLight = hasSkyLight;
    }

    public int X { get; }
    public int Z { get; }
    internal int SerializedLength => _nbt.Length;

    internal static InactiveChunkSnapshot Capture(Chunk chunk, IWorldContext world)
    {
        NBTTagCompound root = new();
        NBTTagCompound level = new();
        root.SetTag("Level", level);
        RegionChunkStorage.storeChunkInCompound(chunk, world, level);
        using MemoryStream output = new();
        NbtIo.Write(root, output);
        return new InactiveChunkSnapshot(
            chunk.X, chunk.Z, output.ToArray(), !world.Dimension.HasCeiling);
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

    /// <summary>Extracts immutable terrain for LOD conversion without creating entities.</summary>
    public TerrainLodSourceSnapshot CaptureTerrain(long? terrainRevision = null)
    {
        using MemoryStream input = new(_nbt, writable: false);
        var root = NbtIo.Read(input);
        var snapshot = TerrainLodSourceSnapshot.FromRegionNbt(
            root.GetCompoundTag("Level"), terrainRevision, _hasSkyLight);
        if (snapshot.ChunkX != X || snapshot.ChunkZ != Z)
            throw new InvalidDataException(
                $"Inactive snapshot for {X},{Z} contains terrain for " +
                $"{snapshot.ChunkX},{snapshot.ChunkZ}.");
        return snapshot;
    }
}
