using Microsoft.Extensions.Logging;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Server.Worlds;

public class ServerChunkCache : IChunkSource
{
    private readonly List<Chunk> _chunks = [];
    private readonly Dictionary<int, Chunk> _chunksByPos = [];
    private readonly HashSet<int> _chunksToUnload = [];
    private readonly Chunk _empty;
    private readonly IChunkSource _generator;
    private readonly ILogger<ServerChunkCache> _logger = Log.Instance.For<ServerChunkCache>();

    private readonly IChunkStorage _storage;

    // Region decoding constructs entities and block entities against the live world. The region
    // byte stream is internally locked, but that larger decode path is not parallel-safe.
    private readonly object _storageLoadLock = new();
    private readonly ServerWorld _world;
    private ServerTerrainLodRuntime? _terrainLod;
    private int _generationScopes;
    private int _retainedChunks;
    private long _retainedPayloadBytes;

    public ServerChunkCache(ServerWorld world, IChunkStorage storage, IChunkSource generator)
    {
        _empty = new EmptyChunk(world, new byte[ChuckFormat.ChunkSize], 0, 0);
        _world = world;
        _storage = storage;
        _generator = generator;
    }

    public WorldGenerationTelemetry GenerationTelemetry { get; } = new();


    public bool IsChunkLoaded(int x, int z) => _chunksByPos.ContainsKey(ChunkPos.GetHashCode(x, z));


    public Chunk LoadChunk(int chunkX, int chunkZ)
    {
        var hash = ChunkPos.GetHashCode(chunkX, chunkZ);
        _chunksToUnload.Remove(hash);
        _chunksByPos.TryGetValue(hash, out var chunk);
        if (chunk == null)
        {
            chunk = LoadChunkFromStorage(chunkX, chunkZ);
            if (chunk == null)
            {
                if (_generator == null)
                {
                    chunk = _empty;
                }
                else
                {
                    chunk = GenerationTelemetry.Measure(
                        WorldGenerationStage.Terrain,
                        () => _generator.GetChunk(chunkX, chunkZ));
                }
            }

            chunk = chunk ?? throw new InvalidOperationException($"Chunk generator returned null for {chunkX},{chunkZ}.");
            ValidateChunkCoordinates(chunk, chunkX, chunkZ);

            _chunksByPos.Add(hash, chunk);
            _chunks.Add(chunk);
            PrepareLoadedChunk(chunk);

            if (!chunk.TerrainPopulated
                && IsChunkLoaded(chunkX + 1, chunkZ + 1)
                && IsChunkLoaded(chunkX, chunkZ + 1)
                && IsChunkLoaded(chunkX + 1, chunkZ))
            {
                DecorateTerrain(this, chunkX, chunkZ);
            }

            if (IsChunkLoaded(chunkX - 1, chunkZ)
                && !GetChunk(chunkX - 1, chunkZ).TerrainPopulated
                && IsChunkLoaded(chunkX - 1, chunkZ + 1)
                && IsChunkLoaded(chunkX, chunkZ + 1)
                && IsChunkLoaded(chunkX - 1, chunkZ))
            {
                DecorateTerrain(this, chunkX - 1, chunkZ);
            }

            if (IsChunkLoaded(chunkX, chunkZ - 1)
                && !GetChunk(chunkX, chunkZ - 1).TerrainPopulated
                && IsChunkLoaded(chunkX + 1, chunkZ - 1)
                && IsChunkLoaded(chunkX, chunkZ - 1)
                && IsChunkLoaded(chunkX + 1, chunkZ))
            {
                DecorateTerrain(this, chunkX, chunkZ - 1);
            }

            if (IsChunkLoaded(chunkX - 1, chunkZ - 1)
                && !GetChunk(chunkX - 1, chunkZ - 1).TerrainPopulated
                && IsChunkLoaded(chunkX - 1, chunkZ - 1)
                && IsChunkLoaded(chunkX, chunkZ - 1)
                && IsChunkLoaded(chunkX - 1, chunkZ))
            {
                DecorateTerrain(this, chunkX - 1, chunkZ - 1);
            }
        }

        return chunk;
    }


