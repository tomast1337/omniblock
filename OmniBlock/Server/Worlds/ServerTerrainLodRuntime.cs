using Microsoft.Extensions.Logging;
using OmniBlock.Network.Messages;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Lod;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Server.Worlds;

internal enum TerrainLodTileAvailability
{
    Ready,
    Pending,
    Missing
}

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
    TerrainLodCacheWriterSnapshot Writer,
    TerrainLodColumnTileCacheSnapshot SpatialCache,
    TerrainLodSpatialHierarchyCoordinatorSnapshot SpatialHierarchy,
    int SpatialReadQueued,
    int SpatialReadPending,
    int SpatialEncodeQueued,
    int SpatialEncodePending,
    int SpatialWirePayloads,
    int SavedTileImportsPending,
    long SavedChunksImported,
    long SavedTileImportsMissing,
    long SavedTileImportsFailed)
{
    // Observational only: asynchronous stages publish independently. Consumers should require
    // a quiet interval after generation completes, not treat a single sample as a flush barrier.
    // Missing siblings outside the generated patch are not pending construction work.
    public int PreparationPendingWork => DirtyChunks + Conversion.OwnedChunks +
        SpatialHierarchy.Construction.Owned + SpatialHierarchy.DeferredParents +
        (SpatialHierarchy.Persistence?.Queued ?? 0) +
        (SpatialHierarchy.Persistence?.Running ?? 0);

    public long PreparationFailureEvents => PipelineFailures + Conversion.FailedConversions +
        Writer.Failures + Writer.Rejected + SpatialHierarchy.Construction.FailedConstructions +
        (SpatialHierarchy.Persistence?.Failed ?? 0);
}

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
    private const int MaximumPendingSavedTileImports = 32;
    private const int DefaultTransientImportMinimumLevel = 6;
    // Dormant 512/1024+ scale profiles have not passed cold saved-source I/O gates.
    private const int MaximumTransientImportLevel = TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel;

    private readonly object _gate = new();
    private readonly ILogger<ServerTerrainLodRuntime> _logger =
        Log.Instance.For<ServerTerrainLodRuntime>();
    private readonly int _dimension;
    private readonly TerrainLodCacheIdentity _identity;
    private readonly TerrainLodMaterialCatalog _materials;
    private readonly TerrainLodSpatialPolicy _spatialPolicy;
    private readonly int _conversionCapacity;
    private readonly int _transientImportMinimumLevel;
    private readonly Dictionary<ChunkKey, TrackedChunk> _tracked = [];
    private readonly TerrainLodConversionService _conversions;
    private readonly TerrainLodCacheStore _cache;
    private readonly TerrainLodCacheWriter _writer;
    private readonly TerrainLodColumnTileCacheStore _spatialCache;
    private readonly TerrainLodSpatialHierarchyCoordinator _spatialHierarchy;
    private readonly IChunkStorage? _storedTerrain;
    private readonly bool _hasSkyLight;
    private readonly List<TerrainLodColumnTile> _completedSpatialParents = [];
    private readonly Queue<TerrainLodTileKey> _spatialReadRequests = [];
    private readonly HashSet<TerrainLodTileKey> _spatialReadsPending = [];
    private readonly HashSet<TerrainLodTileKey> _spatialMissing = [];
    private readonly Queue<SavedTileImport> _savedTileImportQueue = [];
    private readonly Dictionary<TerrainLodTileKey, SavedTileImport> _savedTileImports = [];
    private readonly Queue<TerrainLodTileKey> _spatialEncodeRequests = [];
    private readonly HashSet<TerrainLodTileKey> _spatialEncodesPending = [];
    private readonly Dictionary<TerrainLodTileKey, byte[]> _spatialWirePayloads = [];
    private readonly Queue<TerrainLodTileKey> _spatialWirePayloadOrder = [];
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
    private long _savedChunksImported;
    private long _savedTileImportsMissing;
    private long _savedTileImportsFailed;
    private string? _lastPipelineFailure;
    private ServerTerrainLodSnapshot _publishedSnapshot = null!;

    private ServerTerrainLodRuntime(
        ServerWorld world,
        DirectoryInfo cacheRoot,
        TerrainLodSpatialPolicy spatialPolicy)
        : this(
            world.Dimension.Id,
            TerrainLodMaterialCatalog.FromRuntime(world.Content),
            cacheRoot,
            world,
            storedTerrain: world.GetWorldStorage().GetChunkStorage(world.Dimension),
            spatialPolicy: spatialPolicy)
    {
    }

    internal ServerTerrainLodRuntime(
        int dimension,
        TerrainLodMaterialCatalog materials,
        DirectoryInfo cacheRoot,
        IWorldContext identitySource,
        int conversionCapacity = ConversionCapacity,
        TerrainLodSpatialPolicy? spatialPolicy = null,
        IChunkStorage? storedTerrain = null,
        int transientImportMinimumLevel = DefaultTransientImportMinimumLevel)
    {
        if (conversionCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(conversionCapacity));
        _dimension = dimension;
        _materials = materials;
        _spatialPolicy = spatialPolicy ?? TerrainLodSpatialPolicy.CreateDefault();
        _conversionCapacity = conversionCapacity;
        if (transientImportMinimumLevel is < 3 or > 10)
            throw new ArgumentOutOfRangeException(nameof(transientImportMinimumLevel));
        _transientImportMinimumLevel = transientImportMinimumLevel;
        _storedTerrain = storedTerrain;
        _hasSkyLight = !identitySource.Dimension.HasCeiling;
        // The absolute cache root never crosses the wire; only its hash does. Including it keeps
        // two saves with the same seed/content from sharing a transport identity, while reopening
        // this save remains stable.
        _identity = TerrainLodCacheIdentity.FromWorld(
            identitySource, materials, cacheRoot.FullName, _spatialPolicy);
        if (_identity.Dimension != dimension)
            throw new ArgumentException(
                $"Identity world dimension {_identity.Dimension} does not match {dimension}.",
                nameof(identitySource));
        _cache = new TerrainLodCacheStore(cacheRoot, _identity);
        _spatialCache = new TerrainLodColumnTileCacheStore(cacheRoot, _identity);
        _spatialHierarchy = new TerrainLodSpatialHierarchyCoordinator(
            _spatialPolicy,
            _spatialCache,
            tileCapacity: TerrainLodScaleBudget.ServerHierarchyTiles,
            constructionCapacity: 128,
            completedCapacity: 32,
            persistenceCapacity: 64);
        _conversions = new TerrainLodConversionService(
            _dimension,
            conversionCapacity,
            source =>
            {
                var cached = _cache.Read(
                    source.ChunkX, source.ChunkZ, source.TerrainRevision,
                    source.SourceFingerprint);
                return cached.Status == TerrainLodCacheReadStatus.Hit
                    ? new TerrainLodConversionOutput(
                        cached.Hierarchy!, cached.Lighting, RequiresPersistence: false)
                    : new TerrainLodConversionOutput(TerrainLodReducer.Build(
                        source,
                        materials,
                        TerrainLodReductionStrategy.SurfacePreserving), source.Lighting);
            },
            source => source.Width == 16 && source.Depth == 16
                ? TerrainLodColumnTile.BuildLeaf(source, materials)
                : null);
        _writer = new TerrainLodCacheWriter(
            _conversions,
            _cache.Write,
            PublishSpatialLeaf);
        PublishSnapshotLocked();
        _writerThread = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = $"TerrainLOD-Writer-{_dimension}"
        };
        _writerThread.Start();
    }

    public static ServerTerrainLodRuntime? TryCreate(
        ServerWorld world,
        TerrainLodSpatialPolicy? spatialPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        try
        {
            var root = world.GetWorldStorage().GetTerrainLodCacheDirectory();
            return root is null
                ? null
                : new ServerTerrainLodRuntime(
                    world, root, spatialPolicy ?? TerrainLodSpatialPolicy.CreateDefault());
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
    public TerrainLodCacheIdentity Identity => _identity;

    /// <summary>
    ///     Returns already-resident server-approved coarse coverage without loading a gameplay
    ///     chunk or touching disk on the simulation thread. A cold persistent record is queued for
    ///     the LOD worker and becomes available to a later bounded client retry.
    /// </summary>
    public TerrainLodTileAvailability GetSpatialCoverage(
        TerrainLodTileKey key,
        out TerrainLodColumnTile? tile)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                tile = null;
                return TerrainLodTileAvailability.Missing;
            }
        }
        if (_spatialHierarchy.TryGetCoverage(key, out tile, out _))
        {
            lock (_gate) _spatialMissing.Remove(key);
            return TerrainLodTileAvailability.Ready;
        }
        lock (_gate)
        {
            if (_savedTileImports.ContainsKey(key))
            {
                tile = null;
                return TerrainLodTileAvailability.Pending;
            }
            if (_spatialMissing.Contains(key))
            {
                tile = null;
                return TerrainLodTileAvailability.Missing;
            }
            if (!_disposed && _spatialReadsPending.Add(key))
            {
                _spatialReadRequests.Enqueue(key);
                _writerWake.Set();
            }
        }
        tile = null;
        return TerrainLodTileAvailability.Pending;
    }

    internal bool TryGetSpatialCoverage(TerrainLodTileKey key, out TerrainLodColumnTile? tile) =>
        GetSpatialCoverage(key, out tile) == TerrainLodTileAvailability.Ready;

    /// <summary>Returns a pre-encoded remote payload or queues encoding on the LOD worker.</summary>
    public TerrainLodTileAvailability GetSpatialPayload(TerrainLodTileKey key, out byte[]? payload)
    {
        lock (_gate)
        {
            if (_spatialWirePayloads.TryGetValue(key, out payload))
                return TerrainLodTileAvailability.Ready;
            if (_disposed)
            {
                payload = null;
                return TerrainLodTileAvailability.Missing;
            }
        }
        if (_spatialHierarchy.TryGetCoverage(key, out _, out _)) QueueSpatialEncode(key);
        else
        {
            var availability = GetSpatialCoverage(key, out _);
            if (availability == TerrainLodTileAvailability.Missing)
            {
                payload = null;
                return availability;
            }
        }
        payload = null;
        return TerrainLodTileAvailability.Pending;
    }

    internal bool TryGetSpatialPayload(TerrainLodTileKey key, out byte[]? payload) =>
        GetSpatialPayload(key, out payload) == TerrainLodTileAvailability.Ready;

    /// <summary>
    ///     Prepares a bounded uniform cache fixture through the same durable store and hierarchy
    ///     used by ordinary pregeneration. The integrated-server E2E harness calls this before its
    ///     timed scale sample; no gameplay chunks are synthesized or activated.
    /// </summary>
    internal int PrepareUniformSpatialFixture(
        double centerChunkX,
        double centerChunkZ,
        int nearDistanceChunks,
        int horizonDistanceChunks,
        int surfaceBlockProtocolId)
    {
        if (horizonDistanceChunks <= 0 ||
            horizonDistanceChunks > TerrainLodSpatialPolicy.MaximumHorizonChunksForSpatialLevel(
                _spatialPolicy.MaximumSpatialLevel))
            throw new ArgumentOutOfRangeException(nameof(horizonDistanceChunks));
        const int minimumLevel = 2;
        var rootLevel = Math.Max(
            minimumLevel,
            _spatialPolicy.DesiredSpatialLevel(horizonDistanceChunks));
        var outerBoundaryMinimumLevel =
            TerrainLodCoveragePlanner.RecommendedOuterBoundaryMinimumLevel(
                rootLevel, minimumLevel);
        var coveragePlan = TerrainLodCoveragePlanner.PlanRequiredTiles(
            centerChunkX,
            centerChunkZ,
            nearDistanceChunks,
            horizonDistanceChunks,
            rootLevel,
            minimumLevel,
            outerBoundaryMinimumLevel);
        var surface = _materials.Resolve(surfaceBlockProtocolId, metadata: 0);
        var column = TerrainLodColumn.Create(ChuckFormat.WorldHeight,
        [
            new TerrainLodColumnSpan(0, 64, surface, blockLight: 0, skyLight: 0),
            new TerrainLodColumnSpan(
                64, ChuckFormat.WorldHeight - 64,
                TerrainLodMaterial.Air, blockLight: 0, skyLight: 15)
        ]);
        // A route prepares overlapping partitions. The same uniform tile must retain the same
        // identity irrespective of which camera center requested it.
        const string sourceIdentity = "integrated-scale-fixture-v2";
        foreach (var key in coveragePlan.Tiles)
        {
            var tile = TerrainLodColumnTile.CreateUniform(
                key,
                _spatialPolicy.HorizontalSampleLevelForSpatialLevel(key.Level),
                ChuckFormat.WorldHeight,
                column,
                sourceIdentity);
            _spatialCache.Write(tile);
            _spatialHierarchy.PublishCached(tile);
            lock (_gate)
            {
                _spatialMissing.Remove(key);
                _spatialWirePayloads.Remove(key);
            }
        }
        lock (_gate) PublishSnapshotLocked();
        return coveragePlan.Tiles.Count;
    }

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

    /// <summary>
    ///     Awaited by inactive-generation workers after the terrain commit. Keep the producer's
    ///     existing bounded batch alive until admission rather than silently dropping its LOD.
    ///     Never call this synchronously from the simulation tick.
    /// </summary>
    public async ValueTask SubmitOfflineAsync(
        InactiveChunkSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            var admission = await _conversions.SubmitWhenAvailableAsync(
                snapshot.CaptureTerrain(), cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (admission == TerrainLodAdmissionResult.RejectedStaleRevision)
                    _offlineSnapshotsDropped++;
                else
                {
                    _offlineSnapshotsSubmitted++;
                    RecordAdmissionLocked(admission);
                }
                PublishSnapshotLocked();
                _writerWake.Set();
            }
        }
        catch (Exception error) when (error is not (OperationCanceledException or ObjectDisposedException))
        {
            RecordPipelineFailure(error, snapshot.X, snapshot.Z);
            throw;
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
            var spatial = _spatialHierarchy.Snapshot();
            bool hasDirty;
            lock (_gate) hasDirty = _tracked.Values.Any(static state => state.Dirty);
            if (!hasDirty && conversion.Queued == 0 && conversion.Running == 0 &&
                conversion.Ready == 0 && spatial.Construction.Owned == 0 &&
                spatial.DeferredParents == 0 &&
                (spatial.Persistence?.Queued ?? 0) == 0 &&
                (spatial.Persistence?.Running ?? 0) == 0) break;
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
            _completedSpatialParents.Clear();
            var spatialPublished = _spatialHierarchy.DrainCompleted(
                maximumResults: 16,
                _completedSpatialParents);
            var spatialReads = DrainSpatialReads(maximumReads: 4);
            var savedImports = DrainSavedTileImports(maximumSources: 2);
            CompleteSavedTileImports();
            foreach (var parent in _completedSpatialParents)
                lock (_gate)
                {
                    // Publishing a tile and preparing its remote wire representation are separate
                    // lifecycle steps. Integrated connections consume the immutable tile directly,
                    // and remote clients request compression lazily through GetSpatialPayload().
                    // Eager encoding here made cold-cache reads contend with work that a loopback
                    // session could never consume.
                    _spatialWirePayloads.Remove(parent.Key);
                    _spatialMissing.Remove(parent.Key);
                }
            var spatialEncodes = DrainSpatialEncodes(maximumEncodes: 2);
            lock (_gate) PublishSnapshotLocked();
            if (_writer.Snapshot().Failures != failuresBefore)
                _writerWake.WaitOne(TimeSpan.FromMilliseconds(250));
            else if (consumed == 0 && spatialPublished == 0 && spatialReads == 0 &&
                     savedImports == 0 && spatialEncodes == 0)
                _writerWake.WaitOne(TimeSpan.FromMilliseconds(50));
        }
    }

    private int DrainSpatialReads(int maximumReads)
    {
        var consumed = 0;
        while (consumed < maximumReads)
        {
            TerrainLodTileKey key;
            lock (_gate)
            {
                if (!_spatialReadRequests.TryDequeue(out key)) break;
            }
            try
            {
                var cached = _spatialCache.Read(key);
                if (cached.Status == TerrainLodColumnTileCacheReadStatus.Hit)
                {
                    _spatialHierarchy.PublishCached(cached.Tile!);
                    lock (_gate) _spatialMissing.Remove(key);
                }
                else
                {
                    lock (_gate)
                    {
                        // Reserve slots for the level-two children of in-flight coarse imports.
                        // Filling the queue entirely with parents would otherwise deadlock them.
                        var canAdmit = key.Level == TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel
                            ? _savedTileImports.Count < MaximumPendingSavedTileImports
                            : _savedTileImports.Count < MaximumPendingSavedTileImports - 8 &&
                              _savedTileImports.Values.Count(static import =>
                                  import.Key.Level > TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel) < 8;
                        if (key.Level >= TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel &&
                            key.Level <= Math.Min(_spatialPolicy.MaximumSpatialLevel,
                                MaximumTransientImportLevel) &&
                            _storedTerrain is not null && canAdmit)
                        {
                            var import = new SavedTileImport(key,
                                key.Level >= _transientImportMinimumLevel
                                    ? new TerrainLodTransientTileBuilder(key, _spatialPolicy, _materials)
                                    : null);
                            _savedTileImports.Add(key, import);
                            _savedTileImportQueue.Enqueue(import);
                            _writerWake.Set();
                        }
                        else if (key.Level < TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel ||
                                 key.Level > Math.Min(_spatialPolicy.MaximumSpatialLevel,
                                     MaximumTransientImportLevel) ||
                                 _storedTerrain is null)
                            _spatialMissing.Add(key);
                        // At capacity report Pending; the next request retries admission.
                    }
                }
            }
            finally
            {
                lock (_gate) _spatialReadsPending.Remove(key);
            }
            consumed++;
        }
        return consumed;
    }

    /// <summary>
    ///     Imports at most a few already-saved chunks per pass. Higher-level requests traverse
    ///     their level-two source tiles incrementally, so a cold coarse request does not require
    ///     the client to request thousands of descendants. This work never enters the gameplay
    ///     chunk cache or generator.
    /// </summary>
    private int DrainSavedTileImports(int maximumSources)
    {
        if (_storedTerrain is null) return 0;
        var consumed = 0;
        while (consumed < maximumSources)
        {
            SavedTileImport import;
            lock (_gate)
            {
                if (!_savedTileImportQueue.TryDequeue(out import!)) break;
            }
            try
            {
                if (_spatialHierarchy.TryGetCoverage(import.Key, out _, out _))
                {
                    lock (_gate) _savedTileImports.Remove(import.Key);
                    continue;
                }
                if (import.TransientBuilder is { } builder)
                {
                    var (sourceX, sourceZ) = builder.NextChunkCoordinates();
                    var savedSource = _storedTerrain.ReadTerrainLodSource(
                        sourceX, sourceZ, _hasSkyLight);
                    if (savedSource is null)
                    {
                        FinishSavedTileImport(import, missing: true);
                        continue;
                    }
                    builder.AddSource(savedSource);
                    consumed++;
                    lock (_gate) _savedChunksImported++;
                    if (builder.Result is { } completed)
                    {
                        var write = _spatialCache.Write(completed);
                        if (write != TerrainLodColumnTileCacheWriteStatus.Written)
                            throw new InvalidOperationException(
                                $"Saved terrain tile {import.Key} could not be persisted: {write}.");
                        var publication = _spatialHierarchy.PublishCached(completed);
                        if (publication == TerrainLodTilePublicationResult.RejectedAtCapacity)
                            throw new InvalidOperationException(
                                $"Saved terrain tile {import.Key} exceeded resident hierarchy capacity.");
                        lock (_gate)
                        {
                            _savedTileImports.Remove(import.Key);
                            _spatialMissing.Remove(import.Key);
                            _spatialWirePayloads.Remove(import.Key);
                        }
                    }
                    else
                        lock (_gate) _savedTileImportQueue.Enqueue(import);
                    continue;
                }
                if (import.Key.Level > TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel)
                {
                    var tilesPerSide = 1 <<
                        (import.Key.Level - TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel);
                    var child = new TerrainLodTileKey(
                        TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel,
                        checked(import.Key.X * tilesPerSide +
                                import.NextChunk % tilesPerSide),
                        checked(import.Key.Z * tilesPerSide +
                                import.NextChunk / tilesPerSide));
                    var availability = GetSpatialCoverage(child, out _);
                    if (availability == TerrainLodTileAvailability.Missing)
                    {
                        FinishSavedTileImport(import, missing: true);
                        continue;
                    }
                    if (availability == TerrainLodTileAvailability.Pending)
                    {
                        lock (_gate) _savedTileImportQueue.Enqueue(import);
                        // Wait for this child to publish before requesting the next one.
                        break;
                    }
                    import.NextChunk++;
                    consumed++;
                    lock (_gate)
                    {
                        if (import.NextChunk < tilesPerSide * tilesPerSide)
                            _savedTileImportQueue.Enqueue(import);
                        else
                            import.AwaitingPublicationSince = DateTime.UtcNow;
                    }
                    continue;
                }
                var x = checked((int)import.Key.MinChunkX + (import.NextChunk & 3));
                var z = checked((int)import.Key.MinChunkZ + (import.NextChunk >> 2));
                var source = import.DeferredSource ??
                    _storedTerrain.ReadTerrainLodSource(x, z, _hasSkyLight);
                if (source is null)
                {
                    FinishSavedTileImport(import, missing: true);
                    continue;
                }
                if (source.ChunkX != x || source.ChunkZ != z)
                    throw new InvalidDataException(
                        $"Saved terrain slot {x},{z} returned {source.ChunkX},{source.ChunkZ}.");
                var admission = _conversions.Submit(source);
                if (admission == TerrainLodAdmissionResult.RejectedAtCapacity)
                {
                    import.DeferredSource = source;
                    lock (_gate) _savedTileImportQueue.Enqueue(import);
                    break;
                }
                import.DeferredSource = null;
                import.NextChunk++;
                consumed++;
                lock (_gate)
                {
                    _savedChunksImported++;
                    RecordAdmissionLocked(admission);
                    if (import.NextChunk < 16) _savedTileImportQueue.Enqueue(import);
                    else import.AwaitingPublicationSince = DateTime.UtcNow;
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                                          ArgumentException or InvalidOperationException or
                                          OverflowException)
            {
                FinishSavedTileImport(import, missing: false);
                RecordPipelineFailure(error, checked((int)import.Key.MinChunkX),
                    checked((int)import.Key.MinChunkZ));
            }
        }
        return consumed;
    }

    private void CompleteSavedTileImports()
    {
        SavedTileImport[] waiting;
        lock (_gate)
            waiting = _savedTileImports.Values
                .Where(static import => import.AwaitingPublicationSince is not null)
                .ToArray();
        foreach (var import in waiting)
        {
            if (_spatialHierarchy.TryGetCoverage(import.Key, out _, out _))
            {
                lock (_gate)
                {
                    _savedTileImports.Remove(import.Key);
                    _spatialMissing.Remove(import.Key);
                }
            }
            else if (DateTime.UtcNow - import.AwaitingPublicationSince!.Value >
                     TimeSpan.FromMinutes(2))
            {
                // A failed conversion or tile-capacity rejection must not leave a client
                // waiting forever. A later live chunk update can still publish the parent.
                FinishSavedTileImport(import, missing: false);
            }
        }
    }

    private void FinishSavedTileImport(SavedTileImport import, bool missing)
    {
        lock (_gate)
        {
            if (!_savedTileImports.Remove(import.Key)) return;
            _spatialMissing.Add(import.Key);
            if (missing) _savedTileImportsMissing++;
            else _savedTileImportsFailed++;
        }
    }

    private void QueueSpatialEncode(TerrainLodTileKey key)
    {
        lock (_gate)
        {
            if (_disposed || _spatialWirePayloads.ContainsKey(key) ||
                !_spatialEncodesPending.Add(key)) return;
            _spatialEncodeRequests.Enqueue(key);
            _writerWake.Set();
        }
    }

    private int DrainSpatialEncodes(int maximumEncodes)
    {
        var consumed = 0;
        while (consumed < maximumEncodes)
        {
            TerrainLodTileKey key;
            lock (_gate)
            {
                if (!_spatialEncodeRequests.TryDequeue(out key)) break;
            }
            try
            {
                if (!_spatialHierarchy.TryGetCoverage(key, out var tile, out _) || tile is null)
                    continue;
                var payload = TerrainLodTileMessage.Encode(tile);
                lock (_gate)
                {
                    const int capacity = 512;
                    if (!_spatialWirePayloads.ContainsKey(key)) _spatialWirePayloadOrder.Enqueue(key);
                    _spatialWirePayloads[key] = payload;
                    while (_spatialWirePayloads.Count > capacity &&
                           _spatialWirePayloadOrder.TryDequeue(out var evicted))
                        _spatialWirePayloads.Remove(evicted);
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                                          ArgumentException or InvalidOperationException or
                                          OverflowException)
            {
                RecordPipelineFailure(error, key.X, key.Z);
            }
            finally
            {
                lock (_gate) _spatialEncodesPending.Remove(key);
            }
            consumed++;
        }
        return consumed;
    }

    private void PublishSpatialLeaf(TerrainLodConversionResult result)
    {
        if (result.SpatialLeaf is not { } leaf)
            throw new InvalidOperationException(
                $"Terrain LOD conversion {result.ChunkX},{result.ChunkZ} has no spatial leaf.");
        _spatialHierarchy.PublishLeaf(leaf);
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
            _writer.Snapshot(),
            _spatialCache.Snapshot(),
            _spatialHierarchy.Snapshot(),
            _spatialReadRequests.Count,
            _spatialReadsPending.Count,
            _spatialEncodeRequests.Count,
            _spatialEncodesPending.Count,
            _spatialWirePayloads.Count,
            _savedTileImports.Count,
            _savedChunksImported,
            _savedTileImportsMissing,
            _savedTileImportsFailed));

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var state in _tracked.Values)
                state.Chunk.TerrainChanged -= OnTerrainChanged;
            _tracked.Clear();
            _spatialReadRequests.Clear();
            _spatialReadsPending.Clear();
            _spatialMissing.Clear();
            _savedTileImportQueue.Clear();
            _savedTileImports.Clear();
            _spatialEncodeRequests.Clear();
            _spatialEncodesPending.Clear();
            _spatialWirePayloads.Clear();
            _spatialWirePayloadOrder.Clear();
            _stopWriter = true;
            PublishSnapshotLocked();
        }
        _writerWake.Set();
        if (_writerThread != Thread.CurrentThread)
            _writerThread.Join(TimeSpan.FromSeconds(5));
        _conversions.Dispose();
        _spatialHierarchy.Dispose();
        _writerWake.Dispose();
    }

    private readonly record struct ChunkKey(int X, int Z);

    private sealed class SavedTileImport(
        TerrainLodTileKey key, TerrainLodTransientTileBuilder? transientBuilder)
    {
        public TerrainLodTileKey Key { get; } = key;
        public TerrainLodTransientTileBuilder? TransientBuilder { get; } = transientBuilder;
        public int NextChunk { get; set; }
        public TerrainLodSourceSnapshot? DeferredSource { get; set; }
        public DateTime? AwaitingPublicationSince { get; set; }
    }

    private sealed class TrackedChunk(Chunk chunk, long firstDirtyTick, long dueTick)
    {
        public Chunk Chunk { get; } = chunk;
        public long FirstDirtyTick { get; set; } = firstDirtyTick;
        public long DueTick { get; set; } = dueTick;
        public long LastSubmittedRevision { get; set; } = -1;
        public bool Dirty { get; set; } = true;
    }
}
