using Microsoft.Extensions.Logging;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Server.Worlds;

public sealed record ServerTerrainLodSnapshot(
    int Dimension,
    int TrackedChunks,
    int DirtyChunks,
    long TerrainChangesObserved,
    long SnapshotsSubmitted,
    long SnapshotSubmissionsCoalesced,
    long SnapshotSubmissionsDeferred,
    long UnloadSnapshotsDropped,
    long OfflineSnapshotsSubmitted,
    long OfflineSnapshotsDropped,
    long PipelineFailures,
    string? LastPipelineFailure,
    TerrainLodConversionSnapshot Conversion,
    TerrainLodCacheSnapshot Cache,
    TerrainLodCacheWriterSnapshot Writer);

/// <summary>
///     Per-dimension lifecycle owner joining live chunks to the bounded converter and disposable
///     persistent cache. Live arrays are copied only on the server thread; conversion and writes
///     happen on background workers.
/// </summary>
internal sealed class ServerTerrainLodRuntime : IDisposable
{
    private const int ConversionCapacity = 64;
    private const int DefaultSnapshotsPerTick = 2;
    private const int QuietTicks = 2;
    private const int MaximumDirtyTicks = 20;
    private const int WritesPerDrain = 4;

    private readonly object _gate = new();
    private readonly ILogger<ServerTerrainLodRuntime> _logger =
        Log.Instance.For<ServerTerrainLodRuntime>();
    private readonly int _dimension;
    private readonly int _conversionCapacity;
    private readonly Dictionary<ChunkKey, TrackedChunk> _tracked = [];
    private readonly TerrainLodConversionService _conversions;
    private readonly TerrainLodCacheStore _cache;
    private readonly TerrainLodCacheWriter _writer;
    private readonly AutoResetEvent _writerWake = new(false);
    private readonly Thread _writerThread;
    private bool _disposed;
    private bool _stopWriter;
    private long _tick;
    private long _terrainChangesObserved;
    private long _snapshotsSubmitted;
    private long _snapshotSubmissionsCoalesced;
    private long _snapshotSubmissionsDeferred;
    private long _unloadSnapshotsDropped;
    private long _offlineSnapshotsSubmitted;
    private long _offlineSnapshotsDropped;
    private long _pipelineFailures;
    private string? _lastPipelineFailure;
    private ServerTerrainLodSnapshot _publishedSnapshot = null!;

    private ServerTerrainLodRuntime(ServerWorld world, DirectoryInfo cacheRoot)
        : this(
            world.Dimension.Id,
            TerrainLodMaterialCatalog.FromRuntime(world.Content),
            cacheRoot,
            world)
    {
    }