    public Chunk GetChunk(int chunkX, int chunkZ)
    {
        _chunksByPos.TryGetValue(ChunkPos.GetHashCode(chunkX, chunkZ), out var chunk);
        if (chunk == null)
        {
            return !_world.IsFindingSpawnPoint && _generationScopes == 0 ? _empty : LoadChunk(chunkX, chunkZ);
        }

        return chunk;
    }


    public void DecorateTerrain(IChunkSource source, int x, int z)
    {
        var chunk = GetChunk(x, z);
        if (!chunk.TerrainPopulated)
        {
            chunk.TerrainPopulated = true;
            if (_generator != null)
            {
                GenerationTelemetry.Measure(
                    WorldGenerationStage.Decoration,
                    () => _generator.DecorateTerrain(source, x, z));
                chunk.MarkDirty();
                _world.ChunkMap?.OnChunkDecorated(x, z);
            }
        }
    }

    public bool Save(bool saveEntities, LoadingDisplay? display)
    {
        var savedChunkCount = 0;

        for (var chunkIndex = 0; chunkIndex < _chunks.Count; chunkIndex++)
        {
            var chunk = _chunks[chunkIndex];
            if (saveEntities && !chunk.IsEmpty())
            {
                this.saveEntities(chunk);
            }

            if (chunk.ShouldSave(saveEntities))
            {
                saveChunk(chunk);
                chunk.Dirty = false;
                if (++savedChunkCount == 24 && !saveEntities)
                {
                    return false;
                }
            }
        }

        if (saveEntities)
        {
            if (_storage == null)
            {
                return true;
            }

            _storage.Flush();
        }

        return true;
    }


    public bool Tick()
    {
        _terrainLod?.Tick();
        if (!_world.savingDisabled)
        {
            for (var unloadIndex = 0; unloadIndex < 100; unloadIndex++)
            {
                if (_chunksToUnload.Count > 0)
                {
                    var chunkHash = _chunksToUnload.First();
                    var chunk = _chunksByPos[chunkHash];
                    _terrainLod?.UntrackChunk(chunk);
                    GenerationTelemetry.Measure(WorldGenerationStage.Unload, chunk.Unload);
                    saveChunk(chunk);
                    saveEntities(chunk);
                    _chunksToUnload.Remove(chunkHash);
                    _chunksByPos.Remove(chunkHash);
                    _chunks.Remove(chunk);
                    if (!ReferenceEquals(chunk, _empty))
                    {
                        _retainedChunks--;
                        _retainedPayloadBytes -= EstimatePayloadBytes(chunk);
                    }
                    UpdateResidencyTelemetry();
                }
            }

            _storage?.Tick();
        }

        return _generator.Tick();
    }


    public bool CanSave() => !_world.savingDisabled;

    public string GetDebugInfo()
    {
        var snapshot = GenerationTelemetry.Snapshot();
        return $"ServerChunkCache: {snapshot.RetainedChunks} chunks, " +
               $"queue {snapshot.Queued}/{snapshot.InFlight}/{snapshot.Ready}, " +
               $"payload>={snapshot.RetainedPayloadBytes}B";
    }

    public void isLoaded(int chunkX, int chunkZ)
    {
        var spawnPos = _world.Properties.GetSpawnPos();
        var deltaX = chunkX * 16 + 8 - spawnPos.X;
        var deltaZ = chunkZ * 16 + 8 - spawnPos.Z;
        short spawnRadius = 128;
        if (deltaX < -spawnRadius || deltaX > spawnRadius || deltaZ < -spawnRadius || deltaZ > spawnRadius)
        {
            _chunksToUnload.Add(ChunkPos.GetHashCode(chunkX, chunkZ));
        }
    }

    private Chunk? LoadChunkFromStorage(int chunkX, int chunkZ)
    {
        if (_storage == null)
        {
            return null;
        }

        try
        {
            var loadedChunk = GenerationTelemetry.Measure(WorldGenerationStage.StorageDecode, () =>
            {
                lock (_storageLoadLock)
                {
                    return _storage.LoadChunk(_world, chunkX, chunkZ);
                }
            });

            loadedChunk?.LastSaveTime = _world.GetTime();

            return loadedChunk;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception");
            return null;
        }
    }

