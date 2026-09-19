using System.Diagnostics;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Network.Messages;
using OmniBlock.Profiling;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Lod;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal readonly record struct ClientTerrainLodSnapshot(
    int PendingColumns,
    int ConversionOwnedColumns,
    int ResidentColumns,
    int ExactVoxelLevelColumns,
    int TransitionLevelColumns,
    int PresentedColumns,
    int PresentedTranslucentColumns,
    int HandoffPreparingColumns,
    int HandoffOverlapColumns,
    int LevelTransitionColumns,
    int BoundaryLinkedColumns,
    int BoundaryPendingColumns,
    int UploadsThisFrame,
    long ResidentGpuBytes,
    long ResidentBoundaryBytes,
    long StaleResults,
    long RejectedAdmissions,
    long Evictions,
    long HandoffsStarted,
    long HandoffReversals,
    long LevelTransitionsStarted,
    long LevelTransitionReversals,
    long BoundaryRefreshes,
    int CacheEntries,
    long CacheBytes,
    long CacheHits,
    long CacheMisses,
    int CacheWritesPending,
    long CacheWrites,
    long CacheWriteDrops,
    long CacheErrors,
    long ResourceGeneration,
    long ResourceReloads,
    int LastResourceReloadReusedColumns,
    long LastResourceReloadReusedGpuBytes,
    double SolidRenderCpuMs,
    double TranslucentRenderCpuMs,
    int MeshCompilationOwned,
    int MeshCoverageQueued,
    int MeshRefinementQueued,
    int MeshCoverageCompleted,
    int MeshRefinementCompleted,
    long MeshCompletedResultBytes,
    long MeshPredictedResultBytes,
    double MeshPredictedCompilationMs,
    long MeshAdmissionDeferrals,
    long MeshUploadAdmissionDeferrals,
    long MeshOversizedUploadAdmissions,
    long MeshCompilationSamples,
    long MeshUploadSamples,
    double MeshCompilationMsPerKCell,
    double MeshResultBytesPerKCell,
    double MeshUploadBaseMs,
    double MeshUploadMsPerMiB,
    long RemoteRequests,
    long RemoteTiles,
    long RemoteWireBytes,
    long RemotePendingResponses,
    long RemoteMissingResponses,
    long RemoteDeferredResponses,
    int RemoteCoverageRequired,
    int RemoteCoverageAvailable,
    int RemoteCoverageInFlight,
    int RemoteCoveragePending,
    int RemoteCoverageMissing,
    int RemoteCoverageDeferred);

internal readonly record struct TerrainLodSpatialSnapshot(
    TerrainLodTileKey Root,
    bool CompleteCoverage,
    int SelectedTiles,
    int ParentFallbacks,
    int MissingCoverageGroups,
    int GpuPresentations,
    int HighestGpuResidentLevel,
    int PendingMeshCandidates,
    int DesiredSeams,
    int GpuSeams,
    bool SubmissionReady,
    int AuthoritativeTiles,
    int HighestAuthoritativeLevel,
    int SubmittedSolidPages,
    int SubmittedTranslucentPages,
    long GpuBytes,
    TerrainLodSpatialHierarchyCoordinatorSnapshot Hierarchy,
    TerrainLodSpatialMeshCompilationSnapshot MeshCompilation,
    TerrainLodSpatialSeamCompilationSnapshot SeamCompilation);

/// <summary>
///     Client owner for the first terrain-horizon slice. It compiles immutable chunk snapshots on
///     the LOD worker and publishes solid/translucent GPU presentations atomically on the render
///     thread. Fine Level 1 GPU data is admitted only around the near-renderer transition band.
/// </summary>
/// <remarks>
///     A LOD presentation is coverage, never authority: it is drawn before the ordinary chunk
///     renderer and is hidden only after that renderer has a complete column presentation. An old
///     LOD mesh remains valid coverage while a newer revision is being built.
/// </remarks>
internal sealed class ClientTerrainLodRenderer : IDisposable, ITerrainPresentationHandoff
{
    public const float MaximumDistanceBlocks = 1024.0f;
    private const int ConversionCapacity = 16;
    private const int PendingCapacity = 4096;
    private const int ResidentCapacity = 2048;
    private const long ResidentGpuByteCapacity = 128L * 1024 * 1024;
    private const int SnapshotsPerTick = 4;
    private const double MeshUploadBudgetMs = 1.0;
    private const long MeshUploadBudgetBytes = 8L * 1024 * 1024;
    private const int SeamUploadsPerFrame = 2;
    private const int SeamDrawsPerFrame = 256;
    private const int DrawsPerFrame = 768;
    private const int QuietTicks = 2;
    private const int ExactVoxelMeshLevel = 0;
    private const int TransitionMeshLevel = 1;
    private const int MinimumHorizonMeshLevel = 2;
    private const int MaximumMeshLevel = 4;
    private const int MinimumSpatialGpuLevel = 2;
    private const int SpatialParentResultsPerTick = 8;
    private const int SpatialMeshAdmissionsPerTick = 8;
    private const int SpatialUploadsPerFrame = 2;
    private const int SpatialSeamAdmissionsPerFrame = 8;
    private const int SpatialSeamUploadsPerFrame = 2;
    private const int MaximumRemoteOutstandingRequests = 16;
    private const int OverworldCaveCullCeilingY = 60;

    private readonly World _world;
    private readonly TerrainLodConversionService _conversion;
    private readonly TerrainLodMeshCompilationService _meshCompilation;
    private readonly TerrainLodSpatialPolicy _spatialPolicy;
    private readonly TerrainLodSpatialHierarchyCoordinator _spatialHierarchy;
    private readonly TerrainLodSpatialMeshCompilationService _spatialMeshCompilation;
    private readonly TerrainLodSpatialSeamCompilationService _spatialSeamCompilation;
    private readonly TerrainLodSpatialPresentationSet<TerrainLodSpatialGpuPresentation>
        _spatialPresentations = new();
    private readonly Dictionary<TerrainLodTileKey, TerrainLodColumnTile> _spatialMeshPending = [];
    private readonly Dictionary<TerrainLodTileKey, RemoteTileRequestState> _remoteRequestStates = [];
    private readonly HashSet<TerrainLodSpatialSeamSegment> _desiredSpatialSeams = [];
    private readonly Dictionary<TerrainLodSpatialSeamSegment,
        TerrainLodSpatialGpuSeamPresentation> _spatialSeams = [];
    private readonly Dictionary<TerrainLodSpatialSeamSegment,
        TerrainLodSpatialPresentationFade> _spatialSeamFades = [];
    private readonly Dictionary<TerrainLodSpatialSeamSegment,
        SpatialSeamHashCache> _spatialSeamHashes = [];
    private readonly HashSet<TerrainLodTileKey> _authoritativeSpatialTiles = [];
    private readonly List<VisibleSpatialPage> _visibleSpatialSolid = [];
    private readonly List<VisibleSpatialPage> _visibleSpatialTranslucent = [];
    private readonly List<TerrainLodColumnTile> _completedSpatialParents = [];
    private readonly TerrainLodCacheStore? _cache;
    private readonly TerrainLodAsyncCacheWriter? _cacheWriter;
    private readonly Dictionary<(int X, int Z), PendingColumn> _pending = [];
    private readonly Dictionary<(int X, int Z), ColumnPresentation> _resident = [];
    private readonly Dictionary<(int X, int Z), int> _detailLevelRequests = [];
    private readonly Dictionary<TerrainLodSeamKey, TerrainLodSeamSelection> _desiredSolidSeams = [];
    private readonly Dictionary<TerrainLodSeamKey, GpuSeam> _solidSeams = [];
    private readonly Dictionary<(int X, int Z), int> _selectedSolidLevels = [];
    private readonly Dictionary<(int X, int Z), TerrainLodSeamColumnState> _solidSeamStates = [];
    private readonly Dictionary<TerrainLodSeamKey, TerrainLodSeamFade> _solidSeamFades = [];
    private readonly Dictionary<TerrainLodSeamKey, TerrainLodSeamSelection> _desiredTranslucentSeams = [];
    private readonly Dictionary<TerrainLodSeamKey, GpuSeam> _translucentSeams = [];
    private readonly Dictionary<(int X, int Z), int> _selectedTranslucentLevels = [];
    private readonly Dictionary<(int X, int Z), TerrainLodSeamColumnState> _translucentSeamStates = [];
    private readonly Dictionary<TerrainLodSeamKey, TerrainLodSeamFade> _translucentSeamFades = [];
    private readonly List<VisibleColumn> _visible = [];
    private readonly List<VisibleSeam> _visibleSeams = [];
    private readonly List<VisibleSeam> _visibleTranslucentSeams = [];
    private readonly List<VisibleTranslucentDraw> _visibleTranslucentDraws = [];
    private ChunkDrawMetadata[] _uniforms = [];
    private WgpuPipeline? _opaquePipeline;
    private WgpuPipeline? _translucentPipeline;
    private long _tick;
    private long _staleResults;
    private long _rejectedAdmissions;
    private long _evictions;
    private long _handoffsStarted;
    private long _handoffReversals;
    private long _levelTransitionsStarted;
    private long _levelTransitionReversals;
    private long _boundaryRefreshes;
    private long _resourceGeneration = -1;
    private long _resourceReloads;
    private int _lastResourceReloadReusedColumns;
    private long _lastResourceReloadReusedGpuBytes;
    private double _solidRenderCpuMs;
    private double _translucentRenderCpuMs;
    private long _remoteRequests;
    private long _remoteTiles;
    private long _remoteWireBytes;
    private long _remotePendingResponses;
    private long _remoteMissingResponses;
    private long _remoteDeferredResponses;
    private int _remoteCoverageRequired;
    private int _remoteCoverageAvailable;
    private int _remoteCoverageInFlight;
    private int _remoteCoveragePending;
    private int _remoteCoverageMissing;
    private int _remoteCoverageDeferred;
    private bool _disposed;
    private ClientTerrainLodSnapshot _snapshot;
    private TerrainLodSpatialSnapshot _spatialSnapshot;
    private TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation>?
        _spatialFrame;
    private SpatialForestCacheKey? _spatialForestCacheKey;
    private TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation>?
        _spatialForestCachedFrame;
    private bool _spatialSubmissionReady;

    public ClientTerrainLodRenderer(World world, TerrainLodCacheStore? cache = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _cache = cache;
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        _conversion = cache is null
            ? new TerrainLodConversionService(
                world.Dimension.Id,
                materials,
                TerrainLodReductionStrategy.SurfacePreserving,
                ConversionCapacity)
            : new TerrainLodConversionService(
                world.Dimension.Id,
                materials,
                cache,
                TerrainLodReductionStrategy.SurfacePreserving,
                ConversionCapacity);
        if (cache is not null) _cacheWriter = new TerrainLodAsyncCacheWriter(cache);
        _meshCompilation = new TerrainLodMeshCompilationService(ConversionCapacity);
        // Shared with the server cache producer. The live horizon is capped at 64 chunks: a unit
        // of four reaches levels 2/3/4 at 16/32/64 chunks while exact paths retain nearby detail.
        _spatialPolicy = TerrainLodSpatialPolicy.CreateDefault();
        _spatialHierarchy = new TerrainLodSpatialHierarchyCoordinator(
            _spatialPolicy,
            tileCapacity: 4096,
            constructionCapacity: 64,
            completedCapacity: 16);
        _spatialMeshCompilation = new TerrainLodSpatialMeshCompilationService(
            capacity: 32,
            completedCapacity: 8);
        _spatialSeamCompilation = new TerrainLodSpatialSeamCompilationService(
            capacity: 64,
            completedCapacity: 16);
    }

    public ClientTerrainLodSnapshot Snapshot => _snapshot;
    public TerrainLodSpatialSnapshot SpatialSnapshot => _spatialSnapshot;

    public void ObserveRemoteSpatialTile(TerrainLodColumnTile tile, int wireBytes = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(tile);
        if (tile.Key.Level < MinimumSpatialGpuLevel ||
            tile.Key.Level > _spatialPolicy.MaximumSpatialLevel) return;
        var publication = _spatialHierarchy.PublishCached(tile);
        if (publication != TerrainLodTilePublicationResult.IgnoredCurrent)
            QueueSpatialMesh(tile);
        _remoteRequestStates.Remove(tile.Key);
        _remoteTiles++;
        _remoteWireBytes += Math.Max(0, wireBytes);
    }