    internal ServerTerrainLodRuntime(
        int dimension,
        TerrainLodMaterialCatalog materials,
        DirectoryInfo cacheRoot,
        IWorldContext identitySource,
        int conversionCapacity = ConversionCapacity)
    {
        if (conversionCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(conversionCapacity));
        _dimension = dimension;
        _conversionCapacity = conversionCapacity;
        var identity = TerrainLodCacheIdentity.FromWorld(identitySource, materials);
        if (identity.Dimension != dimension)
            throw new ArgumentException(
                $"Identity world dimension {identity.Dimension} does not match {dimension}.",
                nameof(identitySource));
        _cache = new TerrainLodCacheStore(cacheRoot, identity);
        _conversions = new TerrainLodConversionService(
            _dimension,
            conversionCapacity,
            source =>
            {
                var cached = _cache.Read(
                    source.ChunkX, source.ChunkZ, source.TerrainRevision);
                return cached.Status == TerrainLodCacheReadStatus.Hit
                    ? new TerrainLodConversionOutput(cached.Hierarchy!, false)
                    : new TerrainLodConversionOutput(TerrainLodReducer.Build(
                        source,
                        materials,
                        TerrainLodReductionStrategy.SurfacePreserving));
            });
        _writer = new TerrainLodCacheWriter(_conversions, _cache);
        PublishSnapshotLocked();
        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = $"TerrainLOD-Writer-{_dimension}"
        };
        _writerThread.Start();
    }

    public static ServerTerrainLodRuntime? TryCreate(ServerWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        try
        {
            var root = world.GetWorldStorage().GetTerrainLodCacheDirectory();
            return root is null ? null : new ServerTerrainLodRuntime(world, root);
        }
        catch (Exception error)
        {
            Log.Instance.For<ServerTerrainLodRuntime>().LogWarning(
                error,
                "Distant terrain cache is unavailable for dimension {Dimension}; " +
                "gameplay will continue without it.",
                world.Dimension.Id);
            return null;
        }
    }

    public ServerTerrainLodSnapshot Snapshot() => Volatile.Read(ref _publishedSnapshot);

    public void TrackChunk(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.IsEmpty()) return;
        lock (_gate)
        {
            if (_disposed) return;
            var key = new ChunkKey(chunk.X, chunk.Z);
            if (_tracked.TryGetValue(key, out var current))
            {
                if (ReferenceEquals(current.Chunk, chunk)) return;
                current.Chunk.TerrainChanged -= OnTerrainChanged;
            }
            var state = new TrackedChunk(chunk, _tick, _tick + 1);
            _tracked[key] = state;
            chunk.TerrainChanged += OnTerrainChanged;
            PublishSnapshotLocked();
        }
    }

    public void UntrackChunk(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        lock (_gate)
        {
            var key = new ChunkKey(chunk.X, chunk.Z);
            if (!_tracked.TryGetValue(key, out var state) ||
                !ReferenceEquals(state.Chunk, chunk)) return;
            _tracked.Remove(key);
            chunk.TerrainChanged -= OnTerrainChanged;
            // Unload can remove up to 100 chunks in one server tick. Capturing every last edit here
            // would defeat the per-tick snapshot budget; stale cache data is harmless and will be
            // rejected by the persisted chunk revision when this coordinate is loaded again.
            if (state.LastSubmittedRevision != chunk.TerrainRevision || state.Dirty)
                _unloadSnapshotsDropped++;
            PublishSnapshotLocked();
        }
    }

    /// <summary>Captures a small, deterministic amount of immutable work on the server tick.</summary>
    public void Tick(int maxSnapshots = DefaultSnapshotsPerTick)
    {
        if (maxSnapshots <= 0) throw new ArgumentOutOfRangeException(nameof(maxSnapshots));
        TrackedChunk[] due;
        lock (_gate)
        {
            if (_disposed) return;
            _tick++;
            due = _tracked.Values
                .Where(state => state.Dirty && state.DueTick <= _tick)
                .OrderBy(static state => state.DueTick)
                .ThenBy(static state => state.Chunk.X)
                .ThenBy(static state => state.Chunk.Z)
                .Take(maxSnapshots)
                .ToArray();
            foreach (var state in due) state.Dirty = false;
        }

        foreach (var state in due)
        {
            try
            {
                var snapshot = TerrainLodSourceSnapshot.Capture(state.Chunk);
                var admission = _conversions.Submit(snapshot);
                lock (_gate)
                {
                    if (admission == TerrainLodAdmissionResult.RejectedAtCapacity)
                    {
                        state.Dirty = true;
                        state.DueTick = _tick + 1;
                        _snapshotSubmissionsDeferred++;
                    }
                    else
                    {
                        state.LastSubmittedRevision = snapshot.TerrainRevision;
                        RecordAdmissionLocked(admission);
                    }
                    PublishSnapshotLocked();
                }
                _writerWake.Set();
            }
            catch (Exception error)
            {
                RecordPipelineFailure(error, state.Chunk.X, state.Chunk.Z);
            }
        }
    }

    public bool Retry(int chunkX, int chunkZ)
    {
        var retried = _conversions.Retry(chunkX, chunkZ);
        if (retried) _writerWake.Set();
        return retried;
    }

    /// <summary>
    ///     Accepts a just-committed inactive-generation snapshot. Parsing and copying happen on
    ///     that job's worker, never on the simulation tick.
    /// </summary>
    public void SubmitOffline(InactiveChunkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_gate)
        {
            if (_disposed) return;
        }
        try
        {
            var admission = _conversions.Submit(snapshot.CaptureTerrain());
            lock (_gate)
            {
                if (admission is TerrainLodAdmissionResult.RejectedAtCapacity or
                    TerrainLodAdmissionResult.RejectedStaleRevision)
                    _offlineSnapshotsDropped++;
                else
                {
                    _offlineSnapshotsSubmitted++;
                    RecordAdmissionLocked(admission);
                }
                PublishSnapshotLocked();
            }
            _writerWake.Set();
        }
        catch (Exception error)
        {
            RecordPipelineFailure(error, snapshot.X, snapshot.Z);
        }
    }

    public void Shutdown(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            Tick(_conversionCapacity);
            _writerWake.Set();
            var conversion = _conversions.Snapshot();
            bool hasDirty;
            lock (_gate) hasDirty = _tracked.Values.Any(static state => state.Dirty);
            if (!hasDirty && conversion.Queued == 0 && conversion.Running == 0 &&
                conversion.Ready == 0) break;
            Thread.Sleep(5);
        }
        Dispose();
    }

    private void OnTerrainChanged(Chunk chunk)
    {
        lock (_gate)
        {
            if (_disposed || !_tracked.TryGetValue(
                    new ChunkKey(chunk.X, chunk.Z), out var state) ||
                !ReferenceEquals(state.Chunk, chunk)) return;
            _terrainChangesObserved++;
            if (!state.Dirty)
            {
                state.Dirty = true;
                state.FirstDirtyTick = _tick;
            }
            state.DueTick = Math.Min(
                _tick + QuietTicks,
                state.FirstDirtyTick + MaximumDirtyTicks);
            PublishSnapshotLocked();
        }
    }

    private void RecordAdmissionLocked(TerrainLodAdmissionResult admission)
    {
        if (admission == TerrainLodAdmissionResult.Accepted) _snapshotsSubmitted++;
        else if (admission == TerrainLodAdmissionResult.Coalesced)
            _snapshotSubmissionsCoalesced++;
    }

    private void RecordPipelineFailure(Exception error, int chunkX, int chunkZ)
    {
        lock (_gate)
        {
            _pipelineFailures++;
            _lastPipelineFailure = error.GetBaseException().Message;
            PublishSnapshotLocked();
        }
        _logger.LogWarning(
            error,
            "Distant terrain snapshot {ChunkX},{ChunkZ} was skipped; gameplay terrain is unaffected.",
            chunkX,
            chunkZ);
    }

    private void WriterLoop()
    {
        while (true)
        {
            lock (_gate)
            {
                if (_stopWriter) return;
            }
            var failuresBefore = _writer.Snapshot().Failures;
            var consumed = _writer.Drain(WritesPerDrain);
            lock (_gate) PublishSnapshotLocked();
            if (_writer.Snapshot().Failures != failuresBefore)
                _writerWake.WaitOne(TimeSpan.FromMilliseconds(250));
            else if (consumed == 0)
                _writerWake.WaitOne(TimeSpan.FromMilliseconds(50));
        }
    }

    private void PublishSnapshotLocked() => Volatile.Write(ref _publishedSnapshot,
        new ServerTerrainLodSnapshot(
            _dimension,
            _tracked.Count,
            _tracked.Values.Count(static state => state.Dirty),
            _terrainChangesObserved,
            _snapshotsSubmitted,
            _snapshotSubmissionsCoalesced,
            _snapshotSubmissionsDeferred,
            _unloadSnapshotsDropped,
            _offlineSnapshotsSubmitted,
            _offlineSnapshotsDropped,
            _pipelineFailures,
            _lastPipelineFailure,
            _conversions.Snapshot(),
            _cache.Snapshot(),
            _writer.Snapshot()));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var state in _tracked.Values)
                state.Chunk.TerrainChanged -= OnTerrainChanged;
            _tracked.Clear();
            _stopWriter = true;
            PublishSnapshotLocked();
        }
        _writerWake.Set();
        if (_writerThread != Thread.CurrentThread)
            _writerThread.Join(TimeSpan.FromSeconds(5));
        _conversions.Dispose();
        _writerWake.Dispose();
    }

    private readonly record struct ChunkKey(int X, int Z);

    private sealed class TrackedChunk(Chunk chunk, long firstDirtyTick, long dueTick)
    {
        public Chunk Chunk { get; } = chunk;
        public long FirstDirtyTick { get; set; } = firstDirtyTick;
        public long DueTick { get; set; } = dueTick;
        public long LastSubmittedRevision { get; set; } = -1;
        public bool Dirty { get; set; } = true;
    }
}