    private void saveEntities(Chunk chunk)
    {
        if (_storage != null)
        {
            try
            {
                _storage.SaveEntities(_world, chunk);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception");
            }
        }
    }

    private void saveChunk(Chunk chunk)
    {
        if (_storage != null)
        {
            try
            {
                chunk.LastSaveTime = _world.GetTime();
                _terrainLod?.BeforeChunkSave(chunk);
                GenerationTelemetry.Measure(
                    WorldGenerationStage.EncodeSave,
                    () => _storage.SaveChunk(_world, chunk, null, -1));
                if (_terrainLod is { } lod)
                    _world.BroadcastTerrainLodRefresh(lod.NotifyChunkSaved(chunk));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception");
            }
        }
    }

    /// Creates a parallel-safe generator instance for off-thread terrain generation.
    /// Null if this cache's world has no generator (e.g. storage-only dimensions).
    public IChunkSource? CreateParallelGenerator() => _generator?.CreateParallelInstance();

    /// <summary>
    ///     Loads from storage or generates a chunk without touching any cache state, so it is
    ///     safe to call from a worker thread given a per-thread generator from
    ///     <see cref="CreateParallelGenerator" />. Pair with <see cref="InsertLoadedChunk" /> on
    ///     the tick thread to apply the result — mirrors <see cref="LoadChunk" />'s split into a
    ///     produce step and an apply step, the same split the spawn-region pregen path already
    ///     relies on via <see cref="InsertPreGeneratedChunk" />.
    /// </summary>
    public Chunk LoadOrGenerateChunkOffThread(int chunkX, int chunkZ, IChunkSource? generator) =>
        LoadChunkFromStorage(chunkX, chunkZ) ??
        (generator is null
            ? _empty
            : GenerationTelemetry.Measure(
                WorldGenerationStage.Terrain,
                () => generator.GetChunk(chunkX, chunkZ)));

    /// <summary>
    ///     Inserts a chunk produced by <see cref="LoadOrGenerateChunkOffThread" />, running the
    ///     same post-load bookkeeping <see cref="LoadChunk" /> does inline for an already-produced
    ///     chunk: cache insert, light populate, and the 4-neighbour decoration cascade.
    /// </summary>
    public void InsertLoadedChunk(int chunkX, int chunkZ, Chunk chunk)
    {
        var hash = ChunkPos.GetHashCode(chunkX, chunkZ);
        if (_chunksByPos.ContainsKey(hash))
        {
            return;
        }

        ValidateChunkCoordinates(chunk, chunkX, chunkZ);
        _chunksToUnload.Remove(hash);
        _chunksByPos.Add(hash, chunk);
        _chunks.Add(chunk);
        PrepareLoadedChunk(chunk);
        DecorateIfReady(chunkX, chunkZ);
    }

    // Inserts a pre-generated chunk without triggering terrain re-generation.
    // Checks storage first so that saved data is used correctly on server restart.
    public void InsertPreGeneratedChunk(int chunkX, int chunkZ, Chunk generatedChunk)
    {
        var key = ChunkPos.GetHashCode(chunkX, chunkZ);
        _chunksToUnload.Remove(key);
        if (_chunksByPos.ContainsKey(key)) return;
        var chunk = LoadChunkFromStorage(chunkX, chunkZ) ?? generatedChunk;
        ValidateChunkCoordinates(chunk, chunkX, chunkZ);
        _chunksByPos.Add(key, chunk);
        _chunks.Add(chunk);
        PrepareLoadedChunk(chunk);
    }

    private static void ValidateChunkCoordinates(Chunk chunk, int expectedX, int expectedZ)
    {
        if (!chunk.ChunkPosEquals(expectedX, expectedZ))
        {
            throw new InvalidDataException(
                $"Refusing to cache chunk {chunk.X},{chunk.Z} under key {expectedX},{expectedZ}.");
        }
    }