    public void ObserveRemoteSpatialStatus(TerrainLodTileKey key, TerrainLodTileStatus status)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (key.Level < MinimumSpatialGpuLevel ||
            key.Level > _spatialPolicy.MaximumSpatialLevel) return;
        switch (status)
        {
            case TerrainLodTileStatus.Pending:
                _remotePendingResponses++;
                _remoteRequestStates[key] = new RemoteTileRequestState(
                    _tick + 8, RemoteTileRequestDisposition.Pending);
                break;
            case TerrainLodTileStatus.Missing:
                _remoteMissingResponses++;
                _remoteRequestStates[key] = new RemoteTileRequestState(
                    _tick + 200, RemoteTileRequestDisposition.Missing);
                break;
            case TerrainLodTileStatus.Deferred:
                _remoteDeferredResponses++;
                _remoteRequestStates[key] = new RemoteTileRequestState(
                    _tick + 4, RemoteTileRequestDisposition.Deferred);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }
    }

    public TerrainLodTileKey[] TakeRemoteSpatialRequests(
        Vector3D<double> viewPosition,
        int nearDistanceChunks,
        int horizonDistanceChunks,
        int maximumRequests)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var cameraX = viewPosition.X / 16.0;
        var cameraZ = viewPosition.Z / 16.0;
        var coverageRootLevel = Math.Max(
            MinimumSpatialGpuLevel,
            _spatialPolicy.DesiredSpatialLevel(horizonDistanceChunks));
        var requiredTiles = TerrainLodCoveragePlanner.RequiredTiles(
            cameraX, cameraZ, nearDistanceChunks, horizonDistanceChunks,
            coverageRootLevel, MinimumSpatialGpuLevel);
        UpdateRemoteCoverage(requiredTiles, cameraX, cameraZ, horizonDistanceChunks);
        if (maximumRequests <= 0 || requiredTiles.Length == 0 || (_tick & 3) != 0)
            return [];

        var availableCapacity = MaximumRemoteOutstandingRequests -
            _remoteRequestStates.Values.Count(state =>
                state.Disposition == RemoteTileRequestDisposition.InFlight &&
                state.RetryAfterTick > _tick);
        if (availableCapacity <= 0) return [];

        var requestCount = Math.Min(maximumRequests, availableCapacity);
        List<TerrainLodTileKey> selected = [];
        // The adaptive partition defines the no-hole contract and always consumes request capacity
        // before visual refinement. It is already deterministic and near-to-far.
        foreach (var key in requiredTiles)
        {
            if (selected.Count >= requestCount) break;
            CollectCoverageRequests(key);
        }

        // Refinement may use only capacity not needed by a due coverage tile. Cycling levels keeps
        // the request set bounded while the near-to-far order remains stable within each level.
        if (selected.Count < requestCount)
        {
            var refinementLevels = coverageRootLevel - MinimumSpatialGpuLevel;
            if (refinementLevels > 0)
            {
                var level = coverageRootLevel - 1 -
                            (int)((_tick >> 2) % refinementLevels);
                var refinements = new List<TerrainLodTileKey>();
                foreach (var key in TerrainLodCoveragePlanner.RequiredTiles(
                             cameraX, cameraZ, nearDistanceChunks,
                             horizonDistanceChunks, level, MinimumSpatialGpuLevel))
                {
                    if (selected.Contains(key) ||
                        _spatialHierarchy.TryGetCoverage(key, out _, out _))
                        continue;
                    if (_remoteRequestStates.TryGetValue(key, out var state) &&
                        _tick < state.RetryAfterTick)
                        continue;
                    refinements.Add(key);
                }
                selected.AddRange(refinements
                    .OrderBy(key => key.DistanceTo(cameraX, cameraZ))
                    .ThenBy(static key => key.X)
                    .ThenBy(static key => key.Z)
                    .Take(requestCount - selected.Count));
            }
        }

        foreach (var key in selected)
            _remoteRequestStates[key] = new RemoteTileRequestState(
                _tick + 20, RemoteTileRequestDisposition.InFlight);
        _remoteRequests += selected.Count;
        return [.. selected];

        void CollectCoverageRequests(TerrainLodTileKey key)
        {
            if (selected.Count >= requestCount ||
                _spatialHierarchy.HasCompleteCoverage(key, MinimumSpatialGpuLevel))
                return;
            if (_remoteRequestStates.TryGetValue(key, out var state))
            {
                if (_tick >= state.RetryAfterTick)
                {
                    selected.Add(key);
                    return;
                }
                // A known-absent aggregate can still be reconstructed client-side from smaller
                // approved records. Other dispositions may yet deliver this exact key, so only a
                // definitive miss opens its descendants.
                if (state.Disposition != RemoteTileRequestDisposition.Missing ||
                    key.Level == MinimumSpatialGpuLevel)
                    return;
                for (var index = 0; index < 4; index++)
                    CollectCoverageRequests(key.Child(index));
                return;
            }
            selected.Add(key);
        }
    }

    private void UpdateRemoteCoverage(
        IReadOnlyCollection<TerrainLodTileKey> requiredTiles,
        double cameraChunkX,
        double cameraChunkZ,
        int horizonDistanceChunks)
    {
        foreach (var key in _remoteRequestStates.Keys
                     .Where(key => key.Level < MinimumSpatialGpuLevel ||
                                   key.Level > _spatialPolicy.MaximumSpatialLevel ||
                                   key.DistanceTo(cameraChunkX, cameraChunkZ) >
                                   horizonDistanceChunks)
                     .ToArray())
            _remoteRequestStates.Remove(key);

        _remoteCoverageRequired = requiredTiles.Count;
        _remoteCoverageAvailable = 0;
        _remoteCoverageInFlight = 0;
        _remoteCoveragePending = 0;
        _remoteCoverageMissing = 0;
        _remoteCoverageDeferred = 0;
        foreach (var key in requiredTiles)
        {
            if (HasRemoteCoverage(key))
            {
                _remoteCoverageAvailable++;
                _remoteRequestStates.Remove(key);
                continue;
            }
            var disposition = CoverageDisposition(key);
            switch (disposition)
            {
                case RemoteTileRequestDisposition.InFlight:
                    _remoteCoverageInFlight++;
                    break;
                case RemoteTileRequestDisposition.Pending:
                    _remoteCoveragePending++;
                    break;
                case RemoteTileRequestDisposition.Missing:
                    _remoteCoverageMissing++;
                    break;
                case RemoteTileRequestDisposition.Deferred:
                    _remoteCoverageDeferred++;
                    break;
                case null:
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown remote tile request disposition '{disposition}'.");
            }
        }

        RemoteTileRequestDisposition? CoverageDisposition(TerrainLodTileKey key)
        {
            if (HasRemoteCoverage(key)) return null;
            var hasState = _remoteRequestStates.TryGetValue(key, out var state);
            if (hasState &&
                (state.Disposition != RemoteTileRequestDisposition.Missing ||
                 key.Level == MinimumSpatialGpuLevel))
                return state.Disposition;
            if (key.Level == MinimumSpatialGpuLevel)
                return null;

            RemoteTileRequestDisposition? aggregate = null;
            for (var index = 0; index < 4; index++)
            {
                var childDisposition = CoverageDisposition(key.Child(index));
                if (childDisposition == RemoteTileRequestDisposition.InFlight)
                    return childDisposition;
                if (childDisposition == RemoteTileRequestDisposition.Pending)
                    aggregate = RemoteTileRequestDisposition.Pending;
                else if (childDisposition == RemoteTileRequestDisposition.Deferred &&
                         aggregate is not RemoteTileRequestDisposition.Pending)
                    aggregate = RemoteTileRequestDisposition.Deferred;
                else if (childDisposition == RemoteTileRequestDisposition.Missing &&
                         aggregate is null)
                    aggregate = RemoteTileRequestDisposition.Missing;
            }
            return aggregate ?? (hasState ? state.Disposition : null);
        }
    }

    private bool HasRemoteCoverage(TerrainLodTileKey root) =>
        _spatialHierarchy.HasCompleteCoverage(root, MinimumSpatialGpuLevel);

    /// <summary>
    ///     Observes texture-resource replacement without invalidating terrain presentation. Both
    ///     near and reduced terrain store stable named-atlas layer indices; the texture manager
    ///     replaces the pixels and WebGPU binding behind those indices. Hierarchy data, meshes,
    ///     seams, handoffs, and residency therefore remain compatible across a pack reload.
    /// </summary>
    /// <remarks>
    ///     Resource-dependent presentations such as entity impostor atlases own their own
    ///     generation invalidation. Keeping this counter here makes the deliberate terrain reuse
    ///     visible to diagnostics and E2E tests instead of relying on the absence of a reset call.
    /// </remarks>
    public void ObserveResourceGeneration(long generation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_resourceGeneration < 0)
        {
            _resourceGeneration = generation;
            return;
        }

        if (_resourceGeneration == generation) return;
        _resourceGeneration = generation;
        _resourceReloads++;
        _lastResourceReloadReusedColumns = _resident.Count;
        _lastResourceReloadReusedGpuBytes = ResidentGpuBytes();
    }

    public TerrainNearHandoff GetNearHandoff(int chunkX, int chunkZ, bool translucent)
    {
        if (!_resident.TryGetValue((chunkX, chunkZ), out var presentation) ||
            !presentation.HasLayer(translucent)) return TerrainNearHandoff.Inactive;
        return new TerrainNearHandoff(
            true,
            presentation.HandoffFor(translucent).Progress,
            FadeSeed((chunkX, chunkZ)));
    }

    /// <summary>Coalesces terrain changes by chunk coordinate without retaining source arrays.</summary>
    public void ObserveRegion(int minX, int minZ, int maxX, int maxZ)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var minChunkX = minX >> 4;
        var minChunkZ = minZ >> 4;
        var maxChunkX = maxX >> 4;
        var maxChunkZ = maxZ >> 4;
        for (var chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
        for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
            ObserveColumn(chunkX, chunkZ);
    }

    public void Tick(Vector3D<double> viewPosition)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _tick++;
        _spatialHierarchy.SetCameraChunkPosition(
            viewPosition.X / SubChunkRenderer.Size,
            viewPosition.Z / SubChunkRenderer.Size);

        _completedSpatialParents.Clear();
        _spatialHierarchy.DrainCompleted(
            SpatialParentResultsPerTick, _completedSpatialParents);
        foreach (var parent in _completedSpatialParents) QueueSpatialMesh(parent);
        DispatchSpatialMeshCompilation(viewPosition);

        var due = TerrainLodAdmissionOrder.TakeNearest(
            _pending
            .Where(pair => pair.Value.DueTick <= _tick)
            .Select(pair => pair.Key), viewPosition, SnapshotsPerTick);

        foreach (var key in due)
        {
            if (!_world.BlockHost.HasChunk(key.X, key.Z))
            {
                _pending.Remove(key);
                continue;
            }

            var chunk = _world.BlockHost.GetChunk(key.X, key.Z);
            if (!chunk.Loaded || !chunk.HasCompleteTerrainSnapshot)
            {
                _pending[key] = _pending[key] with { DueTick = _tick + 1 };
                continue;
            }

            var source = TerrainLodSourceSnapshot.Capture(chunk);
            var result = _conversion.Submit(source);
            if (result == TerrainLodAdmissionResult.RejectedAtCapacity)
            {
                _rejectedAdmissions++;
                _pending[key] = _pending[key] with { DueTick = _tick + 1 };
                continue;
            }

            _pending.Remove(key);
        }

        EvictDistant(viewPosition);
        PublishSnapshot(0, 0);
    }

    /// <summary>
    ///     Draws distant opaque coverage before near terrain. The shared depth buffer makes the
    ///     later, more detailed near presentation replace overlapping LOD pixels naturally.
    /// </summary>
    public unsafe void Render(in ChunkRenderParams parameters, ChunkRenderer nearRenderer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(nearRenderer);
        using var cpuMeasurement = new RenderCpuMeasurement(this, translucent: false);
        using var _lodRender = Profiler.Begin("TerrainLodRender");
        var stageStarted = Stopwatch.GetTimestamp();
        var uploads = InstallCompleted(parameters.ViewPos,
            parameters.VerticalFovDegrees, parameters.ViewportHeight,
            parameters.TerrainLodDropoffScale,
            parameters.RenderDistance, nearRenderer);
        uploads += InstallSpatialCompleted(nearRenderer);
        uploads += InstallSpatialSeams(nearRenderer);
        EvaluateSpatialPresentation(parameters);
        EvictDistant(parameters.ViewPos);
        Profiler.Record("InstallAndEvictCpu", Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);

        if (RenderSystem.Fog.Curve != FogCurve.Linear ||
            RenderSystem.DrawTargetOrNull is not WebGpuDrawTarget target ||
            target.CurrentPass is null || target.TerrainArray is not { } terrainArray ||
            WebGpuDevice.Current is not { } device)
        {
            PublishSnapshot(uploads, 0);
            return;
        }

        _visible.Clear();
        _visibleSeams.Clear();
        _selectedSolidLevels.Clear();
        _solidSeamStates.Clear();
        _solidSeamFades.Clear();
        var maximumDistance = Math.Min(
            MaximumDistanceBlocks, Math.Max(1, parameters.TerrainHorizonDistance) * 16.0f);
        var maximumDistanceSquared = maximumDistance * maximumDistance;
        CollectVisibleSpatialPages(parameters, translucent: false, _visibleSpatialSolid);
        stageStarted = Stopwatch.GetTimestamp();
        foreach (var (key, presentation) in _resident)
        {
            if (IsAuthoritativeSpatialChunk(key)) continue;
            var distanceSquared = DistanceSquared(key, parameters.ViewPos);
            if (distanceSquared > maximumDistanceSquared ||
                !parameters.Camera.IsBoundingBoxInFrustum(new Box(
                    key.X * 16, 0, key.Z * 16,
                    key.X * 16 + 16, ChuckFormat.WorldHeight, key.Z * 16 + 16)))
                continue;

            if (!presentation.HasLayer(translucent: false))
            {
                _solidSeamStates[key] = new TerrainLodSeamColumnState(
                    presentation.Boundaries.NearestLevel(presentation.MinimumLevel),
                    1, FadeSeed(key), false);
                continue;
            }
            var (nearPresent, nearReady) = NearState(
                key, distanceSquared, parameters.RenderDistance, nearRenderer);
            var handoff = UpdateHandoff(presentation,
                translucent: false, nearPresent, nearReady,
                parameters.DeltaTime, parameters.ChunkFade);
            var requestedLevel = TerrainLodDetailSelector.SelectLevel(
                Math.Sqrt(distanceSquared), presentation.MaximumLevel,
                presentation.SelectionLevel(translucent: false),
                parameters.VerticalFovDegrees, parameters.ViewportHeight,
                parameters.TerrainLodDropoffScale);
            requestedLevel = ConstrainLevelToNeighbors(
                key, requestedLevel, translucent: false);
            if (requestedLevel < presentation.MinimumLevel)
                RequestDetailLevel(key, requestedLevel);
            if (handoff.Progress >= 1)
            {
                if (presentation.TryGetNearestLevel(
                        requestedLevel, translucent: false, out var exactLevel, out _))
                    _solidSeamStates[key] = new TerrainLodSeamColumnState(
                        exactLevel, handoff.Progress, FadeSeed(key), Drawn: false);
                continue;
            }
            var presentedLevel = AppendVisibleLevels(
                presentation, key, distanceSquared, requestedLevel,
                translucent: false, handoff.Progress,
                parameters.DeltaTime, parameters.ChunkFade);
            if (presentedLevel >= 0)
            {
                _selectedSolidLevels[key] = presentedLevel;
                _solidSeamStates[key] = new TerrainLodSeamColumnState(
                    presentedLevel, handoff.Progress, FadeSeed(key), Drawn: true);
            }
        }
        Profiler.Record("SelectionCpu", Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);

        stageStarted = Stopwatch.GetTimestamp();
        _visible.Sort(static (a, b) =>
        {
            var distance = a.DistanceSquared.CompareTo(b.DistanceSquared);
            if (distance != 0) return distance;
            var x = a.Key.X.CompareTo(b.Key.X);
            if (x != 0) return x;
            var z = a.Key.Z.CompareTo(b.Key.Z);
            return z != 0 ? z : a.Level.CompareTo(b.Level);
        });
        if (_visible.Count > DrawsPerFrame)
            _visible.RemoveRange(DrawsPerFrame, _visible.Count - DrawsPerFrame);
        var drawnColumns = _visible.Select(static item => item.Key).ToHashSet();
        foreach (var key in _selectedSolidLevels.Keys
                     .Where(key => !drawnColumns.Contains(key)).ToArray())
        {
            _selectedSolidLevels.Remove(key);
            _solidSeamStates.Remove(key);
        }
        Profiler.Record("SortAndTrimCpu", Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);

        stageStarted = Stopwatch.GetTimestamp();
        BuildDesiredSeams(
            _solidSeamStates, _desiredSolidSeams, _solidSeamFades);
        uploads += UpdateSeams(device, parameters.ViewPos, SeamUploadsPerFrame,
            translucent: false, _desiredSolidSeams, _solidSeams);
        CollectVisibleSeams(parameters.ViewPos, backToFront: false,
            _desiredSolidSeams, _solidSeams, _solidSeamFades, _visibleSeams);
        var seamDrawBudget = Math.Min(
            SeamDrawsPerFrame, Math.Max(0, DrawsPerFrame - _visible.Count));
        if (_visibleSeams.Count > seamDrawBudget)
            _visibleSeams.RemoveRange(seamDrawBudget, _visibleSeams.Count - seamDrawBudget);
        Profiler.Record("SeamCpu", Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);
        if (_visible.Count == 0 && _visibleSeams.Count == 0 &&
            _visibleSpatialSolid.Count == 0)
        {
            PublishSnapshot(uploads, 0);
            return;
        }

        _opaquePipeline ??= ChunkRenderer.CreateWgpuPipeline(device, RenderState.Opaque);
        _opaquePipeline.Bind(target.CurrentPass);
        _opaquePipeline.UploadUniforms(BuildFrameUniforms(parameters));
        _opaquePipeline.BindUniformGroup(target.CurrentPass);
        WgpuPipeline.BindGroup(target.CurrentPass, 1,
            terrainArray.BindGroupFor(_opaquePipeline.TextureBindGroupLayout), device.Api);

        var spatialStart = _visible.Count + _visibleSeams.Count;
        var drawCount = spatialStart + _visibleSpatialSolid.Count;
        if (_uniforms.Length < drawCount) _uniforms = new ChunkDrawMetadata[drawCount];
        for (var i = 0; i < _visible.Count; i++)
            _uniforms[i] = BuildUniforms(
                parameters, _visible[i].Key, _visible[i].FadeProgress,
                _visible[i].FadeMode, _visible[i].FadeSeed);
        for (var i = 0; i < _visibleSeams.Count; i++)
            _uniforms[_visible.Count + i] = BuildUniforms(
                parameters, _visibleSeams[i].Key.Owner,
                _visibleSeams[i].Fade.Progress,
                _visibleSeams[i].Fade.Mode,
                _visibleSeams[i].Fade.Seed);
        for (var i = 0; i < _visibleSpatialSolid.Count; i++)
            _uniforms[spatialStart + i] = BuildUniforms(
                _visibleSpatialSolid[i].Page.Origin,
                _visibleSpatialSolid[i].Fade.Progress,
                _visibleSpatialSolid[i].Fade.Mode,
                _visibleSpatialSolid[i].Fade.Seed);
        stageStarted = Stopwatch.GetTimestamp();
        _opaquePipeline.WriteDrawStorage(_uniforms.AsSpan(0, drawCount));
        _opaquePipeline.BindDrawStorage(target.CurrentPass);
        Profiler.Record("UniformUploadCpu", Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);

        stageStarted = Stopwatch.GetTimestamp();
        for (var i = 0; i < _visible.Count; i++)
        {
            var gpu = _visible[i].Gpu;
            gpu.SolidMesh!.Draw(
                target.CurrentPass, lightBuffer: gpu.Lighting!.Solid, firstInstance: (uint)i);
        }

        for (var i = 0; i < _visibleSeams.Count; i++)
        {
            var seam = _visibleSeams[i].Gpu;
            seam.Mesh!.Draw(
                target.CurrentPass, lightBuffer: seam.Lighting!.Solid,
                firstInstance: (uint)(_visible.Count + i));
        }
        var spatialBinding = new TerrainStreamBindingState();
        for (var i = 0; i < _visibleSpatialSolid.Count; i++)
            DrawSpatialPage(
                target.CurrentPass, _visibleSpatialSolid[i], translucent: false,
                ref spatialBinding, (uint)(spatialStart + i), parameters.ViewPos);
        Profiler.Record("DrawCpu", Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);

        PublishSnapshot(uploads, _visible.Count);
    }

    /// <summary>
    ///     Draws the independently owned liquid/glass layer in back-to-front column order. The
    ///     caller supplies the same blended terrain state used by the full-detail translucent pass.
    /// </summary>
    public unsafe void RenderTransparent(in ChunkRenderParams parameters, ChunkRenderer nearRenderer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(nearRenderer);
        using var cpuMeasurement = new RenderCpuMeasurement(this, translucent: true);
        if (RenderSystem.Fog.Curve != FogCurve.Linear ||
            RenderSystem.DrawTargetOrNull is not WebGpuDrawTarget target ||
            target.CurrentPass is null || target.TerrainArray is not { } terrainArray ||
            WebGpuDevice.Current is not { } device)
        {
            _snapshot = _snapshot with { PresentedTranslucentColumns = 0 };
            return;
        }

        _visible.Clear();
        _visibleTranslucentSeams.Clear();
        _selectedTranslucentLevels.Clear();
        _translucentSeamStates.Clear();
        _translucentSeamFades.Clear();
        var maximumDistance = Math.Min(
            MaximumDistanceBlocks, Math.Max(1, parameters.TerrainHorizonDistance) * 16.0f);
        var maximumDistanceSquared = maximumDistance * maximumDistance;
        CollectVisibleSpatialPages(parameters, translucent: true, _visibleSpatialTranslucent);
        foreach (var (key, presentation) in _resident)
        {
            if (IsAuthoritativeSpatialChunk(key)) continue;
            var distanceSquared = DistanceSquared(key, parameters.ViewPos);
            if (distanceSquared > maximumDistanceSquared ||
                !parameters.Camera.IsBoundingBoxInFrustum(new Box(
                    key.X * 16, 0, key.Z * 16,
                    key.X * 16 + 16, ChuckFormat.WorldHeight, key.Z * 16 + 16)))
                continue;

            if (!presentation.HasLayer(translucent: true))
            {
                _translucentSeamStates[key] = new TerrainLodSeamColumnState(
                    presentation.Boundaries.NearestLevel(presentation.MinimumLevel),
                    1, FadeSeed(key), false);
                continue;
            }
            var (nearPresent, nearReady) = NearState(
                key, distanceSquared, parameters.RenderDistance, nearRenderer);
            var handoff = UpdateHandoff(presentation,
                translucent: true, nearPresent, nearReady,
                parameters.DeltaTime, parameters.ChunkFade);
            var requestedLevel = TerrainLodDetailSelector.SelectLevel(
                Math.Sqrt(distanceSquared), presentation.MaximumLevel,
                presentation.SelectionLevel(translucent: true),
                parameters.VerticalFovDegrees, parameters.ViewportHeight,
                parameters.TerrainLodDropoffScale);
            requestedLevel = ConstrainLevelToNeighbors(
                key, requestedLevel, translucent: true);
            if (requestedLevel < presentation.MinimumLevel)
                RequestDetailLevel(key, requestedLevel);
            if (handoff.Progress >= 1)
            {
                if (presentation.TryGetNearestLevel(
                        requestedLevel, translucent: true, out var exactLevel, out _))
                    _translucentSeamStates[key] = new TerrainLodSeamColumnState(
                        exactLevel, handoff.Progress, FadeSeed(key), Drawn: false);
                continue;
            }
            var presentedLevel = AppendVisibleLevels(
                presentation, key, distanceSquared, requestedLevel,
                translucent: true, handoff.Progress,
                parameters.DeltaTime, parameters.ChunkFade);
            if (presentedLevel >= 0)
            {
                _selectedTranslucentLevels[key] = presentedLevel;
                _translucentSeamStates[key] = new TerrainLodSeamColumnState(
                    presentedLevel, handoff.Progress, FadeSeed(key), Drawn: true);
            }
        }

        _visible.Sort(static (a, b) =>
        {
            var distance = b.DistanceSquared.CompareTo(a.DistanceSquared);
            if (distance != 0) return distance;
            var x = a.Key.X.CompareTo(b.Key.X);
            if (x != 0) return x;
            var z = a.Key.Z.CompareTo(b.Key.Z);
            return z != 0 ? z : b.Level.CompareTo(a.Level);
        });
        if (_visible.Count > DrawsPerFrame)
            _visible.RemoveRange(DrawsPerFrame, _visible.Count - DrawsPerFrame);
        var drawnColumns = _visible.Select(static item => item.Key).ToHashSet();
        foreach (var key in _selectedTranslucentLevels.Keys
                     .Where(key => !drawnColumns.Contains(key)).ToArray())
        {
            _selectedTranslucentLevels.Remove(key);
            _translucentSeamStates.Remove(key);
        }
        BuildDesiredSeams(
            _translucentSeamStates, _desiredTranslucentSeams, _translucentSeamFades);
        var seamUploads = UpdateSeams(device, parameters.ViewPos, SeamUploadsPerFrame,
            translucent: true, _desiredTranslucentSeams, _translucentSeams);
        CollectVisibleSeams(parameters.ViewPos, backToFront: true,
            _desiredTranslucentSeams, _translucentSeams,
            _translucentSeamFades, _visibleTranslucentSeams);
        var seamDrawBudget = Math.Min(
            SeamDrawsPerFrame, Math.Max(0, DrawsPerFrame - _visible.Count));
        if (_visibleTranslucentSeams.Count > seamDrawBudget)
            _visibleTranslucentSeams.RemoveRange(
                seamDrawBudget, _visibleTranslucentSeams.Count - seamDrawBudget);
        _visibleTranslucentDraws.Clear();
        _visibleTranslucentDraws.AddRange(_visible.Select(static column =>
            new VisibleTranslucentDraw(column, null, null,
                column.Key, column.DistanceSquared)));
        _visibleTranslucentDraws.AddRange(_visibleTranslucentSeams.Select(static seam =>
            new VisibleTranslucentDraw(
                null, seam.Gpu, null,
                seam.Key.Owner, seam.DistanceSquared, seam.Fade)));
        _visibleTranslucentDraws.AddRange(_visibleSpatialTranslucent.Select(static page =>
            new VisibleTranslucentDraw(
                null, null, page,
                (page.Page.Origin.X >> 4, page.Page.Origin.Z >> 4),
                page.DistanceSquared)));
        _visibleTranslucentDraws.Sort(static (a, b) =>
        {
            var distance = b.DistanceSquared.CompareTo(a.DistanceSquared);
            if (distance != 0) return distance;
            var x = a.Key.X.CompareTo(b.Key.X);
            return x != 0 ? x : a.Key.Z.CompareTo(b.Key.Z);
        });
        if (_visibleTranslucentDraws.Count == 0)
        {
            _snapshot = _snapshot with { PresentedTranslucentColumns = 0 };
            return;
        }

        _translucentPipeline ??= ChunkRenderer.CreateWgpuPipeline(device, RenderSystem.State.Current);
        _translucentPipeline.Bind(target.CurrentPass);
        _translucentPipeline.UploadUniforms(BuildFrameUniforms(parameters));
        _translucentPipeline.BindUniformGroup(target.CurrentPass);
        WgpuPipeline.BindGroup(target.CurrentPass, 1,
            terrainArray.BindGroupFor(_translucentPipeline.TextureBindGroupLayout), device.Api);

        var drawCount = _visibleTranslucentDraws.Count;
        if (_uniforms.Length < drawCount) _uniforms = new ChunkDrawMetadata[drawCount];
        for (var i = 0; i < drawCount; i++)
        {
            var draw = _visibleTranslucentDraws[i];
            var column = draw.Column;
            _uniforms[i] = draw.Spatial is { } spatial
                ? BuildUniforms(
                    spatial.Page.Origin,
                    spatial.Fade.Progress,
                    spatial.Fade.Mode,
                    spatial.Fade.Seed)
                : BuildUniforms(
                    parameters, draw.Key,
                    column?.FadeProgress ?? draw.SeamFade.Progress,
                    column?.FadeMode ?? draw.SeamFade.Mode,
                    column?.FadeSeed ?? draw.SeamFade.Seed);
        }
        _translucentPipeline.WriteDrawStorage(_uniforms.AsSpan(0, drawCount));
        _translucentPipeline.BindDrawStorage(target.CurrentPass);

        var spatialBinding = new TerrainStreamBindingState();
        for (var i = 0; i < drawCount; i++)
        {
            var draw = _visibleTranslucentDraws[i];
            if (draw.Column is { } column)
                column.Gpu.TranslucentMesh!.Draw(
                    target.CurrentPass, lightBuffer: column.Gpu.Lighting!.Translucent,
                    firstInstance: (uint)i);
            else if (draw.Seam is { } seam)
                seam.Mesh!.Draw(
                    target.CurrentPass, lightBuffer: seam.Lighting!.Translucent,
                    firstInstance: (uint)i);
            else if (draw.Spatial is { } spatial)
                DrawSpatialPage(
                    target.CurrentPass, spatial, translucent: true,
                    ref spatialBinding, (uint)i, parameters.ViewPos);
        }

        _snapshot = _snapshot with
        {
            PresentedTranslucentColumns = _visible.Count + _visibleSpatialTranslucent.Count,
            UploadsThisFrame = _snapshot.UploadsThisFrame + seamUploads
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conversion.Dispose();
        _meshCompilation.Dispose();
        _spatialMeshCompilation.Dispose();
        _spatialSeamCompilation.Dispose();
        _spatialPresentations.Dispose();
        _spatialHierarchy.Dispose();
        _cacheWriter?.Dispose();
        foreach (var presentation in _resident.Values) presentation.Dispose();
        foreach (var seam in _solidSeams.Values) seam.Dispose();
        foreach (var seam in _translucentSeams.Values) seam.Dispose();
        foreach (var seam in _spatialSeams.Values) seam.Dispose();
        _resident.Clear();
        _solidSeams.Clear();
        _translucentSeams.Clear();
        _spatialSeams.Clear();
        _desiredSpatialSeams.Clear();
        _spatialSeamFades.Clear();
        _authoritativeSpatialTiles.Clear();
        _visibleSpatialSolid.Clear();
        _visibleSpatialTranslucent.Clear();
        _spatialFrame = null;
        _desiredSolidSeams.Clear();
        _desiredTranslucentSeams.Clear();
        _selectedSolidLevels.Clear();
        _selectedTranslucentLevels.Clear();
        _pending.Clear();
        _detailLevelRequests.Clear();
        _spatialMeshPending.Clear();
        _completedSpatialParents.Clear();
        _opaquePipeline?.Dispose();
        _opaquePipeline = null;
        _translucentPipeline?.Dispose();
        _translucentPipeline = null;
    }

    private void ObserveColumn(int chunkX, int chunkZ)
    {
        var key = (chunkX, chunkZ);
        if (_world.BlockHost.HasChunk(chunkX, chunkZ))
        {
            var chunk = _world.BlockHost.GetChunk(chunkX, chunkZ);
            if (chunk.Loaded &&
                _resident.TryGetValue(key, out var current) &&
                current.TerrainRevision == chunk.TerrainRevision &&
                !_pending.ContainsKey(key)) return;
        }

        if (_pending.ContainsKey(key))
        {
            _pending[key] = new PendingColumn(_tick + QuietTicks);
            return;
        }

        if (_pending.Count >= PendingCapacity)
        {
            _rejectedAdmissions++;
            return;
        }
        _pending.Add(key, new PendingColumn(_tick + QuietTicks));
    }

    private int InstallCompleted(
        Vector3D<double> viewPosition,
        double verticalFovDegrees,
        int viewportHeight,
        float detailDropoffScale,
        int renderDistance,
        ChunkRenderer nearRenderer)
    {
        QueueCompletedConversions(
            viewPosition,
            verticalFovDegrees,
            viewportHeight,
            detailDropoffScale);

        var installed = 0;
        var admittedBytes = 0L;
        var stopwatch = Stopwatch.StartNew();
        while (_meshCompilation.TryPeekCompleted(out var next, out var workKind) &&
               next is not null)
        {
            var remainingMs = Math.Max(0, MeshUploadBudgetMs - stopwatch.Elapsed.TotalMilliseconds);
            var remainingBytes = Math.Max(0, MeshUploadBudgetBytes - admittedBytes);
            var predictedUploadMs = _meshCompilation.EstimateUploadMs(next.UploadBytes);
            var regularAdmission = predictedUploadMs <= remainingMs &&
                                   next.UploadBytes <= remainingBytes;
            var oversizedAdmission = !regularAdmission && installed == 0;
            if (!regularAdmission && !oversizedAdmission)
            {
                _meshCompilation.NoteUploadAdmissionDeferred();
                break;
            }
            if (oversizedAdmission) _meshCompilation.NoteOversizedUploadAdmission();
            if (!_meshCompilation.TryTakeCompleted(
                    workKind, advanceFairness: true, out var compiled) || compiled is null)
                continue;

            if (compiled.Failure is not null)
                throw new InvalidOperationException(
                    $"Terrain LOD mesh compilation failed for " +
                    $"{compiled.Conversion.ChunkX},{compiled.Conversion.ChunkZ}.",
                    compiled.Failure);

            var result = compiled.Conversion;
            var key = (result.ChunkX, result.ChunkZ);
            if (_world.BlockHost.HasChunk(key.ChunkX, key.ChunkZ))
            {
                var chunk = _world.BlockHost.GetChunk(key.ChunkX, key.ChunkZ);
                if (chunk.Loaded && chunk.TerrainRevision != result.TerrainRevision)
                {
                    _staleResults++;
                    ObserveColumn(key.ChunkX, key.ChunkZ);
                    continue;
                }
            }

            ColumnPresentation? presentationCandidate = null;
            var uploadStarted = Stopwatch.GetTimestamp();
            try
            {
                _resident.TryGetValue(key, out var previous);
                presentationCandidate = ColumnPresentation.Create(compiled);
                if (previous is not null)
                {
                    presentationCandidate.CopyHandoffsFrom(previous);
                }
                else
                {
                    var distanceSquared = DistanceSquared(key, viewPosition);
                    var (nearPresent, nearReady) = NearState(
                        key, distanceSquared, renderDistance, nearRenderer);
                    if (nearPresent && nearReady) presentationCandidate.InitializeNearOnly();
                }
                if (_resident.Remove(key, out var old)) old.Dispose();
                _resident.Add(key, presentationCandidate);
                if (_detailLevelRequests.TryGetValue(key, out var requestedMinimum) &&
                    presentationCandidate.MinimumLevel <= requestedMinimum)
                    _detailLevelRequests.Remove(key);
                presentationCandidate = null;
                installed++;
                admittedBytes += compiled.UploadBytes;
                _meshCompilation.RecordUpload(
                    Stopwatch.GetElapsedTime(uploadStarted).TotalMilliseconds,
                    compiled.UploadBytes);
            }
            finally
            {
                presentationCandidate?.Dispose();
            }
        }
        return installed;
    }

    private void QueueCompletedConversions(
        Vector3D<double> viewPosition,
        double verticalFovDegrees,
        int viewportHeight,
        float detailDropoffScale)
    {
        while (TryPeekCoverageFirst(out var result) && result is not null)
        {
            var key = (result.ChunkX, result.ChunkZ);
            if (!_world.BlockHost.HasChunk(key.ChunkX, key.ChunkZ))
            {
                _conversion.AcknowledgeCompleted(
                    result.ChunkX, result.ChunkZ, result.TerrainRevision);
                _staleResults++;
                continue;
            }

            var chunk = _world.BlockHost.GetChunk(key.ChunkX, key.ChunkZ);
            if (!chunk.Loaded || chunk.TerrainRevision != result.TerrainRevision)
            {
                _conversion.AcknowledgeCompleted(
                    result.ChunkX, result.ChunkZ, result.TerrainRevision);
                _staleResults++;
                ObserveColumn(key.ChunkX, key.ChunkZ);
                continue;
            }

            _resident.TryGetValue(key, out var previous);
            var selectedLevel = TerrainLodDetailSelector.SelectLevel(
                Math.Sqrt(DistanceSquared(key, viewPosition)), MaximumMeshLevel,
                previous?.SelectionLevel(translucent: false) ?? -1,
                verticalFovDegrees, viewportHeight, detailDropoffScale);
            var minimumLevel = Math.Min(
                Math.Min(selectedLevel, previous?.MinimumLevel ?? MinimumHorizonMeshLevel),
                _detailLevelRequests.GetValueOrDefault(key, MinimumHorizonMeshLevel));
            var workKind = previous is null
                ? TerrainLodMeshWorkKind.Coverage
                : TerrainLodMeshWorkKind.Refinement;
            // Check the learned time/byte admission before copying a complete visual column.
            if (!_meshCompilation.CanSubmit(
                    result, minimumLevel, MaximumMeshLevel, workKind)) break;
            var originX = result.ChunkX * 16;
            var originZ = result.ChunkZ * 16;
            var visuals = new WorldRegionSnapshot(
                _world,
                originX, 0, originZ,
                originX + 15, ChuckFormat.WorldHeight - 1, originZ + 15);
            var request = new TerrainLodMeshCompilationRequest(
                result,
                minimumLevel,
                MaximumMeshLevel,
                visuals,
                !_world.Dimension.HasCeiling,
                workKind,
                _world.Dimension.HasCeiling ? null : OverworldCaveCullCeilingY);
            if (!_meshCompilation.TrySubmit(request))
            {
                visuals.Dispose();
                break;
            }

            if (result.SpatialLeaf is { } spatialLeaf)
                _spatialHierarchy.PublishLeaf(spatialLeaf);

            if (!_conversion.AcknowledgeCompleted(
                    result.ChunkX, result.ChunkZ, result.TerrainRevision))
                throw new InvalidOperationException(
                    $"Terrain LOD conversion {result.ChunkX},{result.ChunkZ} " +
                    $"revision {result.TerrainRevision} lost ownership before mesh compilation.");
            _cacheWriter?.TrySubmit(result);
        }

        return;

        bool TryPeekCoverageFirst(out TerrainLodConversionResult? result)
        {
            // A resident presentation remains visible during refresh/refinement. Prefer a column
            // with no presentation at all, then fall back to the deterministic conversion order.
            if (_conversion.TryPeekCompleted(
                    candidate => !_resident.ContainsKey((candidate.ChunkX, candidate.ChunkZ)),
                    candidate => DistanceSquared(
                        (candidate.ChunkX, candidate.ChunkZ), viewPosition),
                    out result)) return true;
            return _conversion.TryPeekCompleted(
                static _ => true,
                candidate => DistanceSquared(
                    (candidate.ChunkX, candidate.ChunkZ), viewPosition),
                out result);
        }
    }

    private void QueueSpatialMesh(TerrainLodColumnTile tile)
    {
        if (tile.Key.Level < MinimumSpatialGpuLevel) return;
        _spatialMeshPending[tile.Key] = tile;
    }

    private void DispatchSpatialMeshCompilation(Vector3D<double> viewPosition)
    {
        var cameraChunkX = viewPosition.X / SubChunkRenderer.Size;
        var cameraChunkZ = viewPosition.Z / SubChunkRenderer.Size;
        var admitted = 0;
        foreach (var pair in _spatialMeshPending
                     .OrderByDescending(static pair => pair.Key.Level)
                     .ThenBy(pair => pair.Key.DistanceTo(cameraChunkX, cameraChunkZ))
                     .ThenBy(static pair => pair.Key.X)
                     .ThenBy(static pair => pair.Key.Z)
                     .ToArray())
        {
            if (admitted >= SpatialMeshAdmissionsPerTick) break;
            var workKind = _spatialPresentations.IsReady(pair.Key)
                ? TerrainLodSpatialMeshWorkKind.Refinement
                : TerrainLodSpatialMeshWorkKind.Coverage;
            var result = _spatialMeshCompilation.Submit(
                pair.Value,
                _world.Content.Blocks,
                _spatialPolicy.VerticalSliceBudgetForSpatialLevel(pair.Key.Level),
                workKind,
                pair.Key.DistanceTo(cameraChunkX, cameraChunkZ),
                _world.Dimension.HasCeiling ? null : OverworldCaveCullCeilingY);
            if (result == TerrainLodSpatialMeshAdmissionResult.RejectedAtCapacity) break;
            _spatialMeshPending.Remove(pair.Key);
            admitted++;
        }
    }

    /// <summary>
    ///     Uploads real spatial candidates into the shared arenas. Publication alone never makes a
    ///     tile authoritative: the live forest also requires its complete neighbor seam set.
    /// </summary>
    private int InstallSpatialCompleted(ChunkRenderer nearRenderer)
    {
        if (WebGpuDevice.Current is not { } device) return 0;
        var installed = 0;
        while (installed < SpatialUploadsPerFrame &&
               _spatialMeshCompilation.TryTakeCompleted(out var completed) &&
               completed is not null)
        {
            if (completed.Failure is not null)
                throw new InvalidOperationException(
                    "Spatial terrain LOD mesh compilation failed.", completed.Failure);
            var mesh = completed.Mesh ?? throw new InvalidOperationException(
                "Spatial terrain LOD mesh compilation produced no candidate.");
            if (!_spatialHierarchy.TryGetCoverage(mesh.Key, out var current, out _) ||
                current is null || current.CanonicalHash != mesh.CanonicalHash)
            {
                _staleResults++;
                continue;
            }

            var installedCandidate = _spatialPresentations.TryInstall(
                mesh.Key,
                mesh.CanonicalHash,
                () => TerrainLodSpatialGpuPresentation.Create(
                    device, nearRenderer.GetOrCreateTerrainGpuArenas(device), mesh),
                out var failure);
            if (failure is not null)
                throw new InvalidOperationException(
                    $"Spatial terrain LOD upload failed for {mesh.Key}.", failure);
            if (installedCandidate) installed++;
        }
        return installed;
    }

    private int InstallSpatialSeams(ChunkRenderer nearRenderer)
    {
        if (WebGpuDevice.Current is not { } device) return 0;
        var installed = 0;
        while (installed < SpatialSeamUploadsPerFrame &&
               _spatialSeamCompilation.TryTakeCompleted(out var completed) &&
               completed is not null)
        {
            if (completed.Failure is not null)
                throw new InvalidOperationException(
                    "Spatial terrain LOD seam compilation failed.", completed.Failure);
            var mesh = completed.Mesh ?? throw new InvalidOperationException(
                "Spatial terrain LOD seam compilation produced no candidate.");
            if (!_desiredSpatialSeams.Contains(mesh.Segment) ||
                !TryResolveSpatialSeamIdentity(
                    mesh.Segment, out _, out _, out var expectedHash) ||
                expectedHash != mesh.CanonicalHash)
            {
                _staleResults++;
                continue;
            }

            TerrainLodSpatialGpuSeamPresentation? candidate = null;
            try
            {
                candidate = TerrainLodSpatialGpuSeamPresentation.Create(
                    device, nearRenderer.GetOrCreateTerrainGpuArenas(device), mesh);
                if (_spatialSeams.Remove(mesh.Segment, out var previous)) previous.Dispose();
                _spatialSeams.Add(mesh.Segment, candidate);
                candidate = null;
                installed++;
            }
            finally
            {
                candidate?.Dispose();
            }
        }
        return installed;
    }

    private void EvaluateSpatialPresentation(in ChunkRenderParams parameters)
    {
        var stageStarted = Stopwatch.GetTimestamp();
        var cameraChunkX = parameters.ViewPos.X / SubChunkRenderer.Size;
        var cameraChunkZ = parameters.ViewPos.Z / SubChunkRenderer.Size;
        var chunkX = (int)Math.Floor(cameraChunkX);
        var chunkZ = (int)Math.Floor(cameraChunkZ);
        var root = TerrainLodTileKey.ContainingChunk(
            _spatialPolicy.MaximumSpatialLevel, chunkX, chunkZ);
        var selection = TerrainLodSpatialSelector.Select(
            root, cameraChunkX, cameraChunkZ,
            _spatialPolicy, _spatialPresentations.IsReady);
        // Keep the single-root counters for compact diagnostics. A partially loaded level-4
        // management root must not hide the fact that a real level-2 or level-3 GPU partition is
        // already complete and transition-safe.
        for (var level = _spatialPolicy.MaximumSpatialLevel - 1;
             !selection.CompleteCoverage && level >= MinimumSpatialGpuLevel;
             level--)
        {
            var candidateRoot = TerrainLodTileKey.ContainingChunk(level, chunkX, chunkZ);
            var candidate = TerrainLodSpatialSelector.Select(
                candidateRoot, cameraChunkX, cameraChunkZ,
                _spatialPolicy, _spatialPresentations.IsReady);
            if (!candidate.CompleteCoverage) continue;
            root = candidateRoot;
            selection = candidate;
        }
        Profiler.Record("SpatialRootSelectionCpu",
            Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);
        // The forest is the live spatial partition. Bodies become authoritative only after
        // UpdateSpatialSeams verifies that every boundary of this exact partition is GPU-ready.
        stageStarted = Stopwatch.GetTimestamp();
        var frame = BuildSpatialForestFrame(
            cameraChunkX, cameraChunkZ,
            parameters.TerrainHorizonDistance,
            parameters.DeltaTime);
        Profiler.Record("SpatialForestCpu",
            Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);
        stageStarted = Stopwatch.GetTimestamp();
        UpdateSpatialSeams(
            frame, cameraChunkX, cameraChunkZ, parameters.RenderDistance);
        Profiler.Record("SpatialSeamCpu",
            Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);
        stageStarted = Stopwatch.GetTimestamp();
        _spatialSnapshot = new TerrainLodSpatialSnapshot(
            root,
            selection.CompleteCoverage,
            selection.Nodes.Count,
            selection.ParentFallbacks,
            selection.MissingCoverageGroups,
            _spatialPresentations.Count,
            _spatialPresentations.ReadyKeys.Any()
                ? _spatialPresentations.ReadyKeys.Max(static tile => tile.Level)
                : -1,
            _spatialMeshPending.Count,
            _desiredSpatialSeams.Count,
            _spatialSeams.Count,
            _spatialSubmissionReady,
            _authoritativeSpatialTiles.Count,
            _authoritativeSpatialTiles.Count == 0
                ? -1
                : _authoritativeSpatialTiles.Max(static tile => tile.Level),
            _visibleSpatialSolid.Count,
            _visibleSpatialTranslucent.Count,
            _spatialPresentations.ReadyPresentations.Sum(
                static presentation => presentation.EstimatedBytes) +
            _spatialSeams.Values.Sum(static seam => seam.EstimatedBytes),
            _spatialHierarchy.Snapshot(),
            _spatialMeshCompilation.Snapshot(),
            _spatialSeamCompilation.Snapshot());
        Profiler.Record("SpatialSnapshotCpu",
            Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);
    }

    private TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation>
        BuildSpatialForestFrame(
            double cameraChunkX,
            double cameraChunkZ,
            int horizonDistance,
            float deltaTime)
    {
        var cacheKey = new SpatialForestCacheKey(
            cameraChunkX,
            cameraChunkZ,
            horizonDistance,
            _spatialPresentations.Revision);
        if (_spatialForestCacheKey == cacheKey &&
            _spatialForestCachedFrame is not null)
            return _spatialForestCachedFrame;

        List<TerrainLodSpatialPresentationDraw<TerrainLodSpatialGpuPresentation>> draws = [];
        var transitioning = false;
        var maximumDistance = Math.Max(1, horizonDistance) +
                              (1 << MinimumSpatialGpuLevel);
        var forest = TerrainLodSpatialForestSelector.Select(
            _spatialPresentations.ReadyKeys,
            MinimumSpatialGpuLevel,
            cameraChunkX,
            cameraChunkZ,
            maximumDistance,
            _spatialPolicy,
            _spatialPresentations.IsReady);
        HashSet<TerrainLodTileKey> activeRoots = [];
        foreach (var root in forest.Roots)
        {
            activeRoots.Add(root.ManagementRoot);
            // The forest can change spatial level only as a complete parent/child partition. Live
            // fades remain disabled until seam planning can include stable neighboring roots in
            // both transition partitions; the body-plus-seam readiness gate still makes this an
            // atomic replacement with legacy column LOD as the temporary fallback.
            var rootFrame = _spatialPresentations.UpdatePartition(
                root.ManagementRoot,
                root.Nodes,
                root.CompleteCoverage,
                deltaTime,
                fadeEnabled: false);
            if (rootFrame.Draws.Count == 0) continue;
            draws.AddRange(rootFrame.Draws);
            transitioning |= rootFrame.Transitioning;
        }
        _spatialPresentations.RetainTransitionRoots(activeRoots);
        var frame = new TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation>(
            [.. draws], completeCoverage: draws.Count != 0, transitioning);
        _spatialForestCacheKey = cacheKey;
        _spatialForestCachedFrame = frame;
        return frame;
    }

    private void UpdateSpatialSeams(
        TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation> frame,
        double cameraChunkX,
        double cameraChunkZ,
        int renderDistance)
    {
        if (ReferenceEquals(frame, _spatialFrame) && _spatialSubmissionReady)
        {
            RefreshAuthority();
            return;
        }

        _desiredSpatialSeams.Clear();
        _spatialSeamFades.Clear();
        if (frame.CompleteCoverage)
        {
            // Outgoing and incoming partitions overlap during a group fade, so each must be
            // planned independently. Their seam sets coexist until the atomic transition ends.
            foreach (var partition in frame.Draws.GroupBy(static draw => draw.Fade.Mode))
            {
                var fade = partition.First().Fade;
                foreach (var seam in TerrainLodSpatialSeamPlanner.Plan(
                             partition.Select(static draw => draw.Selection)))
                {
                    _desiredSpatialSeams.Add(seam);
                    if (_spatialSeamFades.TryGetValue(seam, out var existing) &&
                        existing.Mode != fade.Mode)
                        _spatialSeamFades[seam] = new TerrainLodSpatialPresentationFade(1, 0, 0);
                    else
                        _spatialSeamFades[seam] = fade;
                }
            }
        }

        _spatialSeamCompilation.Retain(_desiredSpatialSeams);
        foreach (var key in _spatialSeamHashes.Keys
                     .Where(key => !_desiredSpatialSeams.Contains(key)).ToArray())
            _spatialSeamHashes.Remove(key);
        foreach (var key in _spatialSeams.Keys
                     .Where(key => !_desiredSpatialSeams.Contains(key)).ToArray())
            if (_spatialSeams.Remove(key, out var obsolete)) obsolete.Dispose();

        var admitted = 0;
        foreach (var seam in _desiredSpatialSeams
                     .OrderBy(static seam => seam.IsExterior)
                     .ThenBy(seam => SpatialSeamDistance(
                         seam, cameraChunkX, cameraChunkZ))
                     .ThenBy(static seam => seam.FixedChunkCoordinate)
                     .ThenBy(static seam => seam.AlongStartChunk))
        {
            if (admitted >= SpatialSeamAdmissionsPerFrame) break;
            if (!TryResolveSpatialSeamIdentity(
                    seam, out var owner, out var neighbor, out var expectedHash)) continue;
            int? caveCullBelowY = _world.Dimension.HasCeiling
                ? null
                : OverworldCaveCullCeilingY;
            if (_spatialSeams.TryGetValue(seam, out var existing) &&
                existing.CanonicalHash == expectedHash)
                continue;
            if (_spatialSeamCompilation.Submit(
                seam, owner!, neighbor, _world.Content.Blocks,
                    SpatialSeamDistance(seam, cameraChunkX, cameraChunkZ),
                    caveCullBelowY))
                admitted++;
        }

        _spatialFrame = frame;
        _spatialSubmissionReady = frame.CompleteCoverage &&
            _desiredSpatialSeams.All(SpatialSeamIsCurrent);
        RefreshAuthority();

        void RefreshAuthority()
        {
            _spatialFrame = frame;
            _authoritativeSpatialTiles.Clear();
            if (!_spatialSubmissionReady) return;
            foreach (var draw in frame.Draws)
                if (TerrainLodSpatialAuthority.IsBeyondNearRadius(
                        draw.Selection.Tile, cameraChunkX, cameraChunkZ, renderDistance))
                    _authoritativeSpatialTiles.Add(draw.Selection.Tile);
        }

        bool SpatialSeamIsCurrent(TerrainLodSpatialSeamSegment seam)
        {
            if (!_spatialSeams.TryGetValue(seam, out var presentation) ||
                !TryResolveSpatialSeamIdentity(
                    seam, out _, out _, out var expectedHash))
                return false;
            return presentation.CanonicalHash == expectedHash;
        }
    }

    private bool TryResolveSpatialSeamIdentity(
        TerrainLodSpatialSeamSegment seam,
        out TerrainLodColumnTile? owner,
        out TerrainLodColumnTile? neighbor,
        out string canonicalHash)
    {
        canonicalHash = string.Empty;
        if (!TryResolveSpatialSeamTiles(seam, out owner, out neighbor)) return false;
        int? caveCullBelowY = _world.Dimension.HasCeiling
            ? null
            : OverworldCaveCullCeilingY;
        if (_spatialSeamHashes.TryGetValue(seam, out var cached) &&
            string.Equals(cached.OwnerCanonicalHash, owner!.CanonicalHash,
                StringComparison.Ordinal) &&
            string.Equals(cached.NeighborCanonicalHash, neighbor?.CanonicalHash,
                StringComparison.Ordinal) &&
            cached.CaveCullBelowY == caveCullBelowY)
        {
            canonicalHash = cached.CanonicalHash;
            return true;
        }

        canonicalHash = TerrainLodSpatialSeamMeshBuilder.ComputeCanonicalHash(
            seam, owner!, neighbor, caveCullBelowY);
        _spatialSeamHashes[seam] = new SpatialSeamHashCache(
            owner.CanonicalHash,
            neighbor?.CanonicalHash,
            caveCullBelowY,
            canonicalHash);
        return true;
    }

    private void CollectVisibleSpatialPages(
        in ChunkRenderParams parameters,
        bool translucent,
        List<VisibleSpatialPage> destination)
    {
        destination.Clear();
        if (!_spatialSubmissionReady || _spatialFrame is not { } frame ||
            _authoritativeSpatialTiles.Count == 0) return;
        var maximumDistance = Math.Min(
            MaximumDistanceBlocks, Math.Max(1, parameters.TerrainHorizonDistance) * 16.0f);
        var camera = parameters.Camera;
        var viewPosition = parameters.ViewPos;

        foreach (var draw in frame.Draws)
        {
            if (!_authoritativeSpatialTiles.Contains(draw.Selection.Tile)) continue;
            foreach (var page in draw.Presentation.Pages)
                AddPage(page, draw.Fade);
        }
        foreach (var seam in _desiredSpatialSeams)
        {
            if (!SpatialSeamTouchesAuthority(seam) ||
                !_spatialSeams.TryGetValue(seam, out var presentation)) continue;
            var fade = _spatialSeamFades.GetValueOrDefault(
                seam, new TerrainLodSpatialPresentationFade(1, 0, 0));
            foreach (var page in presentation.Pages) AddPage(page, fade);
        }

        destination.Sort((a, b) =>
        {
            var distance = translucent
                ? b.DistanceSquared.CompareTo(a.DistanceSquared)
                : a.DistanceSquared.CompareTo(b.DistanceSquared);
            if (distance != 0) return distance;
            var x = a.Page.Origin.X.CompareTo(b.Page.Origin.X);
            if (x != 0) return x;
            var y = a.Page.Origin.Y.CompareTo(b.Page.Origin.Y);
            return y != 0 ? y : a.Page.Origin.Z.CompareTo(b.Page.Origin.Z);
        });
        return;

        void AddPage(
            TerrainLodSpatialGpuPresentation.GpuPage page,
            TerrainLodSpatialPresentationFade fade)
        {
            if (translucent ? page.Translucent is null : page.Solid is null) return;
            var box = new Box(
                page.Origin.X, page.Origin.Y, page.Origin.Z,
                page.Origin.X + TerrainLodSpatialMeshBuilder.PageSize,
                page.Origin.Y + TerrainLodSpatialMeshBuilder.PageSize,
                page.Origin.Z + TerrainLodSpatialMeshBuilder.PageSize);
            if (!camera.IsBoundingBoxInFrustum(box)) return;
            var dx = Math.Max(0, Math.Max(
                page.Origin.X - viewPosition.X,
                viewPosition.X - (page.Origin.X + TerrainLodSpatialMeshBuilder.PageSize)));
            var dz = Math.Max(0, Math.Max(
                page.Origin.Z - viewPosition.Z,
                viewPosition.Z - (page.Origin.Z + TerrainLodSpatialMeshBuilder.PageSize)));
            var distanceSquared = dx * dx + dz * dz;
            if (distanceSquared > maximumDistance * maximumDistance) return;
            destination.Add(new VisibleSpatialPage(page, fade, distanceSquared));
        }
    }

    private bool IsAuthoritativeSpatialChunk((int X, int Z) key) =>
        _authoritativeSpatialTiles.Any(tile => tile.ContainsChunk(key.X, key.Z));

    private bool SpatialSeamTouchesAuthority(TerrainLodSpatialSeamSegment seam) =>
        _authoritativeSpatialTiles.Contains(seam.Owner.Tile) ||
        seam.Neighbor is { } neighbor &&
        _authoritativeSpatialTiles.Contains(neighbor.Tile);

    private bool TryResolveSpatialSeamTiles(
        TerrainLodSpatialSeamSegment seam,
        out TerrainLodColumnTile? owner,
        out TerrainLodColumnTile? neighbor)
    {
        if (!_spatialHierarchy.TryGetCoverage(seam.Owner.Tile, out owner, out _) ||
            owner is null)
        {
            neighbor = null;
            return false;
        }
        if (seam.Neighbor is not { } selection)
        {
            neighbor = null;
            return true;
        }
        return _spatialHierarchy.TryGetCoverage(selection.Tile, out neighbor, out _) &&
               neighbor is not null;
    }

    private static double SpatialSeamDistance(
        TerrainLodSpatialSeamSegment seam,
        double cameraChunkX,
        double cameraChunkZ)
    {
        var along = (seam.AlongStartChunk + seam.AlongEndChunk) * 0.5;
        var vertical = seam.OwnerSide is TerrainLodSpatialBoundarySide.West or
            TerrainLodSpatialBoundarySide.East;
        var x = vertical ? seam.FixedChunkCoordinate : along;
        var z = vertical ? along : seam.FixedChunkCoordinate;
        var dx = x - cameraChunkX;
        var dz = z - cameraChunkZ;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    private void RequestDetailLevel((int X, int Z) key, int requestedLevel)
    {
        requestedLevel = Math.Clamp(
            requestedLevel, ExactVoxelMeshLevel, MinimumHorizonMeshLevel);
        if (_detailLevelRequests.TryGetValue(key, out var existingRequest) &&
            existingRequest <= requestedLevel) return;
        if (_pending.ContainsKey(key))
        {
            _detailLevelRequests[key] = requestedLevel;
            return;
        }
        if (!_world.BlockHost.HasChunk(key.X, key.Z)) return;
        var chunk = _world.BlockHost.GetChunk(key.X, key.Z);
        if (!chunk.Loaded) return;
        if (_pending.Count >= PendingCapacity)
        {
            _rejectedAdmissions++;
            return;
        }

        _detailLevelRequests[key] = requestedLevel;
        // This is a presentation-detail upgrade of an already valid LOD, not a terrain edit. It may
        // enter the bounded conversion queue on the next tick without the edit quiet period.
        _pending.Add(key, new PendingColumn(_tick));
    }

    private void BuildDesiredSeams(
        Dictionary<(int X, int Z), TerrainLodSeamColumnState> states,
        Dictionary<TerrainLodSeamKey, TerrainLodSeamSelection> desired,
        Dictionary<TerrainLodSeamKey, TerrainLodSeamFade> fades)
    {
        desired.Clear();
        fades.Clear();
        foreach (var (key, state) in states)
        {
            if (!state.Drawn) continue;
            Add(key, Side.East, (key.X + 1, key.Z));
            Add((key.X - 1, key.Z), Side.East, key);
            Add(key, Side.South, (key.X, key.Z + 1));
            Add((key.X, key.Z - 1), Side.South, key);
        }

        void Add(
            (int X, int Z) ownerKey,
            Side side,
            (int X, int Z) neighborKey)
        {
            if (!states.TryGetValue(ownerKey, out var ownerState) ||
                !states.TryGetValue(neighborKey, out var neighborState) ||
                (!ownerState.Drawn && !neighborState.Drawn) ||
                !_resident.TryGetValue(ownerKey, out var owner) ||
                !_resident.TryGetValue(neighborKey, out var neighbor)) return;
            var ownerLevel = ownerState.Level;
            var neighborLevel = neighborState.Level;
            if (Math.Abs(ownerLevel - neighborLevel) > 1)
            {
                // A NearOnly column may retain an older coarse hierarchy while the visible LOD
                // side has already upgraded. It is evidence, not presentation: select the nearest
                // available adjacent tier rather than dropping the exact/LOD boundary altogether.
                if (!ownerState.Drawn)
                {
                    var candidate = ownerLevel < neighborLevel
                        ? neighborLevel - 1
                        : neighborLevel + 1;
                    if (owner.Boundaries.HasLevel(candidate)) ownerLevel = candidate;
                }
                else if (!neighborState.Drawn)
                {
                    var candidate = neighborLevel < ownerLevel
                        ? ownerLevel - 1
                        : ownerLevel + 1;
                    if (neighbor.Boundaries.HasLevel(candidate)) neighborLevel = candidate;
                }
                if (Math.Abs(ownerLevel - neighborLevel) > 1) return;
            }

            var plan = TerrainLodSeamCoverage.Plan(
                ownerState.Drawn, ownerState.NearProgress,
                neighborState.Drawn, neighborState.NearProgress);
            if (plan.Combined)
            {
                AddSelection(TerrainLodSeamMaterialSide.Either, default);
                return;
            }

            if (plan.Owner)
                AddSelection(TerrainLodSeamMaterialSide.Owner,
                    TerrainLodSeamFade.ForLod(ownerState.NearProgress, ownerState.FadeSeed));
            if (plan.Neighbor)
                AddSelection(TerrainLodSeamMaterialSide.Neighbor,
                    TerrainLodSeamFade.ForLod(neighborState.NearProgress, neighborState.FadeSeed));

            void AddSelection(
                TerrainLodSeamMaterialSide materialSide,
                TerrainLodSeamFade fade)
            {
                var seamKey = new TerrainLodSeamKey(ownerKey, side, materialSide);
                desired[seamKey] = new TerrainLodSeamSelection(
                    owner.Boundaries.Identity,
                    neighbor.Boundaries.Identity,
                    ownerLevel,
                    neighborLevel);
                fades[seamKey] = fade;
            }
        }

    }

    private int UpdateSeams(
        WebGpuDevice device,
        Vector3D<double> viewPosition,
        int uploadBudget,
        bool translucent,
        Dictionary<TerrainLodSeamKey, TerrainLodSeamSelection> desired,
        Dictionary<TerrainLodSeamKey, GpuSeam> installed)
    {
        foreach (var (key, seam) in installed.ToArray())
        {
            if (_resident.ContainsKey(key.Owner) && _resident.ContainsKey(key.Neighbor)) continue;
            installed.Remove(key);
            seam.Dispose();
        }

        var uploads = 0;
        foreach (var group in desired
                     .GroupBy(static pair => (pair.Key.Owner, pair.Key.BoundarySide))
                     .OrderBy(group => DistanceSquared(group.Key.Owner, viewPosition))
                     .ThenBy(static group => group.Key.Owner.X)
                     .ThenBy(static group => group.Key.Owner.Z)
                     .ThenBy(static group => group.Key.BoundarySide))
        {
            var missing = group.Count(pair =>
                !installed.TryGetValue(pair.Key, out var current) ||
                current.Selection != pair.Value);
            if (missing == 0) continue;
            // Owner/neighbor split artifacts are one presentation replacement. Never publish only
            // half because the per-frame seam budget happened to end between them.
            if (uploads + missing > uploadBudget) continue;
            foreach (var (key, selection) in group.OrderBy(static pair => pair.Key.MaterialSide))
            {
                if (installed.TryGetValue(key, out var current) &&
                    current.Selection == selection) continue;
                var owner = _resident[key.Owner];
                var neighbor = _resident[key.Neighbor];
                var data = translucent
                    ? TerrainLodSeamMeshBuilder.BuildTranslucent(
                        owner.Boundaries, selection.OwnerLevel,
                        neighbor.Boundaries, selection.NeighborLevel,
                        key.BoundarySide, _world.Content.Blocks,
                        !_world.Dimension.HasCeiling,
                        lighting: _world.Lighting, visuals: _world.Reader,
                        materialSide: key.MaterialSide,
                        caveCullBelowY: _world.Dimension.HasCeiling
                            ? null
                            : OverworldCaveCullCeilingY)
                    : TerrainLodSeamMeshBuilder.BuildSolid(
                        owner.Boundaries, selection.OwnerLevel,
                        neighbor.Boundaries, selection.NeighborLevel,
                        key.BoundarySide, _world.Content.Blocks,
                        !_world.Dimension.HasCeiling,
                        lighting: _world.Lighting, visuals: _world.Reader,
                        materialSide: key.MaterialSide,
                        caveCullBelowY: _world.Dimension.HasCeiling
                            ? null
                            : OverworldCaveCullCeilingY);
                var candidate = GpuSeam.Create(
                    device, key.Owner, selection, data, translucent);
                if (installed.Remove(key, out var previous)) previous.Dispose();
                installed.Add(key, candidate);
                _boundaryRefreshes++;
                uploads++;
            }
        }
        return uploads;
    }

    private static void CollectVisibleSeams(
        Vector3D<double> viewPosition,
        bool backToFront,
        Dictionary<TerrainLodSeamKey, TerrainLodSeamSelection> desired,
        Dictionary<TerrainLodSeamKey, GpuSeam> installed,
        Dictionary<TerrainLodSeamKey, TerrainLodSeamFade> fades,
        List<VisibleSeam> destination)
    {
        foreach (var group in desired.GroupBy(
                     static pair => (pair.Key.Owner, pair.Key.BoundarySide)))
        {
            var desiredParts = group.ToArray();
            var ready = desiredParts.All(pair =>
                installed.TryGetValue(pair.Key, out var seam) &&
                seam.Selection == pair.Value);
            if (ready)
            {
                foreach (var (key, selection) in desiredParts)
                {
                    var seam = installed[key];
                    if (seam.Selection != selection || seam.Mesh is null) continue;
                    Add(key, seam, fades.GetValueOrDefault(key));
                }
                continue;
            }

            // Keep the previous complete edge until every part of the replacement exists. This
            // may briefly retain a coplanar exact face, but it never turns an upload budget into a
            // visible crack; the split replacement removes that overlap atomically.
            var fallback = installed
                .Where(pair => pair.Key.Owner == group.Key.Owner &&
                               pair.Key.BoundarySide == group.Key.BoundarySide)
                .OrderBy(static pair => pair.Key.MaterialSide ==
                    TerrainLodSeamMaterialSide.Either ? 0 : 1)
                .ThenBy(static pair => pair.Key.MaterialSide)
                .ToArray();
            if (fallback.Any(static pair =>
                    pair.Key.MaterialSide == TerrainLodSeamMaterialSide.Either))
                fallback = fallback.Where(static pair =>
                    pair.Key.MaterialSide == TerrainLodSeamMaterialSide.Either).ToArray();
            foreach (var (key, seam) in fallback)
                if (seam.Mesh is not null) Add(key, seam, default);

            void Add(
                TerrainLodSeamKey key,
                GpuSeam seam,
                TerrainLodSeamFade fade) => destination.Add(new VisibleSeam(
                key, seam, DistanceSquared(key.Owner, viewPosition), fade));
        }
        destination.Sort((a, b) =>
        {
            var distance = backToFront
                ? b.DistanceSquared.CompareTo(a.DistanceSquared)
                : a.DistanceSquared.CompareTo(b.DistanceSquared);
            if (distance != 0) return distance;
            var x = a.Key.Owner.X.CompareTo(b.Key.Owner.X);
            if (x != 0) return x;
            var z = a.Key.Owner.Z.CompareTo(b.Key.Owner.Z);
            if (z != 0) return z;
            var side = a.Key.BoundarySide.CompareTo(b.Key.BoundarySide);
            return side != 0 ? side : a.Key.MaterialSide.CompareTo(b.Key.MaterialSide);
        });
    }

    private int ConstrainLevelToNeighbors(
        (int X, int Z) key,
        int requestedLevel,
        bool translucent)
    {
        Span<int> levels = stackalloc int[4];
        var count = 0;
        count = AppendNeighborLevel((key.X, key.Z - 1), translucent, levels, count);
        count = AppendNeighborLevel((key.X, key.Z + 1), translucent, levels, count);
        count = AppendNeighborLevel((key.X - 1, key.Z), translucent, levels, count);
        count = AppendNeighborLevel((key.X + 1, key.Z), translucent, levels, count);
        return TerrainLodNeighborLevelConstraint.Constrain(
            requestedLevel, levels[..count]);
    }

    private int AppendNeighborLevel(
        (int X, int Z) key,
        bool translucent,
        Span<int> destination,
        int count)
    {
        if (!_resident.TryGetValue(key, out var neighbor) ||
            !neighbor.HasLayer(translucent)) return count;
        var level = neighbor.SelectionLevel(translucent);
        if (level >= 0) destination[count++] = level;
        return count;
    }

    private void EvictDistant(Vector3D<double> viewPosition)
    {
        var residentBytes = _resident.Values.Sum(static value => value.EstimatedBytes) +
                            _solidSeams.Values.Sum(static value => value.EstimatedBytes) +
                            _translucentSeams.Values.Sum(static value => value.EstimatedBytes);
        if (_resident.Count <= ResidentCapacity && residentBytes <= ResidentGpuByteCapacity) return;
        var remove = _resident
            .OrderByDescending(pair => DistanceSquared(pair.Key, viewPosition))
            .ThenBy(pair => pair.Value.LastPresentedTick)
            .ToArray();
        foreach (var candidate in remove)
        {
            if (_resident.Count <= ResidentCapacity && residentBytes <= ResidentGpuByteCapacity) break;
            if (!_resident.Remove(candidate.Key, out var presentation)) continue;
            residentBytes -= presentation.EstimatedBytes;
            residentBytes -= RemoveSolidSeamsForColumn(candidate.Key);
            residentBytes -= RemoveTranslucentSeamsForColumn(candidate.Key);
            presentation.Dispose();
            _evictions++;
        }
    }

    private long RemoveSolidSeamsForColumn((int X, int Z) key)
        => RemoveSeamsForColumn(_solidSeams, key);

    private long RemoveTranslucentSeamsForColumn((int X, int Z) key)
        => RemoveSeamsForColumn(_translucentSeams, key);

    private static long RemoveSeamsForColumn(
        Dictionary<TerrainLodSeamKey, GpuSeam> seams,
        (int X, int Z) key)
    {
        long removedBytes = 0;
        foreach (var (seamKey, seam) in seams
                     .Where(pair => pair.Key.Owner == key || pair.Key.Neighbor == key)
                     .ToArray())
        {
            seams.Remove(seamKey);
            removedBytes += seam.EstimatedBytes;
            seam.Dispose();
        }
        return removedBytes;
    }

    private void PublishSnapshot(int uploads, int draws)
    {
        var conversion = _conversion.Snapshot();
        var preparing = _resident.Values.Count(static value =>
            value.IsInState(TerrainLodHandoffState.NearPreparing));
        var overlap = _resident.Values.Count(static value =>
            value.IsInState(TerrainLodHandoffState.Overlap));
        var levelTransitions = _resident.Values.Count(static value =>
            value.HasActiveLevelTransition);
        var cache = _cache?.Snapshot();
        var cacheWriter = _cacheWriter?.Snapshot();
        var compilation = _meshCompilation.Snapshot();
        var cost = compilation.Cost;
        _snapshot = new ClientTerrainLodSnapshot(
            _pending.Count,
            conversion.OwnedChunks,
            _resident.Count,
            _resident.Values.Count(static value => value.HasLevel(ExactVoxelMeshLevel)),
            _resident.Values.Count(static value => value.HasLevel(TransitionMeshLevel)),
            draws,
            _snapshot.PresentedTranslucentColumns,
            preparing,
            overlap,
            levelTransitions,
            _solidSeams.Count + _translucentSeams.Count,
            _desiredSolidSeams.Count(pair =>
                !_solidSeams.TryGetValue(pair.Key, out var seam) ||
                seam.Selection != pair.Value) +
            _desiredTranslucentSeams.Count(pair =>
                !_translucentSeams.TryGetValue(pair.Key, out var seam) ||
                seam.Selection != pair.Value),
            uploads,
            ResidentGpuBytes(),
            _resident.Values.Sum(static value => value.Boundaries.EstimatedBytes),
            _staleResults,
            _rejectedAdmissions,
            _evictions,
            _handoffsStarted,
            _handoffReversals,
            _levelTransitionsStarted,
            _levelTransitionReversals,
            _boundaryRefreshes,
            cache?.EntryCount ?? 0,
            cache?.CurrentBytes ?? 0,
            cache?.ReadHits ?? 0,
            (cache?.ReadMisses ?? 0) + (cache?.StaleReads ?? 0) +
            (cache?.IncompatibleReads ?? 0),
            cacheWriter?.Queued ?? 0,
            cacheWriter?.Written ?? 0,
            cacheWriter?.RejectedAtCapacity ?? 0,
            (cacheWriter?.Failed ?? 0) + (cache?.CorruptReads ?? 0) +
            (cache?.WriteFailures ?? 0),
            _resourceGeneration,
            _resourceReloads,
            _lastResourceReloadReusedColumns,
            _lastResourceReloadReusedGpuBytes,
            _solidRenderCpuMs,
            _translucentRenderCpuMs,
            compilation.Owned,
            compilation.CoverageQueued,
            compilation.RefinementQueued,
            compilation.CoverageCompleted,
            compilation.RefinementCompleted,
            compilation.CompletedResultBytes,
            compilation.PredictedResultBytes,
            compilation.PredictedCompilationMs,
            compilation.AdmissionDeferrals,
            compilation.UploadAdmissionDeferrals,
            compilation.OversizedUploadAdmissions,
            cost.CompilationSamples,
            cost.UploadSamples,
            cost.CompilationMsPerKCell,
            cost.ResultBytesPerKCell,
            cost.UploadBaseMs,
            cost.UploadMsPerMiB,
            _remoteRequests,
            _remoteTiles,
            _remoteWireBytes,
            _remotePendingResponses,
            _remoteMissingResponses,
            _remoteDeferredResponses,
            _remoteCoverageRequired,
            _remoteCoverageAvailable,
            _remoteCoverageInFlight,
            _remoteCoveragePending,
            _remoteCoverageMissing,
            _remoteCoverageDeferred);
    }

    private long ResidentGpuBytes() =>
        _resident.Values.Sum(static value => value.EstimatedBytes) +
        _solidSeams.Values.Sum(static value => value.EstimatedBytes) +
        _translucentSeams.Values.Sum(static value => value.EstimatedBytes);

    private readonly struct RenderCpuMeasurement : IDisposable
    {
        private readonly ClientTerrainLodRenderer _owner;
        private readonly bool _translucent;
        private readonly long _started = Stopwatch.GetTimestamp();

        public RenderCpuMeasurement(ClientTerrainLodRenderer owner, bool translucent)
        {
            _owner = owner;
            _translucent = translucent;
        }

        public void Dispose()
        {
            var elapsed = Stopwatch.GetElapsedTime(_started).TotalMilliseconds;
            if (_translucent) _owner._translucentRenderCpuMs = elapsed;
            else _owner._solidRenderCpuMs = elapsed;
        }
    }

    private static double DistanceSquared((int X, int Z) key, Vector3D<double> point)
    {
        var dx = key.X * 16 + 8 - point.X;
        var dz = key.Z * 16 + 8 - point.Z;
        return dx * dx + dz * dz;
    }

    private (bool Present, bool Ready) NearState(
        (int X, int Z) key,
        double distanceSquared,
        int renderDistance,
        ChunkRenderer nearRenderer)
    {
        var nearDistance = Math.Max(0, renderDistance) * (double)SubChunkRenderer.Size;
        var nearPresent = distanceSquared < nearDistance * nearDistance &&
                          _world.BlockHost.HasChunk(key.X, key.Z) &&
                          _world.BlockHost.GetChunk(key.X, key.Z).Loaded;
        return (nearPresent,
            nearPresent && nearRenderer.IsMeshColumnReady(key.X, key.Z));
    }

    private TerrainLodHandoffTransition UpdateHandoff(
        ColumnPresentation presentation,
        bool translucent,
        bool nearPresent,
        bool nearReady,
        float deltaTime,
        bool fadeEnabled)
    {
        var before = presentation.HandoffFor(translucent);
        var after = presentation.UpdateHandoff(
            translucent, nearPresent, nearReady, deltaTime, fadeEnabled);
        _handoffsStarted += after.Started - before.Started;
        _handoffReversals += after.Reversals - before.Reversals;
        return after;
    }

    private TerrainLodLevelBlend UpdateLevelTransition(
        ColumnPresentation presentation,
        bool translucent,
        int requestedLevel,
        float deltaTime,
        bool fadeEnabled)
    {
        var before = presentation.LevelTransitionFor(translucent);
        var blend = presentation.UpdateLevelTransition(
            translucent, requestedLevel, deltaTime, fadeEnabled);
        var after = presentation.LevelTransitionFor(translucent);
        _levelTransitionsStarted += after.Started - before.Started;
        _levelTransitionReversals += after.Reversals - before.Reversals;
        return blend;
    }

    private static uint FadeSeed((int X, int Z) key) => unchecked(
        (uint)(key.X * 73_856_093 ^ key.Z * 19_349_663));

    private int AppendVisibleLevels(
        ColumnPresentation presentation,
        (int X, int Z) key,
        double distanceSquared,
        int requestedLevel,
        bool translucent,
        float nearHandoffProgress,
        float deltaTime,
        bool fadeEnabled)
    {
        if (!presentation.TryGetNearestLevel(
                requestedLevel, translucent, out var selectedLevel, out _)) return -1;

        // The exact/LOD handoff owns the single dither mask while it is active. Freeze a hierarchy
        // level transition during that short interval rather than trying to compose two masks.
        var blend = UpdateLevelTransition(presentation,
            translucent, selectedLevel,
            nearHandoffProgress > 0 ? 0 : deltaTime,
            fadeEnabled);
        presentation.LastPresentedTick = _tick;
        var seed = FadeSeed(key);

        if (nearHandoffProgress > 0)
        {
            Add(blend.DominantLevel, fadeMode: 2, nearHandoffProgress);
            return blend.DominantLevel;
        }

        if (blend.Active)
        {
            Add(blend.PrimaryLevel, fadeMode: 2, blend.Progress);
            Add(blend.SecondaryLevel, fadeMode: 1, blend.Progress);
            return blend.DominantLevel;
        }

        Add(blend.PrimaryLevel, fadeMode: 0, fadeProgress: 1);
        return blend.PrimaryLevel;

        void Add(int level, uint fadeMode, float fadeProgress)
        {
            if (!presentation.TryGetLevel(level, translucent, out var gpu)) return;
            _visible.Add(new VisibleColumn(
                key, distanceSquared, level, gpu, fadeProgress, fadeMode, seed));
        }
    }

    private static ChunkDrawMetadata BuildUniforms(
        in ChunkRenderParams parameters,
        (int X, int Z) key,
        float fadeProgress,
        uint fadeMode,
        uint fadeSeed)
    {
        var origin = new Vector3D<int>(key.X * 16, ChuckFormat.WorldHeight / 2, key.Z * 16);
        return BuildUniforms(origin, fadeProgress, fadeMode, fadeSeed);
    }

    private static ChunkDrawMetadata BuildUniforms(
        Vector3D<int> origin,
        float fadeProgress,
        uint fadeMode,
        uint fadeSeed)
    {
        TerrainCoordinateFrame.Split(origin.X, out var cellX, out var localX);
        TerrainCoordinateFrame.Split(origin.Y, out var cellY, out var localY);
        TerrainCoordinateFrame.Split(origin.Z, out var cellZ, out var localZ);
        return new ChunkDrawMetadata
        {
            RegionCellX = cellX,
            RegionCellY = cellY,
            RegionCellZ = cellZ,
            LocalOriginX = localX,
            LocalOriginY = localY,
            LocalOriginZ = localZ,
            ChunkPosX = origin.X,
            ChunkPosY = origin.Z,
            ChunkFadeEnabled = 0,
            FadeProgress = fadeProgress,
            PresentationFadeMode = fadeMode,
            PresentationFadeSeed = fadeSeed
        };
    }

    private static unsafe void DrawSpatialPage(
        RenderPassEncoder* pass,
        in VisibleSpatialPage visible,
        bool translucent,
        ref TerrainStreamBindingState binding,
        uint drawMetadataIndex,
        Vector3D<double> viewPosition)
    {
        var mesh = translucent ? visible.Page.Translucent : visible.Page.Solid;
        if (mesh is null) return;
        var ranges = translucent
            ? visible.Page.TranslucentRanges
            : visible.Page.SolidRanges;
        Span<ChunkQuadRange> selected = stackalloc ChunkQuadRange[7];
        var faceMask = DirectionalFaceVisibility.ForBounds(
            visible.Page.Origin, TerrainLodSpatialMeshBuilder.PageSize, viewPosition);
        var selectedCount = ranges.Select(faceMask, selected);
        if (selectedCount == 0) return;
        mesh.BindChunkQuadStreams(pass, ref binding);
        for (var index = 0; index < selectedCount; index++)
        {
            var range = selected[index];
            mesh.DrawBoundQuadRange(
                pass, (uint)range.FirstQuad, (uint)range.QuadCount,
                firstInstance: drawMetadataIndex);
        }
    }

    private static ChunkFrameUniforms BuildFrameUniforms(in ChunkRenderParams parameters)
    {
        var fog = parameters.Fog;
        var light = RenderSystem.WorldLight;
        TerrainCoordinateFrame.Split(parameters.ViewPos.X, out var cameraCellX, out var cameraLocalX);
        TerrainCoordinateFrame.Split(parameters.ViewPos.Y, out var cameraCellY, out var cameraLocalY);
        TerrainCoordinateFrame.Split(parameters.ViewPos.Z, out var cameraCellZ, out var cameraLocalZ);
        return new ChunkFrameUniforms
        {
            ModelViewMatrix = parameters.ModelView,
            ProjectionMatrix = WgpuClip.FromGl(parameters.Projection),
            CameraCellX = cameraCellX,
            CameraCellY = cameraCellY,
            CameraCellZ = cameraCellZ,
            CameraLocalX = cameraLocalX,
            CameraLocalY = cameraLocalY,
            CameraLocalZ = cameraLocalZ,
            AmbientDarkness = light.AmbientDarkness,
            LuminanceOffset = light.LuminanceOffset,
            FogMode = (uint)fog.Curve,
            FogDensity = fog.Density,
            FogStart = fog.Start,
            FogEnd = fog.End,
            FogColorR = fog.Color.X,
            FogColorG = fog.Color.Y,
            FogColorB = fog.Color.Z,
            FogColorA = fog.Color.W
        };
    }

    private enum RemoteTileRequestDisposition
    {
        InFlight,
        Pending,
        Missing,
        Deferred
    }

    private readonly record struct RemoteTileRequestState(
        long RetryAfterTick,
        RemoteTileRequestDisposition Disposition);

    private readonly record struct PendingColumn(long DueTick);
    private readonly record struct VisibleColumn(
        (int X, int Z) Key,
        double DistanceSquared,
        int Level,
        GpuLevel Gpu,
        float FadeProgress,
        uint FadeMode,
        uint FadeSeed);

    private readonly record struct VisibleSeam(
        TerrainLodSeamKey Key,
        GpuSeam Gpu,
        double DistanceSquared,
        TerrainLodSeamFade Fade);

    private readonly record struct VisibleSpatialPage(
        TerrainLodSpatialGpuPresentation.GpuPage Page,
        TerrainLodSpatialPresentationFade Fade,
        double DistanceSquared);

    private readonly record struct SpatialSeamHashCache(
        string OwnerCanonicalHash,
        string? NeighborCanonicalHash,
        int? CaveCullBelowY,
        string CanonicalHash);

    private readonly record struct SpatialForestCacheKey(
        double CameraChunkX,
        double CameraChunkZ,
        int HorizonDistance,
        long PresentationRevision);

    private readonly record struct VisibleTranslucentDraw(
        VisibleColumn? Column,
        GpuSeam? Seam,
        VisibleSpatialPage? Spatial,
        (int X, int Z) Key,
        double DistanceSquared,
        TerrainLodSeamFade SeamFade = default);

    private readonly record struct TerrainLodSeamColumnState(
        int Level,
        float NearProgress,
        uint FadeSeed,
        bool Drawn);

    private readonly record struct TerrainLodSeamKey(
        (int X, int Z) Owner,
        Side BoundarySide,
        TerrainLodSeamMaterialSide MaterialSide)
    {
        public (int X, int Z) Neighbor => BoundarySide == global::OmniBlock.Blocks.Side.East
            ? (Owner.X + 1, Owner.Z)
            : (Owner.X, Owner.Z + 1);
    }

    private readonly record struct TerrainLodSeamSelection(
        TerrainLodBoundaryIdentity OwnerIdentity,
        TerrainLodBoundaryIdentity NeighborIdentity,
        int OwnerLevel,
        int NeighborLevel);

    private sealed class ColumnPresentation : IDisposable
    {
        private ColumnPresentation(
            long terrainRevision,
            Dictionary<int, GpuLevel> levels,
            TerrainLodBoundarySummary boundaries)
        {
            TerrainRevision = terrainRevision;
            Levels = levels;
            Boundaries = boundaries;
            MinimumLevel = levels.Count == 0 ? MinimumHorizonMeshLevel : levels.Keys.Min();
            MaximumLevel = levels.Count == 0 ? MinimumHorizonMeshLevel : levels.Keys.Max();
        }

        public long TerrainRevision { get; }
        public Dictionary<int, GpuLevel> Levels { get; }
        public TerrainLodBoundarySummary Boundaries { get; }
        public int MinimumLevel { get; }
        public int MaximumLevel { get; }
        public long LastPresentedTick { get; set; }
        public long EstimatedBytes => Levels.Values.Sum(static level => level.EstimatedBytes);
        private TerrainLodHandoffTransition SolidHandoff;
        private TerrainLodHandoffTransition TranslucentHandoff;
        private TerrainLodLevelTransition SolidLevelTransition;
        private TerrainLodLevelTransition TranslucentLevelTransition;
        public bool HasLevel(int level) => Levels.ContainsKey(level);
        public bool HasLayer(bool translucent) => Levels.Values.Any(level => translucent
            ? level.TranslucentMesh is not null
            : level.SolidMesh is not null);

        public TerrainLodHandoffTransition HandoffFor(bool translucent) =>
            translucent ? TranslucentHandoff : SolidHandoff;

        public int SelectionLevel(bool translucent) => translucent
            ? TranslucentLevelTransition.SelectionLevel
            : SolidLevelTransition.SelectionLevel;
        public bool HasActiveLevelTransition =>
            SolidLevelTransition.Active || TranslucentLevelTransition.Active;
        public TerrainLodLevelTransition LevelTransitionFor(bool translucent) =>
            translucent ? TranslucentLevelTransition : SolidLevelTransition;

        public TerrainLodLevelBlend UpdateLevelTransition(
            bool translucent,
            int requestedLevel,
            float deltaTime,
            bool fadeEnabled)
        {
            return translucent
                ? TranslucentLevelTransition.Update(requestedLevel, deltaTime, fadeEnabled)
                : SolidLevelTransition.Update(requestedLevel, deltaTime, fadeEnabled);
        }

        public TerrainLodHandoffTransition UpdateHandoff(
            bool translucent,
            bool nearPresent,
            bool nearReady,
            float deltaTime,
            bool fadeEnabled)
        {
            if (translucent)
            {
                TranslucentHandoff.Update(nearPresent, nearReady, deltaTime, fadeEnabled);
                return TranslucentHandoff;
            }

            SolidHandoff.Update(nearPresent, nearReady, deltaTime, fadeEnabled);
            return SolidHandoff;
        }

        public bool IsInState(TerrainLodHandoffState state) =>
            SolidHandoff.State == state || TranslucentHandoff.State == state;

        public void InitializeNearOnly()
        {
            SolidHandoff.InitializeNearOnly();
            TranslucentHandoff.InitializeNearOnly();
        }

        public void CopyHandoffsFrom(ColumnPresentation previous)
        {
            SolidHandoff.CopyFrom(previous.SolidHandoff);
            TranslucentHandoff.CopyFrom(previous.TranslucentHandoff);
            SolidLevelTransition.CopyFrom(previous.SolidLevelTransition);
            TranslucentLevelTransition.CopyFrom(previous.TranslucentLevelTransition);
        }

        public bool TryGetLevel(int level, bool translucent, out GpuLevel gpu)
        {
            if (!Levels.TryGetValue(level, out gpu!)) return false;
            return translucent ? gpu.TranslucentMesh is not null : gpu.SolidMesh is not null;
        }

        public bool TryGetNearestLevel(
            int requested,
            bool translucent,
            out int selected,
            out GpuLevel gpu)
        {
            if (Levels.TryGetValue(requested, out gpu!) && HasRequestedLayer(gpu))
            {
                selected = requested;
                return true;
            }

            foreach (var candidate in Levels.Keys
                         .OrderBy(level => Math.Abs(level - requested))
                         .ThenBy(level => level))
            {
                var candidateGpu = Levels[candidate];
                if (!HasRequestedLayer(candidateGpu)) continue;
                selected = candidate;
                gpu = candidateGpu;
                return true;
            }

            selected = -1;
            gpu = null!;
            return false;

            bool HasRequestedLayer(GpuLevel candidate) => translucent
                ? candidate.TranslucentMesh is not null
                : candidate.SolidMesh is not null;
        }

        public static ColumnPresentation Create(TerrainLodMeshCompilationResult compiled)
        {
            var device = WebGpuDevice.Current
                ?? throw new InvalidOperationException("Terrain LOD upload requires a WebGPU device.");
            Dictionary<int, GpuLevel> levels = [];
            try
            {
                foreach (var data in compiled.Levels)
                {
                    levels.Add(data.Level, GpuLevel.Create(
                        device,
                        compiled.Conversion.ChunkX,
                        compiled.Conversion.ChunkZ,
                        data));
                }
                return new ColumnPresentation(
                    compiled.Conversion.TerrainRevision,
                    levels,
                    compiled.Boundaries ?? throw new InvalidOperationException(
                        "Compiled terrain LOD result has no boundary summary."));
            }
            catch
            {
                foreach (var level in levels.Values) level.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var level in Levels.Values) level.Dispose();
            Levels.Clear();
        }
    }

    private sealed class GpuLevel : IDisposable
    {
        private GpuLevel(
            WgpuMesh? solidMesh,
            WgpuMesh? translucentMesh,
            SectionLighting? lighting,
            long estimatedBytes)
        {
            SolidMesh = solidMesh;
            TranslucentMesh = translucentMesh;
            Lighting = lighting;
            EstimatedBytes = estimatedBytes;
        }

        public WgpuMesh? SolidMesh { get; }
        public WgpuMesh? TranslucentMesh { get; }
        public SectionLighting? Lighting { get; }
        public long EstimatedBytes { get; }

        public static GpuLevel Create(
            WebGpuDevice device, int chunkX, int chunkZ, TerrainLodMeshData data)
        {
            if (data.Vertices.Length == 0 && data.TranslucentVertices.Length == 0)
                return new GpuLevel(null, null, null, 0);
            WgpuMesh? solidMesh = null;
            WgpuMesh? translucentMesh = null;
            SectionLighting? lighting = null;
            try
            {
                var origin = new Vector3D<int>(
                    chunkX * 16, ChuckFormat.WorldHeight / 2, chunkZ * 16);
                SectionLightModel? solidLight = null;
                SectionLightModel? translucentLight = null;
                if (data.Vertices.Length > 0)
                {
                    solidMesh = WgpuMesh.FromChunkQuads(device, data.Vertices);
                    solidLight = SectionLightModel.Create(origin, data.Vertices, data.Lights);
                }
                if (data.TranslucentVertices.Length > 0)
                {
                    translucentMesh = WgpuMesh.FromChunkQuads(device, data.TranslucentVertices);
                    translucentLight = SectionLightModel.Create(
                        origin, data.TranslucentVertices, data.TranslucentLights);
                }
                lighting = SectionLighting.CreateInitial(device, solidLight, translucentLight);
                return new GpuLevel(
                    solidMesh, translucentMesh, lighting, data.EstimatedBytes);
            }
            catch
            {
                solidMesh?.Dispose();
                translucentMesh?.Dispose();
                lighting?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            SolidMesh?.Dispose();
            TranslucentMesh?.Dispose();
            Lighting?.Dispose();
        }
    }

    private sealed class GpuSeam : IDisposable
    {
        private GpuSeam(
            TerrainLodSeamSelection selection,
            WgpuMesh? mesh,
            SectionLighting? lighting,
            long estimatedBytes)
        {
            Selection = selection;
            Mesh = mesh;
            Lighting = lighting;
            EstimatedBytes = estimatedBytes;
        }

        public TerrainLodSeamSelection Selection { get; }
        public WgpuMesh? Mesh { get; }
        public SectionLighting? Lighting { get; }
        public long EstimatedBytes { get; }

        public static GpuSeam Create(
            WebGpuDevice device,
            (int X, int Z) owner,
            TerrainLodSeamSelection selection,
            TerrainLodSeamMeshData data,
            bool translucent)
        {
            if (data.Vertices.Length == 0)
                return new GpuSeam(selection, null, null, 0);
            WgpuMesh? mesh = null;
            SectionLighting? lighting = null;
            try
            {
                mesh = WgpuMesh.FromChunkQuads(device, data.Vertices);
                var origin = new Vector3D<int>(
                    owner.X * 16, ChuckFormat.WorldHeight / 2, owner.Z * 16);
                var lightModel = SectionLightModel.Create(origin, data.Vertices, data.Lights);
                lighting = SectionLighting.CreateInitial(
                    device,
                    translucent ? null : lightModel,
                    translucent ? lightModel : null);
                return new GpuSeam(selection, mesh, lighting, data.EstimatedBytes);
            }
            catch
            {
                mesh?.Dispose();
                lighting?.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            Mesh?.Dispose();
            Lighting?.Dispose();
        }
    }
}

/// <summary>
///     Extends ordinary linear terrain fog across the distant horizon. Both exact and reduced
///     terrain consume this resolved state, so their overlap cannot reveal two different fog
///     depths for the same surface.
/// </summary>
internal static class TerrainLodFog
{
    public static FogState Resolve(
        FogState source,
        int renderDistance,
        int terrainHorizonDistance,
        int fogDistance)
    {
        if (source.Curve != FogCurve.Linear) return source;

        var nearDistance = Math.Max(1, renderDistance) * (float)SubChunkRenderer.Size;
        var horizon = Math.Max(renderDistance, terrainHorizonDistance) *
                      (float)SubChunkRenderer.Size;
        var end = Math.Clamp(
            Math.Max(1, fogDistance) * (float)SubChunkRenderer.Size,
            nearDistance, horizon);
        var start = Math.Clamp(source.Start, 0, end - 1);
        return source with { Start = start, End = end };
    }
}

internal static class TerrainLodAdmissionOrder
{
    /// <summary>
    ///     Missing coverage closest to the camera is admitted first. Coordinate tie-breakers make
    ///     equal-distance rings deterministic without introducing a directional preference.
    /// </summary>
    public static (int X, int Z)[] TakeNearest(
        IEnumerable<(int X, int Z)> coordinates,
        Vector3D<double> viewPosition,
        int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        return coordinates
            .OrderBy(key => DistanceSquared(key, viewPosition))
            .ThenBy(key => key.X)
            .ThenBy(key => key.Z)
            .Take(count)
            .ToArray();
    }

    private static double DistanceSquared((int X, int Z) key, Vector3D<double> point)
    {
        var dx = key.X * 16 + 8 - point.X;
        var dz = key.Z * 16 + 8 - point.Z;
        return dx * dx + dz * dz;
    }
}

/// <summary>Distance thresholds with hysteresis so neighboring LOD levels do not flicker.</summary>
internal static class TerrainLodDetailSelector
{
    private const double TargetProjectedCellPixels = 5;
    private const double Hysteresis = 0.10;
    // Fine tiers are deliberately bounded even on high-resolution displays. They are transition
    // coverage, not a second full-resolution copy of the entire render distance.
    private const double MaximumExactVoxelDistance = 96;
    private const double MaximumTransitionDistance = 256;

    public static int SelectLevel(
        double distance,
        int maximumLevel,
        int previousLevel = -1,
        double verticalFovDegrees = 70,
        int viewportHeight = 480,
        double detailDropoffScale = 1)
    {
        if (!double.IsFinite(distance) || distance < 0)
            throw new ArgumentOutOfRangeException(nameof(distance));
        var maximum = Math.Clamp(maximumLevel, 0, 4);
        if (!double.IsFinite(verticalFovDegrees) || verticalFovDegrees is <= 1 or >= 179)
            verticalFovDegrees = 70;
        if (viewportHeight <= 0) viewportHeight = 480;
        if (!double.IsFinite(detailDropoffScale) || detailDropoffScale <= 0)
            detailDropoffScale = 1;

        var focalLength = viewportHeight /
                          (2 * Math.Tan(verticalFovDegrees * Math.PI / 360));
        var exactToTransition = Math.Min(
            2 * focalLength / TargetProjectedCellPixels, MaximumExactVoxelDistance) *
            detailDropoffScale;
        var transitionToFine = Math.Min(
            4 * focalLength / TargetProjectedCellPixels, MaximumTransitionDistance) *
            detailDropoffScale;
        var fineToMedium = 8 * focalLength / TargetProjectedCellPixels * detailDropoffScale;
        var mediumToCoarse = 16 * focalLength / TargetProjectedCellPixels * detailDropoffScale;
        var desired = distance < exactToTransition
            ? 0
            : distance < transitionToFine
                ? 1
                : distance < fineToMedium
                    ? 2
                    : distance < mediumToCoarse
                        ? 3
                        : 4;
        desired = Math.Min(desired, maximum);

        if (previousLevel == 0 && desired > 0 &&
            distance < exactToTransition * (1 + Hysteresis)) return 0;
        if (previousLevel == 1)
        {
            if (desired < 1 && distance > exactToTransition * (1 - Hysteresis)) return 1;
            if (desired > 1 && distance < transitionToFine * (1 + Hysteresis)) return 1;
        }
        if (previousLevel == 2)
        {
            if (desired < 2 && distance > transitionToFine * (1 - Hysteresis)) return 2;
            if (desired > 2 && distance < fineToMedium * (1 + Hysteresis)) return 2;
        }
        if (previousLevel == 3)
        {
            if (desired < 3 && distance > fineToMedium * (1 - Hysteresis)) return 3;
            if (desired > 3 && distance < mediumToCoarse * (1 + Hysteresis)) return 3;
        }
        if (previousLevel == 4 && desired < 4 &&
            distance > mediumToCoarse * (1 - Hysteresis)) return Math.Min(4, maximum);
        return desired;
    }
}

/// <summary>
///     Keeps a column within one level of an already-stable neighbor. If inherited state is itself
///     inconsistent (for example immediately after teleport), the midpoint is deterministic and
///     lets the local field converge without iteration-order bias.
/// </summary>
internal static class TerrainLodNeighborLevelConstraint
{
    public static int Constrain(int requestedLevel, ReadOnlySpan<int> neighborLevels)
    {
        if (neighborLevels.IsEmpty) return requestedLevel;
        var lower = int.MinValue;
        var upper = int.MaxValue;
        var minimum = int.MaxValue;
        var maximum = int.MinValue;
        foreach (var level in neighborLevels)
        {
            lower = Math.Max(lower, level - 1);
            upper = Math.Min(upper, level + 1);
            minimum = Math.Min(minimum, level);
            maximum = Math.Max(maximum, level);
        }

        if (lower <= upper) return Math.Clamp(requestedLevel, lower, upper);
        var midpointLow = (minimum + maximum) / 2;
        var midpointHigh = (minimum + maximum + 1) / 2;
        return Math.Abs(requestedLevel - midpointLow) <=
               Math.Abs(requestedLevel - midpointHigh)
            ? midpointLow
            : midpointHigh;
    }
}