    private void PrepareLoadedChunk(Chunk chunk)
    {
        GenerationTelemetry.Measure(WorldGenerationStage.LightInitialization, chunk.PopulateBlockLight);
        GenerationTelemetry.Measure(WorldGenerationStage.Activation, chunk.Load);
        if (!ReferenceEquals(chunk, _empty))
        {
            _retainedChunks++;
            _retainedPayloadBytes += EstimatePayloadBytes(chunk);
            _terrainLod?.TrackChunk(chunk);
        }
        UpdateResidencyTelemetry();
    }

    private void UpdateResidencyTelemetry() =>
        GenerationTelemetry.SetResidency(_retainedChunks, _retainedPayloadBytes);

    internal void AttachTerrainLod(ServerTerrainLodRuntime? terrainLod)
    {
        _terrainLod = terrainLod;
        if (terrainLod is null) return;
        foreach (var chunk in _chunks)
        {
            if (!ReferenceEquals(chunk, _empty)) terrainLod.TrackChunk(chunk);
        }
    }

    /// <summary>
    ///     Counts the large, directly owned chunk buffers. Managed object/list overhead is excluded,
    ///     so the gauge is a stable lower bound rather than a claim about total process memory.
    /// </summary>
    internal static long EstimatePayloadBytes(Chunk chunk) =>
        chunk.Blocks.LongLength + chunk.Meta.Bytes.LongLength + chunk.SkyLight.Bytes.LongLength +
        chunk.BlockLight.Bytes.LongLength + chunk.HeightMap.LongLength;

    // Runs the 4 decoration neighbour checks for a newly inserted chunk,
    // mirroring the logic in LoadChunk but without re-generating terrain.

    public void DecorateIfReady(int chunkX, int chunkZ)
    {
        if (!IsChunkLoaded(chunkX, chunkZ)) return;

        if (!GetChunk(chunkX, chunkZ).TerrainPopulated
            && IsChunkLoaded(chunkX + 1, chunkZ + 1)
            && IsChunkLoaded(chunkX, chunkZ + 1)
            && IsChunkLoaded(chunkX + 1, chunkZ))
            DecorateTerrain(this, chunkX, chunkZ);

        if (IsChunkLoaded(chunkX - 1, chunkZ)
            && !GetChunk(chunkX - 1, chunkZ).TerrainPopulated
            && IsChunkLoaded(chunkX - 1, chunkZ + 1)
            && IsChunkLoaded(chunkX, chunkZ + 1)
            && IsChunkLoaded(chunkX - 1, chunkZ))
            DecorateTerrain(this, chunkX - 1, chunkZ);

        if (IsChunkLoaded(chunkX, chunkZ - 1)
            && !GetChunk(chunkX, chunkZ - 1).TerrainPopulated
            && IsChunkLoaded(chunkX + 1, chunkZ - 1)
            && IsChunkLoaded(chunkX, chunkZ - 1)
            && IsChunkLoaded(chunkX + 1, chunkZ))
            DecorateTerrain(this, chunkX, chunkZ - 1);

        if (IsChunkLoaded(chunkX - 1, chunkZ - 1)
            && !GetChunk(chunkX - 1, chunkZ - 1).TerrainPopulated
            && IsChunkLoaded(chunkX - 1, chunkZ - 1)
            && IsChunkLoaded(chunkX, chunkZ - 1)
            && IsChunkLoaded(chunkX - 1, chunkZ))
            DecorateTerrain(this, chunkX - 1, chunkZ - 1);
    }

    /// <summary>
    ///     Lets reads generate chunks for as long as the returned scope is held.
    /// </summary>
    /// <remarks>
    ///     A scope rather than a flag a caller sets and clears by hand. The one caller that needs
    ///     this builds a portal, which can throw, and a flag left set turns "reading the world
    ///     generates terrain" on permanently and silently — which is the opposite of what the
    ///     default is careful to prevent.
    /// </remarks>
    public GenerationScope AllowGenerationOnRead() => new(this);

    /// <summary>Counted, so nesting two scopes does not have the inner one end both.</summary>
    public readonly struct GenerationScope : IDisposable
    {
        private readonly ServerChunkCache _cache;

        internal GenerationScope(ServerChunkCache cache)
        {
            _cache = cache;
            cache._generationScopes++;
        }

        public void Dispose() => _cache._generationScopes--;
    }
}
