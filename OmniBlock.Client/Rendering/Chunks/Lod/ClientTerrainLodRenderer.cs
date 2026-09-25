using System.Diagnostics;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Client.Worlds;
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
    long NetworkTilesReceived,
    long NetworkTilesAdmitted,
    int NetworkTileQueueDepth,
    int NetworkTileQueuePeak,
    int TransportQueueDepth,
    int TransportQueuePeak,
    long RemotePendingResponses,
    long RemoteMissingResponses,
    long RemoteDeferredResponses,
    int RemoteCoverageRequired,
    int RemoteCoverageAvailable,
    int RemoteCoverageInFlight,
    int RemoteCoveragePending,
    int RemoteCoverageMissing,
    int RemoteCoverageDeferred,
    int CoarseCoverSourceUnavailable,
    int CoarseCoverBuilding,
    int CoarseCoverTransportPending,
    int CoarseCoverGpuPending,
    int CoarseCoverReady,
    int CoarseCoverAwaitingRequest,
    int CoarseCoverFrontierUnknown,
    bool CoarseCoverComplete,
    bool CoarseCoverRetainingPrevious,
    double ColdCoverMs,
    double FirstCompleteHorizonMs,
    double RefinementMs,
    TerrainLodConvergenceSnapshot Convergence);

/// <summary>
///     Generation-scoped time-to-first-cover milestones. Values are milliseconds from the moment
///     the required radial partition changed; -1 means that stage has not completed yet.
/// </summary>
internal readonly record struct TerrainLodConvergenceSnapshot(
    long Generation,
    double FirstRequestMs,
    double FirstSourceTileMs,
    double SourceCompleteMs,
    double FirstBodyUploadMs,
    double BodiesCompleteMs,
    double FirstSeamUploadMs,
    double SeamsCompleteMs,
    double PublicationMs,
    long BodyUploads,
    long BodyUploadBytes,
    double BodyInstallMs,
    long SeamUploads,
    long SeamUploadBytes,
    double SeamInstallMs);

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
    TerrainLodSpatialSeamCompilationSnapshot SeamCompilation,
    int PinnedPresentations,
    long GpuEvictions,
    long CpuEvictions);

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
internal sealed partial class ClientTerrainLodRenderer : IDisposable, ITerrainPresentationHandoff
{
    private const int ConversionCapacity = 16;
    private readonly TerrainLodVisualCaptures _conversionVisuals = new(ConversionCapacity);
    // Full-detail upgrades are rare, but must remain possible after the simulation unloads a
    // nearby column. Bound the retained source separately from resident GPU mesh capacity.
    private readonly TerrainLodRefinementSources _refinementSources = new(64L * 1024 * 1024, 192);
    private long _unloadedConversionsPreserved;
    private const int PendingCapacity = 4096;
    private const int ResidentCapacity = 2048;
    private const long ResidentGpuByteCapacity = 128L * 1024 * 1024;
    private const int SnapshotsPerTick = 4;
    private const double MeshUploadBudgetMs = 1.0;
    private const long MeshUploadBudgetBytes = TerrainLodScaleBudget.MaximumUploadBytesPerFrame;
    private const int SeamUploadsPerFrame = 2;
    private const int SeamDrawsPerFrame = 256;
    private const int QuietTicks = 2;
    private const int ExactVoxelMeshLevel = 0;
    private const int TransitionMeshLevel = 1;
    private const int MinimumHorizonMeshLevel = 2;
    private const int MaximumMeshLevel = 4;
    private const int MinimumSpatialGpuLevel = 2;
    private const int SpatialParentResultsPerTick = 8;
    private const int SpatialMeshAdmissionsPerTick = 8;
    private const int SpatialUploadsPerFrame = TerrainLodScaleBudget.SpatialUploadsPerFrame;
    private const int SpatialSeamAdmissionsPerFrame = 8;
    private const int SpatialSeamUploadsPerFrame =
        TerrainLodScaleBudget.SpatialSeamUploadsPerFrame;
    // A cold horizon can install hundreds of bodies. Re-running connected-cover selection after
    // every individual upload made startup quadratic and produced visible 300 ms frame spikes.
    // Partial covers advance atomically in small batches; the final complete cover bypasses the
    // batch immediately.
    private const int MaximumRemoteOutstandingRequests =
        TerrainLodScaleBudget.MaximumRemoteOutstandingRequests;
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
    private readonly Dictionary<TerrainLodTileKey, long> _remoteRefreshNeeded = [];
    private readonly Dictionary<TerrainLodTileKey, long> _remoteTileGenerations = [];
    private readonly HashSet<TerrainLodSpatialSeamSegment> _desiredSpatialSeams = [];
    private readonly Dictionary<TerrainLodSpatialSeamSegment,
        TerrainLodSpatialGpuSeamPresentation> _spatialSeams = [];
    private readonly Dictionary<TerrainLodSpatialSeamSegment,
        TerrainLodSpatialPresentationFade> _spatialSeamFades = [];
    private readonly Dictionary<TerrainLodSpatialSeamSegment,
        SpatialSeamHashCache> _spatialSeamHashes = [];
    private readonly HashSet<TerrainLodTileKey> _authoritativeSpatialTiles = [];
    private readonly HashSet<(int X, int Z)> _spatialReplacementColumns = [];
    private readonly HashSet<(int X, int Z)> _completedSpatialHandoffs = [];
    private int _spatialPlanningNearDistance = -1;
    private readonly Dictionary<(int X, int Z), ulong> _spatialColumnMasks = [];
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
    private readonly HashSet<(int X, int Z)> _coverageFootprint = [];
    private readonly Dictionary<((int X, int Z) Column, uint Mode), int>
        _coverageSpatialBodies = [];
    private readonly HashSet<(int X, int Z)> _coverageSpatialConflicts = [];
    private readonly List<TerrainCoverageColumn> _coverageColumns = [];
    private readonly HashSet<TerrainLodSpatialSeamSegment> _coverageSpatialSeams = [];
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
    private int _coarseCoverSourceUnavailable;
    private int _coarseCoverBuilding;
    private int _coarseCoverTransportPending;
    private int _coarseCoverGpuPending;
    private int _coarseCoverReady;
    private int _coarseCoverAwaitingRequest;
    private int _coarseCoverFrontierUnknown;
    private bool _coarseCoverComplete;
    private bool _coarseCoverRetainingPrevious;
    private double _coldCoverMs = -1;
    private double _firstCompleteHorizonMs = -1;
    private double _refinementMs = -1;
    private double _firstRequestMs = -1;
    private double _firstSourceTileMs = -1;
    private double _sourceCompleteMs = -1;
    private double _firstBodyUploadMs = -1;
    private double _bodiesCompleteMs = -1;
    private double _firstSeamUploadMs = -1;
    private double _seamsCompleteMs = -1;
    private double _publicationMs = -1;
    private long _bodyUploads;
    private long _bodyUploadBytes;
    private double _bodyInstallMs;
    private long _seamUploads;
    private long _seamUploadBytes;
    private double _seamInstallMs;
    private int _coarseCoverSelectedTiles;
    private int _coarseCoverParentFallbacks;
    private int _coarseCoverMissingGroups;
    private int _coarseOuterBoundaryMinimumLevel = MinimumSpatialGpuLevel;
    private int _spatialPinnedPresentations;
    private long _spatialGpuEvictions;
    private long _spatialCpuEvictions;
    private int _lastSpatialResidencyChunkX = int.MinValue;
    private int _lastSpatialResidencyChunkZ = int.MinValue;
    private int _lastSpatialResidencyHorizon = -1;
    private long _lastSpatialResidencyRevision = -1;
    private long _lastSpatialResidencyGpuBytes;
    private int _lastSpatialResidencyCpuTiles;
    private TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation>?
        _lastSpatialResidencyFrame;
    private long _coarseCoverStartedTimestamp;
    private long _coarseCoverGeneration;
    private long _publishedCoarseCoverGeneration = -1;
    private TerrainLodTileKey[] _coarseRequiredTiles = [];
    private bool _disposed;
    private long _lastCoverageTick = -1;
    private ClientTerrainLodSnapshot _snapshot;
    private TerrainLodSpatialSnapshot _spatialSnapshot;
    private TerrainCoverageSnapshot _coverageSnapshot;
    private readonly TerrainLodSpatialPublication<TerrainLodSpatialGpuPresentation, TerrainLodSpatialGpuSeamPresentation>
        _spatialPublication = new();
    private TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation>?
        _spatialFrame => _spatialPublication.Frame;
    private SpatialForestCacheKey? _spatialForestCacheKey;
    private TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation>?
        _spatialForestCachedFrame;
    private TerrainLodSpatialMeshCompilationResult? _deferredSpatialMeshUpload;
    private TerrainLodSpatialSeamCompilationResult? _deferredSpatialSeamUpload;
    private bool _spatialSubmissionReady => _spatialFrame is not null;

    public ClientTerrainLodRenderer(
        World world,
        TerrainLodCacheStore? cache = null,
        TerrainLodSpatialPolicy? spatialPolicy = null)
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
        // Shared with the server cache producer. The generated policy preserves levels 0..4 used
        // by the 64-chunk renderer and adds only the ancestors needed by larger horizons.
        _spatialPolicy = spatialPolicy ?? TerrainLodSpatialPolicy.CreateDefault();
        _spatialHierarchy = new TerrainLodSpatialHierarchyCoordinator(
            _spatialPolicy,
            tileCapacity: TerrainLodScaleBudget.ClientHierarchyTiles,
            constructionCapacity: 64,
            completedCapacity: 16);
        _spatialMeshCompilation = new TerrainLodSpatialMeshCompilationService(
            capacity: TerrainLodScaleBudget.SpatialMeshCompilationItems,
            completedCapacity: TerrainLodScaleBudget.SpatialMeshCompletedItems);
        _spatialSeamCompilation = new TerrainLodSpatialSeamCompilationService(
            capacity: TerrainLodScaleBudget.SpatialSeamCompilationItems,
            completedCapacity: TerrainLodScaleBudget.SpatialSeamCompletedItems);
    }

    public ClientTerrainLodSnapshot Snapshot => _snapshot;
    public TerrainLodSpatialSnapshot SpatialSnapshot => _spatialSnapshot;
    public TerrainCoverageSnapshot CoverageSnapshot => _coverageSnapshot;
    internal bool HasPendingRemoteRefresh(TerrainLodTileKey key) =>
        _remoteRefreshNeeded.ContainsKey(key);

    public void ObserveRemoteSpatialTile(TerrainLodColumnTile tile, int wireBytes = 0,
        long generation = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(tile);
        if (tile.Key.Level < MinimumSpatialGpuLevel ||
            tile.Key.Level > _spatialPolicy.MaximumSpatialLevel) return;
        if (generation < _remoteRefreshNeeded.GetValueOrDefault(tile.Key) ||
            generation < _remoteTileGenerations.GetValueOrDefault(tile.Key)) return;
        RecordRemoteSource(tile);
        var unchanged = _spatialHierarchy.TryGetCoverage(tile.Key, out var previous, out _) &&
                        previous?.CanonicalHash == tile.CanonicalHash;
        var publication = _spatialHierarchy.PublishCached(tile);
        if (!unchanged && publication != TerrainLodTilePublicationResult.IgnoredCurrent)
            QueueSpatialMesh(tile);
        _remoteTileGenerations[tile.Key] = generation;
        _remoteRequestStates.Remove(tile.Key);
        _remoteRefreshNeeded.Remove(tile.Key);
        if (_firstSourceTileMs < 0)
            _firstSourceTileMs = CoarseCoverElapsedMs();
        _remoteTiles++;
        _remoteWireBytes += Math.Max(0, wireBytes);
    }

    public void ObserveRemoteSpatialStatus(TerrainLodTileKey key, TerrainLodTileStatus status,
        long generation = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (key.Level < MinimumSpatialGpuLevel ||
            key.Level > _spatialPolicy.MaximumSpatialLevel) return;
        if (generation < _remoteRefreshNeeded.GetValueOrDefault(key) ||
            generation < _remoteTileGenerations.GetValueOrDefault(key)) return;
        switch (status)
        {
            case TerrainLodTileStatus.Invalidated:
                // Keep both the old source and GPU presentation until a replacement is fully
                // built. Refresh requests bypass the ordinary "already covered" shortcut.
                if (generation <= _remoteTileGenerations.GetValueOrDefault(key)) break;
                if (_remoteRequestStates.ContainsKey(key) ||
                    _spatialHierarchy.TryGetCoverage(key, out _, out _) ||
                    _spatialPresentations.IsReady(key))
                    _remoteRefreshNeeded[key] = Math.Max(generation,
                        _remoteRefreshNeeded.GetValueOrDefault(key));
                _remoteRequestStates.Remove(key);
                break;
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
        int maximumRequests,
        int maximumSpatialLevel = TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (maximumSpatialLevel < MinimumSpatialGpuLevel ||
            maximumSpatialLevel > _spatialPolicy.MaximumSpatialLevel)
            throw new ArgumentOutOfRangeException(nameof(maximumSpatialLevel));
        // Render-thread publication may still need the old inner annulus while a newly
        // requested exact radius streams in. Keep requesting that same cover.
        if (_spatialPlanningNearDistance >= 0)
            nearDistanceChunks = Math.Min(nearDistanceChunks, _spatialPlanningNearDistance);
        horizonDistanceChunks = Math.Min(
            horizonDistanceChunks,
            TerrainLodSpatialPolicy.MaximumHorizonChunksForSpatialLevel(maximumSpatialLevel));
        var cameraX = viewPosition.X / 16.0;
        var cameraZ = viewPosition.Z / 16.0;
        var coverageRootLevel = Math.Min(maximumSpatialLevel, Math.Max(
            MinimumSpatialGpuLevel,
            _spatialPolicy.DesiredSpatialLevel(horizonDistanceChunks)));
        var outerBoundaryMinimumLevel =
            TerrainLodCoveragePlanner.RecommendedOuterBoundaryMinimumLevel(
                coverageRootLevel, MinimumSpatialGpuLevel);
        var coveragePlan = TerrainLodCoveragePlanner.PlanRequiredTiles(
            cameraX, cameraZ, nearDistanceChunks, horizonDistanceChunks,
            coverageRootLevel, MinimumSpatialGpuLevel, outerBoundaryMinimumLevel);
        outerBoundaryMinimumLevel = coveragePlan.EffectiveOuterBoundaryMinimumLevel;
        _coarseOuterBoundaryMinimumLevel = outerBoundaryMinimumLevel;
        var requiredTiles = TerrainLodCoveragePlanner.PrioritizeMissing(
            coveragePlan.Tiles,
            cameraX, cameraZ, _spatialPolicy);
        UpdateCoarseCoverPlan(requiredTiles);
        UpdateRemoteCoverage(requiredTiles, cameraX, cameraZ, horizonDistanceChunks,
            outerBoundaryMinimumLevel);
        // Outstanding-request and server transport budgets already bound this lane. The old
        // every-fourth-tick gate turned a 289-node warm cache into a tens-of-seconds handshake,
        // especially when mesh work lengthened client ticks.
        if (maximumRequests <= 0 || requiredTiles.Length == 0)
            return [];

        var availableCapacity = MaximumRemoteOutstandingRequests -
            _remoteRequestStates.Values.Count(state =>
                state.Disposition == RemoteTileRequestDisposition.InFlight &&
                state.RetryAfterTick > _tick);
        if (availableCapacity <= 0) return [];

        var requestCount = Math.Min(maximumRequests, availableCapacity);
        List<TerrainLodTileKey> selected = [];
        // Reserve a tiny fair lane for previously presented tiles that the server invalidated.
        // Otherwise an endless newly discovered horizon can starve a visible replacement.
        foreach (var key in _remoteRefreshNeeded.Keys
                     .Where(key => key.DistanceTo(cameraX, cameraZ) <= horizonDistanceChunks)
                     .OrderBy(key => key.DistanceTo(cameraX, cameraZ))
                     .ThenBy(static key => key.Level)
                     .ThenBy(static key => key.X)
                     .ThenBy(static key => key.Z))
        {
            if (selected.Count >= Math.Min(2, requestCount)) break;
            if (_remoteRequestStates.TryGetValue(key, out var state) &&
                _tick < state.RetryAfterTick) continue;
            selected.Add(key);
        }
        // The adaptive partition defines the no-hole contract and always consumes request capacity
        // before visual refinement, apart from the bounded invalidation lane above. It is already
        // deterministic and near-to-far.
        foreach (var key in requiredTiles)
        {
            if (selected.Count >= requestCount) break;
            CollectCoverageRequests(key, Math.Min(key.Level, outerBoundaryMinimumLevel));
        }

        // Refinement may use only capacity not needed by a due coverage tile. Cycling levels keeps
        // the request set bounded while the near-to-far order remains stable within each level.
        if (selected.Count < requestCount && requiredTiles.All(key =>
                CoverageRequestResolved(
                    key, Math.Min(key.Level, outerBoundaryMinimumLevel))))
        {
            var refinementLevels = coverageRootLevel - outerBoundaryMinimumLevel;
            if (refinementLevels > 0)
            {
                var level = coverageRootLevel - 1 -
                            (int)((_tick >> 2) % refinementLevels);
                var refinements = new List<TerrainLodTileKey>();
                if (TerrainLodCoveragePlanner.TryPlanRequiredTiles(
                        cameraX, cameraZ, nearDistanceChunks,
                        horizonDistanceChunks, level, MinimumSpatialGpuLevel,
                        Math.Min(level, outerBoundaryMinimumLevel),
                        out var refinementPlan))
                {
                    foreach (var key in refinementPlan!.Tiles)
                    {
                        if (selected.Contains(key) ||
                            _spatialHierarchy.TryGetCoverage(key, out _, out _))
                            continue;
                        if (_remoteRequestStates.TryGetValue(key, out var state) &&
                            _tick < state.RetryAfterTick)
                            continue;
                        refinements.Add(key);
                    }
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
        if (selected.Count > 0 && _firstRequestMs < 0)
            _firstRequestMs = CoarseCoverElapsedMs();
        _remoteRequests += selected.Count;
        return [.. selected];

        void CollectCoverageRequests(TerrainLodTileKey key, int fallbackMinimumLevel)
        {
            if (selected.Count >= requestCount ||
                _spatialHierarchy.HasCompleteCoverage(key, fallbackMinimumLevel))
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
                    key.Level == fallbackMinimumLevel)
                    return;
                for (var index = 0; index < 4; index++)
                    CollectCoverageRequests(key.Child(index), fallbackMinimumLevel);
                return;
            }
            selected.Add(key);
        }
    }

    private void UpdateRemoteCoverage(
        IReadOnlyCollection<TerrainLodTileKey> requiredTiles,
        double cameraChunkX,
        double cameraChunkZ,
        int horizonDistanceChunks,
        int outerBoundaryMinimumLevel)
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
        _coarseCoverSourceUnavailable = 0;
        _coarseCoverBuilding = 0;
        _coarseCoverTransportPending = 0;
        _coarseCoverGpuPending = 0;
        _coarseCoverReady = 0;
        _coarseCoverAwaitingRequest = 0;
        foreach (var key in requiredTiles)
        {
            var fallbackMinimumLevel = Math.Min(key.Level, outerBoundaryMinimumLevel);
            if (HasGpuCoverage(key, fallbackMinimumLevel))
            {
                _coarseCoverReady++;
                _remoteCoverageAvailable++;
                if (!_remoteRefreshNeeded.ContainsKey(key))
                    _remoteRequestStates.Remove(key);
                continue;
            }
            if (HasRemoteCoverage(key, fallbackMinimumLevel))
            {
                _coarseCoverGpuPending++;
                _remoteCoverageAvailable++;
                if (!_remoteRefreshNeeded.ContainsKey(key))
                    _remoteRequestStates.Remove(key);
                continue;
            }
            var disposition = CoverageDisposition(key, fallbackMinimumLevel);
            switch (disposition)
            {
                case RemoteTileRequestDisposition.InFlight:
                    _remoteCoverageInFlight++;
                    _coarseCoverTransportPending++;
                    break;
                case RemoteTileRequestDisposition.Pending:
                    _remoteCoveragePending++;
                    _coarseCoverBuilding++;
                    break;
                case RemoteTileRequestDisposition.Missing:
                    _remoteCoverageMissing++;
                    _coarseCoverSourceUnavailable++;
                    break;
                case RemoteTileRequestDisposition.Deferred:
                    _remoteCoverageDeferred++;
                    _coarseCoverTransportPending++;
                    break;
                case null:
                    _coarseCoverAwaitingRequest++;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown remote tile request disposition '{disposition}'.");
            }
        }
        _coarseCoverComplete = requiredTiles.Count != 0 &&
                               _coarseCoverReady == requiredTiles.Count;
        if (requiredTiles.Count != 0 &&
            _remoteCoverageAvailable == requiredTiles.Count &&
            _sourceCompleteMs < 0)
            _sourceCompleteMs = CoarseCoverElapsedMs();
        if (_coarseCoverComplete && _bodiesCompleteMs < 0)
            _bodiesCompleteMs = CoarseCoverElapsedMs();
        _coarseCoverFrontierUnknown = requiredTiles.Count - _coarseCoverReady;
        _coarseCoverRetainingPrevious = _spatialFrame is not null &&
                                         _publishedCoarseCoverGeneration !=
                                         _coarseCoverGeneration;

        RemoteTileRequestDisposition? CoverageDisposition(
            TerrainLodTileKey key,
            int fallbackMinimumLevel)
        {
            if (HasRemoteCoverage(key, fallbackMinimumLevel)) return null;
            var hasState = _remoteRequestStates.TryGetValue(key, out var state);
            if (hasState &&
                (state.Disposition != RemoteTileRequestDisposition.Missing ||
                 key.Level == fallbackMinimumLevel))
                return state.Disposition;
            if (key.Level == fallbackMinimumLevel)
                return null;

            RemoteTileRequestDisposition? aggregate = null;
            for (var index = 0; index < 4; index++)
            {
                var childDisposition = CoverageDisposition(
                    key.Child(index), fallbackMinimumLevel);
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

    private bool HasRemoteCoverage(TerrainLodTileKey root, int fallbackMinimumLevel) =>
        _spatialHierarchy.HasCompleteCoverage(root, fallbackMinimumLevel);

    private bool HasGpuCoverage(TerrainLodTileKey root, int fallbackMinimumLevel) =>
        TerrainLodCoveragePlanner.HasCompleteCoverage(
            root, fallbackMinimumLevel, _spatialPresentations.IsReady);

    private bool CoverageRequestResolved(
        TerrainLodTileKey key,
        int fallbackMinimumLevel)
    {
        if (HasRemoteCoverage(key, fallbackMinimumLevel)) return true;
        if (!_remoteRequestStates.TryGetValue(key, out var state) ||
            state.Disposition != RemoteTileRequestDisposition.Missing)
            return false;
        if (key.Level == fallbackMinimumLevel) return true;
        for (var index = 0; index < 4; index++)
            if (!CoverageRequestResolved(key.Child(index), fallbackMinimumLevel)) return false;
        return true;
    }

    private void UpdateCoarseCoverPlan(TerrainLodTileKey[] requiredTiles)
    {
        var canonical = requiredTiles
            .OrderBy(static key => key.Level)
            .ThenBy(static key => key.X)
            .ThenBy(static key => key.Z)
            .ToArray();
        if (_coarseRequiredTiles.AsSpan().SequenceEqual(canonical)) return;
        _coarseRequiredTiles = canonical;
        _coarseCoverGeneration++;
        _coarseCoverStartedTimestamp = Stopwatch.GetTimestamp();
        _firstCompleteHorizonMs = -1;
        _refinementMs = -1;
        _firstRequestMs = -1;
        _firstSourceTileMs = -1;
        _sourceCompleteMs = -1;
        _firstBodyUploadMs = -1;
        _bodiesCompleteMs = -1;
        _firstSeamUploadMs = -1;
        _seamsCompleteMs = -1;
        _publicationMs = -1;
        _bodyUploads = 0;
        _bodyUploadBytes = 0;
        _bodyInstallMs = 0;
        _seamUploads = 0;
        _seamUploadBytes = 0;
        _seamInstallMs = 0;
        _coarseCoverComplete = false;
        _coarseCoverRetainingPrevious = _spatialFrame is not null;
        _spatialForestCacheKey = null;
        _spatialForestCachedFrame = null;
    }

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
        // Spatial LOD hides partial exact replacement until the near column is complete.
        if (IsAuthoritativeSpatialChunk((chunkX, chunkZ)))
            return new TerrainNearHandoff(true, 0, FadeSeed((chunkX, chunkZ)));
        // A completed spatial handoff is still covered terrain, even after the spatial tile
        // releases ownership. Do not turn the exact mesh's initial fog fade back on then.
        if (_completedSpatialHandoffs.Contains((chunkX, chunkZ)))
            return new TerrainNearHandoff(true, 1, FadeSeed((chunkX, chunkZ)));
        if (!_resident.TryGetValue((chunkX, chunkZ), out var presentation) ||
            !presentation.HasLayer(translucent)) return TerrainNearHandoff.Inactive;
        return new TerrainNearHandoff(
            true,
            presentation.HandoffFor(translucent).Progress,
            FadeSeed((chunkX, chunkZ)));
    }

    internal int ResidentMinimumLevel(int chunkX, int chunkZ) =>
        _resident.TryGetValue((chunkX, chunkZ), out var presentation)
            ? presentation.MinimumLevel : -1;

    /// <summary>Restricted E2E observation of an installed, GPU-ready spatial tile.</summary>
    internal bool HasSpatialPresentation(TerrainLodTileKey key) =>
        _spatialPresentations.IsReady(key);

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
        if ((_tick & 15) == 0)
            _refinementSources.Prune(viewPosition.X, viewPosition.Z);
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

            if (result != TerrainLodAdmissionResult.RejectedStaleRevision)
            {
                // Capture metadata/tint/bounds evidence on this same client-thread turn, before
                // packets can edit/unload the source. Do not copy visuals for rejected work.
                // The conversion worker cannot consume these; only the render thread transfers
                // them to the compiler after completion.
                var visuals = new WorldRegionSnapshot(_world, key.X * 16, 0, key.Z * 16,
                    key.X * 16 + 15, ChuckFormat.WorldHeight - 1, key.Z * 16 + 15);
                try
                {
                    _conversionVisuals.Replace(key, new(new TerrainLodSourceLifetime(chunk), visuals, source));
                }
                catch
                {
                    visuals.Dispose();
                    throw;
                }
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
        _qualityCamera = parameters.ViewPos;
        _qualityNearDistance = parameters.RenderDistance;
        _qualityHorizonDistance = parameters.TerrainHorizonDistance;
        _qualityFov = parameters.VerticalFovDegrees;
        _qualityHeight = parameters.ViewportHeight;
        _qualityDropoff = parameters.TerrainLodDropoffScale;
        var stageStarted = Stopwatch.GetTimestamp();
        EvictSpatialResidency(
            parameters.ViewPos.X / SubChunkRenderer.Size,
            parameters.ViewPos.Z / SubChunkRenderer.Size,
            parameters.TerrainHorizonDistance);
        var uploads = InstallCompleted(parameters.ViewPos,
            parameters.VerticalFovDegrees, parameters.ViewportHeight,
            parameters.TerrainLodDropoffScale,
            parameters.RenderDistance, nearRenderer);
        uploads += InstallSpatialCompleted(nearRenderer);
        uploads += InstallSpatialSeams(nearRenderer);
        EvictDistant(parameters.ViewPos);
        EvaluateSpatialPresentation(parameters, nearRenderer);
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
        var maximumDistance = Math.Max(1, parameters.TerrainHorizonDistance) * 16.0f;
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
                key, distanceSquared, parameters.RenderDistance, nearRenderer,
                RequiresBoundaryCleanHandoff(
                    presentation.HandoffFor(translucent: false).State));
            // LOD already covers this column. Switch atomically once exact is ready; the
            // chunk-load animation belongs at an uncovered streaming edge, not here.
            var handoff = UpdateHandoff(presentation,
                translucent: false, nearPresent, nearReady,
                parameters.DeltaTime, fadeEnabled: false);
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
                var selection = presentation.SelectLayerLevel(requestedLevel, translucent: false);
                if (selection.Available)
                    _solidSeamStates[key] = new TerrainLodSeamColumnState(
                        selection.Level, handoff.Progress, FadeSeed(key), Drawn: false);
                continue;
            }
            var visibleBefore = _visible.Count;
            var presentedLevel = AppendVisibleLevels(
                presentation, key, distanceSquared, requestedLevel,
                translucent: false, handoff.Progress,
                parameters.DeltaTime, parameters.ChunkFade);
            if (presentedLevel >= 0)
            {
                _selectedSolidLevels[key] = presentedLevel;
                _solidSeamStates[key] = new TerrainLodSeamColumnState(
                    presentedLevel, handoff.Progress, FadeSeed(key), Drawn: _visible.Count > visibleBefore);
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
        // Empty compiled layers remain valid selections and boundary evidence, without a draw.
        Profiler.Record("SortAndTrimCpu", Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);

        stageStarted = Stopwatch.GetTimestamp();
        BuildDesiredSeams(
            _solidSeamStates, _desiredSolidSeams, _solidSeamFades);
        uploads += UpdateSeams(device, parameters.ViewPos, SeamUploadsPerFrame,
            translucent: false, _desiredSolidSeams, _solidSeams);
        UpdateCoverageSnapshot(parameters, nearRenderer);
        CollectVisibleSeams(parameters.ViewPos, backToFront: false,
            _desiredSolidSeams, _solidSeams, _solidSeamFades, _visibleSeams);
        // Body coverage is never trimmed to make room for seams. The spatial hierarchy bounds the
        // normal far-field draw count; this legacy path is the correctness fallback while a
        // complete spatial partition is unavailable. Dropping its tail made resident columns
        // blink as camera rotation changed the frustum candidate set.
        var seamDrawBudget = SeamDrawsPerFrame;
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
                _visibleSpatialSolid[i].Fade.Seed,
                _visibleSpatialSolid[i].HiddenColumns);
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
        var maximumDistance = Math.Max(1, parameters.TerrainHorizonDistance) * 16.0f;
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
                key, distanceSquared, parameters.RenderDistance, nearRenderer,
                RequiresBoundaryCleanHandoff(
                    presentation.HandoffFor(translucent: true).State));
            // Match the solid layer: water must not replay the load animation over LOD.
            var handoff = UpdateHandoff(presentation,
                translucent: true, nearPresent, nearReady,
                parameters.DeltaTime, fadeEnabled: false);
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
                var selection = presentation.SelectLayerLevel(requestedLevel, translucent: true);
                if (selection.Available)
                    _translucentSeamStates[key] = new TerrainLodSeamColumnState(
                        selection.Level, handoff.Progress, FadeSeed(key), Drawn: false);
                continue;
            }
            var visibleBefore = _visible.Count;
            var presentedLevel = AppendVisibleLevels(
                presentation, key, distanceSquared, requestedLevel,
                translucent: true, handoff.Progress,
                parameters.DeltaTime, parameters.ChunkFade);
            if (presentedLevel >= 0)
            {
                _selectedTranslucentLevels[key] = presentedLevel;
                _translucentSeamStates[key] = new TerrainLodSeamColumnState(
                    presentedLevel, handoff.Progress, FadeSeed(key), Drawn: _visible.Count > visibleBefore);
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
        // As with solids, an empty layer is selected coverage, not an unavailable level.
        BuildDesiredSeams(
            _translucentSeamStates, _desiredTranslucentSeams, _translucentSeamFades);
        var seamUploads = UpdateSeams(device, parameters.ViewPos, SeamUploadsPerFrame,
            translucent: true, _desiredTranslucentSeams, _translucentSeams);
        CollectVisibleSeams(parameters.ViewPos, backToFront: true,
            _desiredTranslucentSeams, _translucentSeams,
            _translucentSeamFades, _visibleTranslucentSeams);
        var seamDrawBudget = SeamDrawsPerFrame;
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
                    spatial.Fade.Seed,
                    spatial.HiddenColumns)
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
        _conversionVisuals.Dispose();
        _refinementSources.Dispose();
        _meshCompilation.Dispose();
        _spatialMeshCompilation.Dispose();
        _spatialSeamCompilation.Dispose();
        _spatialPublication.Dispose();
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
        _recentRemoteSources.Clear();
        _spatialReplacementColumns.Clear();
        _completedSpatialHandoffs.Clear();
        _spatialColumnMasks.Clear();
        _visibleSpatialSolid.Clear();
        _visibleSpatialTranslucent.Clear();
        _coverageFootprint.Clear();
        _coverageSpatialBodies.Clear();
        _coverageSpatialConflicts.Clear();
        _coverageColumns.Clear();
        _coverageSpatialSeams.Clear();
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
            var currentChunk = _world.BlockHost.HasChunk(key.ChunkX, key.ChunkZ)
                ? _world.BlockHost.GetChunk(key.ChunkX, key.ChunkZ) : null;
            if (compiled.SourceLifetime is { } lifetime && !lifetime.IsCurrent(currentChunk))
            {
                _staleResults++;
                if (currentChunk is not null) ObserveColumn(key.ChunkX, key.ChunkZ);
                continue;
            }
            if (currentChunk is { } chunk)
            {
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

    // CPU-only boundary, also exercised without a GPU by the unload/reload regression tests.
    internal void QueueCompletedConversions(
        Vector3D<double> viewPosition,
        double verticalFovDegrees,
        int viewportHeight,
        float detailDropoffScale)
    {
        while (TryPeekCoverageFirst(out var result) && result is not null)
        {
            var key = (result.ChunkX, result.ChunkZ);
            var chunk = _world.BlockHost.HasChunk(key.ChunkX, key.ChunkZ)
                ? _world.BlockHost.GetChunk(key.ChunkX, key.ChunkZ) : null;
            if (!_conversionVisuals.TryGet(key, out var captured) ||
                captured.Lifetime.Revision != result.TerrainRevision)
            {
                _conversion.AcknowledgeCompleted(
                    result.ChunkX, result.ChunkZ, result.TerrainRevision);
                _staleResults++;
                continue;
            }

            if (!captured.Lifetime.IsCurrent(chunk))
            {
                _conversion.AcknowledgeCompleted(
                    result.ChunkX, result.ChunkZ, result.TerrainRevision);
                _staleResults++;
                _conversionVisuals.Discard(key);
                if (chunk is not null) ObserveColumn(key.ChunkX, key.ChunkZ);
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
            // Keep the capture owned here while learned time/byte admission defers compilation.
            if (!_meshCompilation.CanSubmit(
                    result, minimumLevel, MaximumMeshLevel, workKind)) break;
            var request = new TerrainLodMeshCompilationRequest(
                result,
                minimumLevel,
                MaximumMeshLevel,
                captured.Visuals,
                !_world.Dimension.HasCeiling,
                workKind,
                _world.Dimension.HasCeiling ? null : OverworldCaveCullCeilingY,
                captured.Lifetime);
            // The worker may dispose captured.Visuals immediately after submission. Copy any
            // refinement evidence while this render-thread owner still holds the snapshot.
            if (minimumLevel > ExactVoxelMeshLevel)
                _refinementSources.Retain(key, captured, viewPosition.X, viewPosition.Z);
            else
                _refinementSources.Remove(key);
            if (!_meshCompilation.TrySubmit(request))
            {
                break;
            }
            _conversionVisuals.TransferToCompiler(key);
            if (chunk is null || !chunk.Loaded) _unloadedConversionsPreserved++;

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
        var gpuBytes = SpatialGpuBytes();
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
            _spatialPresentations.TryGetPresentation(pair.Key, out var previous);
            var maximumResultBytes = Math.Min(
                TerrainLodScaleBudget.MaximumUploadBytesPerFrame,
                Math.Max(0, TerrainLodScaleBudget.MaximumSpatialGpuBytes -
                            (gpuBytes - (previous?.EstimatedBytes ?? 0))));
            if (maximumResultBytes == 0)
            {
                _rejectedAdmissions++;
                break;
            }
            var result = _spatialMeshCompilation.Submit(
                pair.Value,
                _world.Content.Blocks,
                _spatialPolicy.VerticalSliceBudgetForSpatialLevel(pair.Key.Level),
                workKind,
                pair.Key.DistanceTo(cameraChunkX, cameraChunkZ),
                _world.Dimension.HasCeiling ? null : OverworldCaveCullCeilingY,
                maximumResultBytes);
            if (result == TerrainLodSpatialMeshAdmissionResult.RejectedAtCapacity) break;
            if (result == TerrainLodSpatialMeshAdmissionResult.RejectedOverBudget)
            {
                _rejectedAdmissions++;
                break;
            }
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
        long uploadedBytes = 0;
        while (installed < SpatialUploadsPerFrame)
        {
            var completed = _deferredSpatialMeshUpload;
            _deferredSpatialMeshUpload = null;
            if (completed is null && !_spatialMeshCompilation.TryTakeCompleted(out completed))
                break;
            if (completed is null) break;
            if (completed.Failure is not null)
                throw new InvalidOperationException(
                    "Spatial terrain LOD mesh compilation failed.", completed.Failure);
            var mesh = completed.Mesh ?? throw new InvalidOperationException(
                "Spatial terrain LOD mesh compilation produced no candidate.");
            if (mesh.EstimatedBytes > TerrainLodScaleBudget.MaximumUploadBytesPerFrame)
            {
                _rejectedAdmissions++;
                continue;
            }
            if (uploadedBytes + mesh.EstimatedBytes >
                TerrainLodScaleBudget.MaximumUploadBytesPerFrame)
            {
                _deferredSpatialMeshUpload = completed;
                break;
            }
            if (!_spatialHierarchy.TryGetCoverage(mesh.Key, out var current, out _) ||
                current is null || current.CanonicalHash != mesh.CanonicalHash)
            {
                _staleResults++;
                continue;
            }

            _spatialPresentations.TryGetPresentation(mesh.Key, out var previous);
            if (previous is not null &&
                string.Equals(previous.CanonicalHash, mesh.CanonicalHash,
                    StringComparison.Ordinal))
                continue;
            if (previous is null &&
                _spatialPresentations.Count >=
                TerrainLodScaleBudget.MaximumSpatialPresentations)
            {
                _rejectedAdmissions++;
                _deferredSpatialMeshUpload = completed;
                break;
            }
            var projectedBytes = SpatialGpuBytes() - (previous?.EstimatedBytes ?? 0) +
                                 mesh.EstimatedBytes;
            // Residency pressure is temporary, not a failed mesh. Keep this bounded candidate
            // for retry after eviction; dropping it loses the only completion for a ready source.
            if (projectedBytes > TerrainLodScaleBudget.MaximumSpatialGpuBytes)
            {
                _rejectedAdmissions++;
                _deferredSpatialMeshUpload = completed;
                break;
            }

            var arenas = nearRenderer.GetOrCreateTerrainGpuArenas(device);
            if (arenas.ProjectedRegionCapacityBytes(
                    TerrainRenderRegionKey.DistantTerrainArena,
                    mesh.ArenaAllocationVertexCounts) >
                TerrainLodScaleBudget.MaximumSpatialGpuBytes)
            {
                _rejectedAdmissions++;
                _deferredSpatialMeshUpload = completed;
                break;
            }

            var installStarted = Stopwatch.GetTimestamp();
            var installedCandidate = _spatialPresentations.TryInstall(
                mesh.Key,
                mesh.CanonicalHash,
                () => TerrainLodSpatialGpuPresentation.Create(
                    device, arenas, mesh),
                out var failure);
            _bodyInstallMs += Stopwatch.GetElapsedTime(installStarted).TotalMilliseconds;
            if (failure is not null)
                throw new InvalidOperationException(
                    $"Spatial terrain LOD upload failed for {mesh.Key}.", failure);
            if (installedCandidate)
            {
                installed++;
                uploadedBytes += mesh.EstimatedBytes;
                _bodyUploads++;
                _bodyUploadBytes += mesh.EstimatedBytes;
                if (_firstBodyUploadMs < 0)
                    _firstBodyUploadMs = CoarseCoverElapsedMs();
            }
        }
        return installed;
    }

    private int InstallSpatialSeams(ChunkRenderer nearRenderer)
    {
        if (WebGpuDevice.Current is not { } device) return 0;
        var installed = 0;
        long uploadedBytes = 0;
        while (installed < SpatialSeamUploadsPerFrame)
        {
            var completed = _deferredSpatialSeamUpload;
            _deferredSpatialSeamUpload = null;
            if (completed is null && !_spatialSeamCompilation.TryTakeCompleted(out completed))
                break;
            if (completed is null) break;
            if (completed.Failure is not null)
                throw new InvalidOperationException(
                    "Spatial terrain LOD seam compilation failed.", completed.Failure);
            var mesh = completed.Mesh ?? throw new InvalidOperationException(
                "Spatial terrain LOD seam compilation produced no candidate.");
            if (mesh.EstimatedBytes > TerrainLodScaleBudget.MaximumUploadBytesPerFrame)
            {
                _rejectedAdmissions++;
                continue;
            }
            if (uploadedBytes + mesh.EstimatedBytes >
                TerrainLodScaleBudget.MaximumUploadBytesPerFrame)
            {
                _deferredSpatialSeamUpload = completed;
                break;
            }
            if (!_desiredSpatialSeams.Contains(mesh.Segment) ||
                !TryResolveSpatialSeamIdentity(
                    mesh.Segment, out _, out _, out var expectedHash) ||
                expectedHash != mesh.CanonicalHash)
            {
                _staleResults++;
                continue;
            }

            var previousBytes = _spatialSeams.TryGetValue(mesh.Segment, out var previousSeam)
                ? previousSeam.EstimatedBytes
                : 0;
            if (SpatialGpuBytes() - previousBytes + mesh.EstimatedBytes >
                TerrainLodScaleBudget.MaximumSpatialGpuBytes)
            {
                _rejectedAdmissions++;
                _deferredSpatialSeamUpload = completed;
                break;
            }

            var arenas = nearRenderer.GetOrCreateTerrainGpuArenas(device);
            if (arenas.ProjectedRegionCapacityBytes(
                    TerrainRenderRegionKey.DistantTerrainArena,
                    mesh.ArenaAllocationVertexCounts) >
                TerrainLodScaleBudget.MaximumSpatialGpuBytes)
            {
                _rejectedAdmissions++;
                _deferredSpatialSeamUpload = completed;
                break;
            }

            TerrainLodSpatialGpuSeamPresentation? candidate = null;
            var installStarted = Stopwatch.GetTimestamp();
            try
            {
                candidate = TerrainLodSpatialGpuSeamPresentation.Create(
                    device, arenas, mesh);
                if (_spatialSeams.Remove(mesh.Segment, out var previous)) previous.Dispose();
                _spatialSeams.Add(mesh.Segment, candidate);
                candidate = null;
                installed++;
                uploadedBytes += mesh.EstimatedBytes;
                _seamUploads++;
                _seamUploadBytes += mesh.EstimatedBytes;
                if (_firstSeamUploadMs < 0)
                    _firstSeamUploadMs = CoarseCoverElapsedMs();
            }
            finally
            {
                candidate?.Dispose();
                _seamInstallMs += Stopwatch.GetElapsedTime(installStarted).TotalMilliseconds;
            }
        }
        return installed;
    }

    private void EvaluateSpatialPresentation(in ChunkRenderParams parameters, ChunkRenderer nearRenderer)
    {
        var stageStarted = Stopwatch.GetTimestamp();
        var cameraChunkX = parameters.ViewPos.X / SubChunkRenderer.Size;
        var cameraChunkZ = parameters.ViewPos.Z / SubChunkRenderer.Size;
        var chunkX = (int)Math.Floor(cameraChunkX);
        var chunkZ = (int)Math.Floor(cameraChunkZ);
        var requestedNearDistance = parameters.RenderDistance;
        _spatialPlanningNearDistance = _spatialPlanningNearDistance < 0 ||
                                       _spatialFrame is not { } previousFrame
            ? requestedNearDistance
            : TerrainLodSpatialNearDistance.Resolve(
                _spatialPlanningNearDistance, requestedNearDistance,
                cameraChunkX, cameraChunkZ,
                previousFrame.Draws.Select(static draw => draw.Selection.Tile),
                nearRenderer.IsMeshColumnReadyForLodHandoff);
        var root = TerrainLodTileKey.ContainingChunk(
            _spatialPolicy.MaximumSpatialLevel, chunkX, chunkZ);
        var selection = TerrainLodSpatialSelector.Select(
            root, cameraChunkX, cameraChunkZ,
            _spatialPolicy, _spatialPresentations.IsReady);
        // Keep the single-root counters for compact diagnostics. A partially loaded maximum-level
        // management root must not hide the fact that a finer GPU partition is already complete
        // and transition-safe.
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
            _spatialPlanningNearDistance,
            parameters.TerrainHorizonDistance,
            parameters.DeltaTime);
        Profiler.Record("SpatialForestCpu",
            Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);
        stageStarted = Stopwatch.GetTimestamp();
        UpdateSpatialSeams(
            frame, cameraChunkX, cameraChunkZ, _spatialPlanningNearDistance, nearRenderer);
        Profiler.Record("SpatialSeamCpu",
            Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);
        EvictSpatialResidency(
            cameraChunkX, cameraChunkZ, parameters.TerrainHorizonDistance);
        stageStarted = Stopwatch.GetTimestamp();
        _spatialSnapshot = new TerrainLodSpatialSnapshot(
            root,
            frame.CompleteCoverage,
            _coarseCoverSelectedTiles,
            _coarseCoverParentFallbacks,
            _coarseCoverMissingGroups,
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
            SpatialGpuBytes(),
            _spatialHierarchy.Snapshot(),
            _spatialMeshCompilation.Snapshot(),
            _spatialSeamCompilation.Snapshot(),
            _spatialPinnedPresentations,
            _spatialGpuEvictions,
            _spatialCpuEvictions);
        Profiler.Record("SpatialSnapshotCpu",
            Stopwatch.GetElapsedTime(stageStarted).TotalMilliseconds);
    }

    private TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation>
        BuildSpatialForestFrame(
            double cameraChunkX,
            double cameraChunkZ,
            int nearDistance,
            int horizonDistance,
            float deltaTime)
    {
        var coverageRootLevel = Math.Max(
            MinimumSpatialGpuLevel,
            _spatialPolicy.DesiredSpatialLevel(horizonDistance));
        var outerBoundaryMinimumLevel =
            TerrainLodCoveragePlanner.RecommendedOuterBoundaryMinimumLevel(
                coverageRootLevel, MinimumSpatialGpuLevel);
        var coveragePlan = TerrainLodCoveragePlanner.PlanRequiredTiles(
            cameraChunkX, cameraChunkZ, nearDistance, horizonDistance,
            coverageRootLevel, MinimumSpatialGpuLevel, outerBoundaryMinimumLevel);
        outerBoundaryMinimumLevel = coveragePlan.EffectiveOuterBoundaryMinimumLevel;
        _coarseOuterBoundaryMinimumLevel = outerBoundaryMinimumLevel;
        var requiredTiles = TerrainLodCoveragePlanner.PrioritizeMissing(
            coveragePlan.Tiles,
            cameraChunkX, cameraChunkZ, _spatialPolicy);
        RetainSpatialMeshCandidates(requiredTiles);
        UpdateCoarseCoverPlan(requiredTiles);
        // Uploads happen earlier in this render pass than presentation evaluation. Reclassify the
        // same immutable partition here so GPU-pending/ready diagnostics describe this frame,
        // rather than the previous simulation tick.
        UpdateRemoteCoverage(requiredTiles, cameraChunkX, cameraChunkZ, horizonDistance,
            outerBoundaryMinimumLevel);
        // Even one newly resident bridge tile can unlock the next contiguous band. Quantizing
        // revisions left the tail of an incomplete horizon cached forever at a stationary camera.
        // Upload admission already bounds new residency per frame; unchanged frames still hit.
        var presentationRevision = _spatialPresentations.Revision;
        var cacheKey = new SpatialForestCacheKey(
            cameraChunkX,
            cameraChunkZ,
            nearDistance,
            horizonDistance,
            _coarseCoverGeneration,
            presentationRevision);
        if (_spatialForestCacheKey == cacheKey &&
            _spatialForestCachedFrame is { } cachedFrame &&
            SpatialForestReferencesCurrentPresentations(cachedFrame))
            return cachedFrame;

        var horizonCover = TerrainLodCoveragePlanner.SelectCompleteCover(
            requiredTiles, cameraChunkX, cameraChunkZ,
            _spatialPolicy, _spatialPresentations.IsReady,
            TerrainLodScaleBudget.MaximumSelectedNodes);
        var cover = horizonCover.CompleteHorizon
            ? horizonCover
            : TerrainLodCoveragePlanner.SelectContiguousAvailableCover(
                _spatialPresentations.ReadyKeys,
                MinimumSpatialGpuLevel,
                cameraChunkX,
                cameraChunkZ,
                Math.Max(1, horizonDistance) + (1 << MinimumSpatialGpuLevel),
                _spatialPolicy,
                _spatialPresentations.IsReady,
                _spatialFrame?.Draws
                    .Select(static draw => draw.Selection.Tile)
                    .ToHashSet(),
                TerrainLodScaleBudget.MaximumSelectedNodes);
        _coarseCoverComplete = horizonCover.CompleteHorizon;
        _coarseCoverSelectedTiles = cover.SelectedNodes;
        _coarseCoverParentFallbacks = cover.ParentFallbacks;
        _coarseCoverMissingGroups = cover.MissingCoverageGroups;
        _coarseCoverReady = requiredTiles.Count(key => HasGpuCoverage(
            key, Math.Min(key.Level, outerBoundaryMinimumLevel)));
        _coarseCoverFrontierUnknown = requiredTiles.Length - _coarseCoverReady;
        _coarseCoverRetainingPrevious = _spatialFrame is not null &&
                                         _publishedCoarseCoverGeneration !=
                                         _coarseCoverGeneration;
        if (!cover.CompleteCoverage)
        {
            var incomplete = new TerrainLodSpatialPresentationFrame<
                TerrainLodSpatialGpuPresentation>([], false, false);
            _spatialForestCacheKey = cacheKey;
            _spatialForestCachedFrame = incomplete;
            return incomplete;
        }

        var candidateSelections = cover.Roots.SelectMany(static root => root.Nodes).ToArray();
        if (!WithinSpatialBodyDrawBudget(candidateSelections))
        {
            var overBudget = new TerrainLodSpatialPresentationFrame<
                TerrainLodSpatialGpuPresentation>([], false, false);
            _spatialForestCacheKey = cacheKey;
            _spatialForestCachedFrame = overBudget;
            return overBudget;
        }
        if (!horizonCover.CompleteHorizon &&
            _spatialFrame is { } previous &&
            _publishedCoarseCoverGeneration != _coarseCoverGeneration &&
            TerrainLodCoveragePlanner.ShouldRetainPreviousCover(
                previous.Draws.Select(static draw => draw.Selection),
                candidateSelections,
                cameraChunkX,
                cameraChunkZ,
                Math.Max(1, horizonDistance) + (1 << MinimumSpatialGpuLevel)))
        {
            _coarseCoverRetainingPrevious = true;
            var retained = new TerrainLodSpatialPresentationFrame<
                TerrainLodSpatialGpuPresentation>([], false, false);
            _spatialForestCacheKey = cacheKey;
            _spatialForestCachedFrame = retained;
            return retained;
        }

        var elapsed = Stopwatch.GetElapsedTime(_coarseCoverStartedTimestamp).TotalMilliseconds;
        if (_firstCompleteHorizonMs < 0 && horizonCover.CompleteHorizon)
            _firstCompleteHorizonMs = elapsed;
        if (_refinementMs < 0 && horizonCover.CompleteHorizon &&
            horizonCover.ParentFallbacks == 0)
            _refinementMs = elapsed;

        List<TerrainLodSpatialPresentationDraw<TerrainLodSpatialGpuPresentation>> draws = [];
        var transitioning = false;
        HashSet<TerrainLodTileKey> activeRoots = [];
        foreach (var root in cover.Roots)
        {
            activeRoots.Add(root.Root);
            // The forest can change spatial level only as a complete parent/child partition. Live
            // fades remain disabled until seam planning can include stable neighboring roots in
            // both transition partitions; the body-plus-seam readiness gate still makes this an
            // atomic replacement; the last complete spatial snapshot remains displayed while the
            // new body/seam partition is being prepared.
            var rootFrame = _spatialPresentations.UpdatePartition(
                root.Root,
                root.Nodes,
                completeRootCoverage: true,
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

    /// <summary>
    ///     Cached cold-cover selection may outlive an individual catalog entry. Replacing an
    ///     unpublished entry releases its owner reference immediately, so a cache hit is valid
    ///     only while every draw still names the exact live presentation object. Published frames
    ///     have independent leases and are deliberately handled by the publication owner instead.
    /// </summary>
    private bool SpatialForestReferencesCurrentPresentations(
        TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation> frame)
    {
        foreach (var draw in frame.Draws)
            if (!_spatialPresentations.TryGetPresentation(
                    draw.Selection.Tile, out var current) ||
                !ReferenceEquals(current, draw.Presentation))
                return false;
        return true;
    }

    /// <summary>
    ///     Cancels body work that cannot participate in the current atomic cover. Ancestors remain
    ///     eligible because they are the conservative fallback while a complete child group is
    ///     being compiled; the currently published frame is retained for the same reason.
    /// </summary>
    private void RetainSpatialMeshCandidates(IReadOnlyList<TerrainLodTileKey> requiredTiles)
    {
        HashSet<TerrainLodTileKey> desired = [];
        foreach (var required in requiredTiles)
        {
            var candidate = required;
            while (true)
            {
                desired.Add(candidate);
                if (candidate.Level >= _spatialPolicy.MaximumSpatialLevel) break;
                candidate = candidate.Parent();
            }
        }
        if (_spatialFrame is { } published)
            foreach (var draw in published.Draws)
                desired.Add(draw.Selection.Tile);

        _spatialMeshCompilation.Retain(desired);
        if (_deferredSpatialMeshUpload?.Mesh is { } deferred && !desired.Contains(deferred.Key))
        {
            _deferredSpatialMeshUpload = null;
            _staleResults++;
        }
        foreach (var key in _spatialMeshPending.Keys
                     .Where(key => !desired.Contains(key)).ToArray())
            _spatialMeshPending.Remove(key);

        // Source residency outlives GPU residency. A returning required tile must be rebuilt
        // from its cached CPU record even when no network or hierarchy publication fires again.
        foreach (var required in requiredTiles)
        {
            if (_spatialPresentations.IsReady(required) ||
                _spatialMeshPending.ContainsKey(required) ||
                _deferredSpatialMeshUpload?.Mesh?.Key == required ||
                !_spatialHierarchy.TryGetCoverage(required, out var tile, out _) || tile is null ||
                _spatialMeshCompilation.Contains(required, tile.CanonicalHash)) continue;
            QueueSpatialMesh(tile);
        }
    }

    private void UpdateSpatialSeams(
        TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation> frame,
        double cameraChunkX,
        double cameraChunkZ,
        int renderDistance,
        ChunkRenderer nearRenderer)
    {
        if (ReferenceEquals(frame, _spatialFrame) && _spatialSubmissionReady)
        {
            RefreshAuthority();
            return;
        }

        // An incomplete candidate is not a presentation. Keep the last complete body-and-seam
        // snapshot (and its leases) while the leading edge is sourced, built, transported, and
        // uploaded. On a cold start the ordinary fog frontier remains visible instead of claiming
        // terrain the client does not own.
        if (!frame.CompleteCoverage)
        {
            _coarseCoverRetainingPrevious = _spatialFrame is not null;
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
        var gpuBytes = SpatialGpuBytes();
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
            var previousBytes = _spatialSeams.TryGetValue(seam, out var previous)
                ? previous.EstimatedBytes
                : 0;
            var maximumResultBytes = Math.Min(
                TerrainLodScaleBudget.MaximumUploadBytesPerFrame,
                Math.Max(0, TerrainLodScaleBudget.MaximumSpatialGpuBytes -
                            (gpuBytes - previousBytes)));
            if (maximumResultBytes == 0)
            {
                _rejectedAdmissions++;
                break;
            }
            if (_spatialSeamCompilation.Submit(
                seam, owner!, neighbor, _world.Content.Blocks,
                    SpatialSeamDistance(seam, cameraChunkX, cameraChunkZ),
                    caveCullBelowY,
                    maximumResultBytes))
                admitted++;
        }

        if (frame.CompleteCoverage && _desiredSpatialSeams.All(SpatialSeamIsCurrent))
        {
            if (_seamsCompleteMs < 0)
                _seamsCompleteMs = CoarseCoverElapsedMs();
            var seams = _desiredSpatialSeams.ToDictionary(
                static key => key,
                key => new PublishedTerrainSeam<TerrainLodSpatialGpuSeamPresentation>(
                    _spatialSeams[key], _spatialSeamFades.GetValueOrDefault(
                        key, new TerrainLodSpatialPresentationFade(1, 0, 0))));
            if (WithinSpatialPublishedDrawBudget(frame, seams) &&
                _spatialPublication.TryPublish(frame, seams, seamsReady: true))
            {
                _publishedCoarseCoverGeneration = _coarseCoverGeneration;
                _coarseCoverRetainingPrevious = false;
                if (_publicationMs < 0)
                    _publicationMs = CoarseCoverElapsedMs();
                if (_coldCoverMs < 0)
                    _coldCoverMs = Stopwatch.GetElapsedTime(
                        _coarseCoverStartedTimestamp).TotalMilliseconds;
            }
        }
        RefreshAuthority();

        void RefreshAuthority()
        {
            // Keep the handoff memory bounded to the current detailed radius.
            _completedSpatialHandoffs.RemoveWhere(column =>
            {
                var dx = column.X + 0.5 - cameraChunkX;
                var dz = column.Z + 0.5 - cameraChunkZ;
                return dx * dx + dz * dz >= (double)renderDistance * renderDistance;
            });
            _authoritativeSpatialTiles.Clear();
            _spatialReplacementColumns.Clear();
            _spatialColumnMasks.Clear();
            if (_spatialFrame is not { } published) return;
            foreach (var draw in published.Draws)
            {
                if (TerrainLodSpatialAuthority.ShouldPresent(
                        draw.Selection.Tile, cameraChunkX, cameraChunkZ, renderDistance,
                        ReplacementReady))
                {
                    _authoritativeSpatialTiles.Add(draw.Selection.Tile);
                    var tile = draw.Selection.Tile;
                    var distant = TerrainLodSpatialAuthority.IsBeyondNearRadius(
                        tile, cameraChunkX, cameraChunkZ, renderDistance);
                    if (distant) continue;

                    // The whole published tile remains owned, with bounded exceptions for ready
                    // replacements. Recording only owned columns inside this window hides the
                    // rest of a mixed tile beyond the window, leaving holes at the guard edge.
                    var handoffRadius = Math.Max(0, renderDistance) + 1;
                    var minX = Math.Max(tile.MinChunkX,
                        (long)Math.Floor(cameraChunkX - handoffRadius));
                    var maxX = Math.Min(tile.MaxChunkX,
                        (long)Math.Ceiling(cameraChunkX + handoffRadius));
                    var minZ = Math.Max(tile.MinChunkZ,
                        (long)Math.Floor(cameraChunkZ - handoffRadius));
                    var maxZ = Math.Min(tile.MaxChunkZ,
                        (long)Math.Ceiling(cameraChunkZ + handoffRadius));
                    for (var z = minZ; z <= maxZ; z++)
                    for (var x = minX; x <= maxX; x++)
                        if (ReplacementReady((int)x, (int)z))
                            _spatialReplacementColumns.Add(((int)x, (int)z));
                        else
                            _completedSpatialHandoffs.Remove(((int)x, (int)z));
                }
            }
        }

        bool ReplacementReady(int x, int z)
        {
            // A cached legacy LOD column is not necessarily submitted this frame. Masking the
            // spatial page merely because it exists can expose an empty exact section during a
            // detail-radius increase. Only a complete exact column may take spatial ownership.
            var dx = x + 0.5 - cameraChunkX;
            var dz = z + 0.5 - cameraChunkZ;
            var ready = dx * dx + dz * dz < (double)renderDistance * renderDistance &&
                (_completedSpatialHandoffs.Contains((x, z))
                    ? nearRenderer.IsMeshColumnReady(x, z)
                    : nearRenderer.IsMeshColumnReadyForLodHandoff(x, z));
            if (ready) _completedSpatialHandoffs.Add((x, z));
            else _completedSpatialHandoffs.Remove((x, z));
            return ready;
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

    private bool WithinSpatialBodyDrawBudget(
        IEnumerable<TerrainLodTileSelection> selections)
    {
        var solid = 0;
        var translucent = 0;
        foreach (var selection in selections)
        {
            if (!_spatialPresentations.TryGetPresentation(
                    selection.Tile, out var presentation) || presentation is null)
                return false;
            solid += presentation.Pages.Count(static page => page.Solid is not null);
            translucent += presentation.Pages.Count(static page => page.Translucent is not null);
            if (solid > TerrainLodScaleBudget.MaximumDrawPagesPerLayer ||
                translucent > TerrainLodScaleBudget.MaximumDrawPagesPerLayer)
                return false;
        }
        return true;
    }

    private static bool WithinSpatialPublishedDrawBudget(
        TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation> frame,
        IReadOnlyDictionary<TerrainLodSpatialSeamSegment,
            PublishedTerrainSeam<TerrainLodSpatialGpuSeamPresentation>> seams)
    {
        var solid = frame.Draws.Sum(static draw =>
            draw.Presentation.Pages.Count(static page => page.Solid is not null));
        var translucent = frame.Draws.Sum(static draw =>
            draw.Presentation.Pages.Count(static page => page.Translucent is not null));
        foreach (var seam in seams.Values)
        {
            solid += seam.Presentation.Pages.Count(static page => page.Solid is not null);
            translucent += seam.Presentation.Pages.Count(
                static page => page.Translucent is not null);
        }
        return solid <= TerrainLodScaleBudget.MaximumDrawPagesPerLayer &&
               translucent <= TerrainLodScaleBudget.MaximumDrawPagesPerLayer;
    }

    private long SpatialGpuBytes() =>
        _spatialPresentations.ReadyPresentations
            .Concat(_spatialFrame?.Draws.Select(static draw => draw.Presentation) ?? [])
            .Distinct().Sum(static presentation => presentation.EstimatedBytes) +
        _spatialSeams.Values
            .Concat(_spatialPublication.Seams.Values.Select(static seam => seam.Presentation))
            .Distinct().Sum(static seam => seam.EstimatedBytes);

    /// <summary>
    ///     Keeps the active atomic snapshot, its bounded replacement, and a small leading edge in
    ///     memory. Everything else is trailing cache state and may be reconstructed from the
    ///     durable column-tile cache without deleting that cache entry.
    /// </summary>
    private void EvictSpatialResidency(
        double cameraChunkX,
        double cameraChunkZ,
        int horizonDistanceChunks)
    {
        if (horizonDistanceChunks <= 0) return;
        var cameraChunkFloorX = (int)Math.Floor(cameraChunkX);
        var cameraChunkFloorZ = (int)Math.Floor(cameraChunkZ);
        var cpuTarget = TerrainLodScaleBudget.ClientHierarchyTiles -
                        TerrainLodScaleBudget.ClientHierarchyTileReserve;
        var unchanged = cameraChunkFloorX == _lastSpatialResidencyChunkX &&
                        cameraChunkFloorZ == _lastSpatialResidencyChunkZ &&
                        horizonDistanceChunks == _lastSpatialResidencyHorizon &&
                        _spatialPresentations.Revision == _lastSpatialResidencyRevision &&
                        ReferenceEquals(_spatialFrame, _lastSpatialResidencyFrame);
        if (unchanged &&
            _spatialPresentations.Count <= TerrainLodScaleBudget.TargetSpatialPresentations &&
            _lastSpatialResidencyGpuBytes <= TerrainLodScaleBudget.TargetSpatialGpuBytes &&
            _lastSpatialResidencyCpuTiles <= cpuTarget)
            return;

        var gpuBytesBefore = SpatialGpuBytes();
        var retentionMargin = Math.Clamp(horizonDistanceChunks / 8.0, 16, 64);
        var retentionDistance = horizonDistanceChunks + retentionMargin;
        var readyKeys = _spatialPresentations.ReadyKeys.ToArray();
        var hierarchyKeys = _spatialHierarchy.ResidentKeys();

        // Publication revisions are frequent while a horizon is filling. Most of them do not
        // create trailing residency, so do not rebuild the replacement tree or sort candidates
        // merely to discover that there is nothing to evict. This keeps ordinary publication
        // proportional to the resident-key scan and reserves the more expensive pinning pass for
        // actual movement or pressure.
        if (readyKeys.Length <= TerrainLodScaleBudget.TargetSpatialPresentations &&
            gpuBytesBefore <= TerrainLodScaleBudget.TargetSpatialGpuBytes &&
            hierarchyKeys.Length <= cpuTarget &&
            readyKeys.All(key =>
                key.DistanceTo(cameraChunkX, cameraChunkZ) <= retentionDistance) &&
            hierarchyKeys.All(key =>
                key.DistanceTo(cameraChunkX, cameraChunkZ) <= retentionDistance))
        {
            HashSet<TerrainLodTileKey> activePins = [];
            AddFramePins(_spatialFrame, activePins);
            AddFramePins(_spatialForestCachedFrame, activePins);
            activePins.UnionWith(_spatialPresentations.TransitionTiles);
            _spatialPinnedPresentations = activePins.Count(_spatialPresentations.IsReady);
            RememberResidency(gpuBytesBefore, hierarchyKeys.Length);
            return;
        }

        HashSet<TerrainLodTileKey> pinned = [];
        PinFrame(_spatialFrame);
        PinFrame(_spatialForestCachedFrame);
        pinned.UnionWith(_spatialPresentations.TransitionTiles);

        // A replacement may not yet form a complete frame. Pin its complete ready groups directly
        // from the required coverage roots so movement cannot continuously evict its leading edge.
        var replacementPins = 0;
        foreach (var root in _coarseRequiredTiles) PinReplacement(root);

        foreach (var key in _spatialMeshPending.Keys)
            if (_spatialPresentations.IsReady(key)) pinned.Add(key);
        var optionalCache = _spatialPresentations.ReadyKeys
                     .Where(key => !pinned.Contains(key))
                     .OrderBy(key => key.DistanceTo(cameraChunkX, cameraChunkZ))
                     .ThenByDescending(static key => key.Level)
                     .ThenBy(static key => key.X)
                     .ThenBy(static key => key.Z)
                     .Select(key => (Key: key, Bytes:
                         _spatialPresentations.TryGetPresentation(key, out var value)
                             ? value!.EstimatedBytes : 0))
                     .ToArray();
        var reservedBytes = gpuBytesBefore - optionalCache.Sum(static entry => entry.Bytes);
        foreach (var key in TerrainLodSpatialResidencyPolicy.SelectLeadingEdge(
                     optionalCache, reservedBytes))
            pinned.Add(key);
        _spatialPinnedPresentations = pinned.Count(_spatialPresentations.IsReady);

        var gpuBytes = gpuBytesBefore;
        foreach (var candidate in readyKeys
                     .Where(key => !pinned.Contains(key))
                     .Select(key => new
                     {
                         Key = key,
                         Distance = key.DistanceTo(cameraChunkX, cameraChunkZ),
                         Presentation = _spatialPresentations.TryGetPresentation(
                             key, out var value) ? value : null
                     })
                     .Where(static candidate => candidate.Presentation is not null)
                     .OrderByDescending(candidate => candidate.Distance > retentionDistance)
                     .ThenByDescending(static candidate => candidate.Distance)
                     .ThenBy(static candidate => candidate.Key.Level)
                     .ThenBy(static candidate => candidate.Key.X)
                     .ThenBy(static candidate => candidate.Key.Z)
                     .ToArray())
        {
            var outsideRetention = candidate.Distance > retentionDistance;
            var underPressure =
                _spatialPresentations.Count > TerrainLodScaleBudget.TargetSpatialPresentations ||
                gpuBytes > TerrainLodScaleBudget.TargetSpatialGpuBytes;
            if (!outsideRetention && !underPressure) break;
            if (!_spatialPresentations.TryEvict(candidate.Key)) continue;
            gpuBytes -= candidate.Presentation!.EstimatedBytes;
            _spatialGpuEvictions++;
        }

        // GPU entries, active seams, pending compilation inputs, and the required replacement
        // roots keep their CPU records. Remaining distant hierarchy nodes are memory cache only.
        HashSet<TerrainLodTileKey> cpuPinned = [.. pinned];
        cpuPinned.UnionWith(_spatialPresentations.ReadyKeys);
        cpuPinned.UnionWith(_coarseRequiredTiles);
        cpuPinned.UnionWith(_spatialMeshPending.Keys);
        if (_deferredSpatialMeshUpload?.Mesh is { } deferredMesh)
            cpuPinned.Add(deferredMesh.Key);
        foreach (var seam in _spatialPublication.Seams.Keys.Concat(_spatialSeams.Keys))
        {
            cpuPinned.Add(seam.Owner.Tile);
            if (seam.Neighbor is { } neighbor) cpuPinned.Add(neighbor.Tile);
        }
        if (_deferredSpatialSeamUpload?.Mesh is { } deferredSeam)
        {
            cpuPinned.Add(deferredSeam.Segment.Owner.Tile);
            if (deferredSeam.Segment.Neighbor is { } neighbor)
                cpuPinned.Add(neighbor.Tile);
        }

        var cpuCount = hierarchyKeys.Length;
        foreach (var candidate in hierarchyKeys
                     .Where(key => !cpuPinned.Contains(key))
                     .Select(key => new
                     {
                         Key = key,
                         Distance = key.DistanceTo(cameraChunkX, cameraChunkZ)
                     })
                     .OrderByDescending(candidate => candidate.Distance > retentionDistance)
                     .ThenByDescending(static candidate => candidate.Distance)
                     .ThenBy(static candidate => candidate.Key.Level)
                     .ThenBy(static candidate => candidate.Key.X)
                     .ThenBy(static candidate => candidate.Key.Z))
        {
            var outsideRetention = candidate.Distance > retentionDistance;
            if (!outsideRetention && cpuCount <= cpuTarget) break;
            if (!_spatialHierarchy.EvictResident(candidate.Key)) continue;
            cpuCount--;
            _spatialCpuEvictions++;
        }

        foreach (var key in _remoteRefreshNeeded.Keys
                     .Where(key => key.DistanceTo(cameraChunkX, cameraChunkZ) >
                                   retentionDistance &&
                                   !_spatialHierarchy.TryGetCoverage(key, out _, out _) &&
                                   !_spatialPresentations.IsReady(key) &&
                                   !_remoteRequestStates.ContainsKey(key)).ToArray())
            _remoteRefreshNeeded.Remove(key);

        // Revision tokens belong to resident/pending presentations, not the entire route a
        // player has ever flown. Re-entering an evicted area starts a fresh source request.
        var retainedVersions = _spatialHierarchy.ResidentKeys().ToHashSet();
        retainedVersions.UnionWith(_spatialPresentations.ReadyKeys);
        retainedVersions.UnionWith(_remoteRefreshNeeded.Keys);
        retainedVersions.UnionWith(_remoteRequestStates.Keys);
        foreach (var key in _remoteTileGenerations.Keys
                     .Where(key => !retainedVersions.Contains(key)).ToArray())
            _remoteTileGenerations.Remove(key);

        RememberResidency(gpuBytes, cpuCount);

        return;

        void PinFrame(
            TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation>? frame)
        {
            if (frame is null) return;
            foreach (var draw in frame.Draws) pinned.Add(draw.Selection.Tile);
        }

        static void AddFramePins(
            TerrainLodSpatialPresentationFrame<TerrainLodSpatialGpuPresentation>? frame,
            HashSet<TerrainLodTileKey> destination)
        {
            if (frame is null) return;
            foreach (var draw in frame.Draws) destination.Add(draw.Selection.Tile);
        }

        void RememberResidency(long gpuBytes, int cpuTiles)
        {
            _lastSpatialResidencyChunkX = cameraChunkFloorX;
            _lastSpatialResidencyChunkZ = cameraChunkFloorZ;
            _lastSpatialResidencyHorizon = horizonDistanceChunks;
            _lastSpatialResidencyRevision = _spatialPresentations.Revision;
            _lastSpatialResidencyFrame = _spatialFrame;
            _lastSpatialResidencyGpuBytes = gpuBytes;
            _lastSpatialResidencyCpuTiles = cpuTiles;
        }

        void PinReplacement(TerrainLodTileKey root)
        {
            if (replacementPins >= TerrainLodScaleBudget.MaximumSelectedNodes) return;
            var selection = TerrainLodSpatialSelector.Select(
                root, cameraChunkX, cameraChunkZ,
                _spatialPolicy, _spatialPresentations.IsReady);
            if (selection.CompleteCoverage)
            {
                if (replacementPins + selection.Nodes.Count >
                    TerrainLodScaleBudget.MaximumSelectedNodes)
                    return;
                foreach (var node in selection.Nodes) pinned.Add(node.Tile);
                replacementPins += selection.Nodes.Count;
                return;
            }
            if (root.Level <= _coarseOuterBoundaryMinimumLevel) return;
            for (var index = 0; index < 4; index++) PinReplacement(root.Child(index));
        }
    }

    private bool TryResolveSpatialSeamIdentity(
        TerrainLodSpatialSeamSegment seam,
        out TerrainLodColumnTile? owner,
        out TerrainLodColumnTile? neighbor,
        out string canonicalHash)
    {
        canonicalHash = string.Empty;
        if (!TryResolveSpatialSeamTiles(seam, out owner, out neighbor) || owner is null) return false;
        int? caveCullBelowY = _world.Dimension.HasCeiling
            ? null
            : OverworldCaveCullCeilingY;
        if (_spatialSeamHashes.TryGetValue(seam, out var cached) &&
            string.Equals(cached.OwnerCanonicalHash, owner.CanonicalHash,
                StringComparison.Ordinal) &&
            string.Equals(cached.NeighborCanonicalHash, neighbor?.CanonicalHash,
                StringComparison.Ordinal) &&
            cached.CaveCullBelowY == caveCullBelowY)
        {
            canonicalHash = cached.CanonicalHash;
            return true;
        }

        canonicalHash = TerrainLodSpatialSeamMeshBuilder.ComputeCanonicalHash(
            seam, owner, neighbor, caveCullBelowY);
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
        var maximumDistance = Math.Max(1, parameters.TerrainHorizonDistance) * 16.0f;
        var camera = parameters.Camera;
        var viewPosition = parameters.ViewPos;

        foreach (var draw in frame.Draws)
        {
            if (!_authoritativeSpatialTiles.Contains(draw.Selection.Tile)) continue;
            foreach (var page in draw.Presentation.Pages)
                AddPage(page, draw.Fade);
        }
        foreach (var (seam, published) in _spatialPublication.Seams)
        {
            if (!SpatialSeamTouchesAuthority(seam)) continue;
            foreach (var page in published.Presentation.Pages) AddPage(page, published.Fade);
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
                page.Maximum.X, page.Maximum.Y, page.Maximum.Z);
            if (!camera.IsBoundingBoxInFrustum(box)) return;
            var dx = Math.Max(0, Math.Max(
                page.Origin.X - viewPosition.X,
                viewPosition.X - page.Maximum.X));
            var dz = Math.Max(0, Math.Max(
                page.Origin.Z - viewPosition.Z,
                viewPosition.Z - page.Maximum.Z));
            var distanceSquared = dx * dx + dz * dz;
            if (distanceSquared > maximumDistance * maximumDistance) return;
            // Reuse across vertical pages, seams and both layers; coverage is horizontal and
            // changes only when the frame's authority decision is refreshed.
            ulong hiddenColumns = 0;
            if (page.Extent.X == TerrainLodSpatialMeshBuilder.PageSize &&
                page.Extent.Z == TerrainLodSpatialMeshBuilder.PageSize)
            {
                var maskKey = (X: page.Origin.X >> 4, Z: page.Origin.Z >> 4);
                if (!_spatialColumnMasks.TryGetValue(maskKey, out hiddenColumns))
                {
                    hiddenColumns = TerrainLodSpatialAuthority.HiddenColumnMask(
                        maskKey.X, maskKey.Z,
                        (x, z) => IsAuthoritativeSpatialChunk((x, z)));
                    _spatialColumnMasks.Add(maskKey, hiddenColumns);
                }
            }
            if (hiddenColumns == TerrainLodSpatialAuthority.AllColumnsHidden) return;
            destination.Add(new VisibleSpatialPage(page, fade, distanceSquared, hiddenColumns));
        }
    }

    private bool IsAuthoritativeSpatialChunk((int X, int Z) key) =>
        TerrainLodSpatialAuthority.OwnsColumn(key.X, key.Z, _authoritativeSpatialTiles,
            _spatialReplacementColumns, MinimumSpatialGpuLevel, _spatialPolicy.MaximumSpatialLevel);

    /// <summary>
    /// Builds a layer-independent ownership report at simulation-tick frequency. Per-column
    /// validation is bounded to the exact/LOD handoff; far spatial coverage is validated through
    /// its atomic tile-and-seam topology rather than expanded into every covered chunk.
    /// </summary>
    private void UpdateCoverageSnapshot(
        in ChunkRenderParams parameters,
        ChunkRenderer nearRenderer)
    {
        if (_lastCoverageTick == _tick) return;
        _lastCoverageTick = _tick;
        _coverageFootprint.Clear();
        _coverageSpatialBodies.Clear();
        _coverageSpatialConflicts.Clear();
        _coverageColumns.Clear();
        _coverageSpatialSeams.Clear();

        var cameraChunkX = parameters.ViewPos.X / SubChunkRenderer.Size;
        var cameraChunkZ = parameters.ViewPos.Z / SubChunkRenderer.Size;
        var detail = Math.Max(0, parameters.RenderDistance);
        var auditRadius = detail + 2;
        var auditRadiusSquared = (double)auditRadius * auditRadius;
        TerrainPresentationCoverageOracle.AddExactRadiusColumns(
            _coverageFootprint, cameraChunkX, cameraChunkZ, detail);

        foreach (var key in _resident.Keys)
            if (ColumnDistanceSquared(key.X, key.Z) <= auditRadiusSquared)
                _coverageFootprint.Add(key);

        if (_spatialFrame is { } published)
        {
            foreach (var draw in published.Draws)
            {
                var tile = draw.Selection.Tile;
                var spatialMinX = Math.Max(tile.MinChunkX,
                    (long)Math.Floor(cameraChunkX - auditRadius));
                var spatialMaxX = Math.Min(tile.MaxChunkX,
                    (long)Math.Ceiling(cameraChunkX + auditRadius));
                var spatialMinZ = Math.Max(tile.MinChunkZ,
                    (long)Math.Floor(cameraChunkZ - auditRadius));
                var spatialMaxZ = Math.Min(tile.MaxChunkZ,
                    (long)Math.Ceiling(cameraChunkZ + auditRadius));
                for (var z = spatialMinZ; z <= spatialMaxZ; z++)
                for (var x = spatialMinX; x <= spatialMaxX; x++)
                {
                    var column = (X: checked((int)x), Z: checked((int)z));
                    if (ColumnDistanceSquared(column.X, column.Z) > auditRadiusSquared) continue;
                    _coverageFootprint.Add(column);
                    var body = (column, draw.Fade.Mode);
                    var count = _coverageSpatialBodies.GetValueOrDefault(body) + 1;
                    _coverageSpatialBodies[body] = count;
                    if (count > 1) _coverageSpatialConflicts.Add(column);
                }
            }

            foreach (var partition in published.Draws.GroupBy(static draw => draw.Fade.Mode))
            foreach (var seam in TerrainLodSpatialSeamPlanner.Plan(
                         partition.Select(static draw => draw.Selection)))
                _coverageSpatialSeams.Add(seam);
        }

        foreach (var key in _coverageFootprint.OrderBy(static key => key.X)
                     .ThenBy(static key => key.Z))
        {
            if (!parameters.Camera.IsBoundingBoxInFrustum(new Box(
                    key.X * SubChunkRenderer.Size, 0, key.Z * SubChunkRenderer.Size,
                    (key.X + 1) * SubChunkRenderer.Size, ChuckFormat.WorldHeight,
                    (key.Z + 1) * SubChunkRenderer.Size)))
                continue;
            var chunkLoaded = _world.BlockHost.HasChunk(key.X, key.Z) &&
                              _world.BlockHost.GetChunk(key.X, key.Z).Loaded;
            var exactPresent = chunkLoaded && nearRenderer.IsMeshColumnResident(key.X, key.Z);
            var hasColumnLod = _resident.TryGetValue(key, out var columnLod);
            var handoff = hasColumnLod
                ? Math.Min(
                    columnLod!.HandoffFor(translucent: false).Progress,
                    columnLod.HandoffFor(translucent: true).Progress)
                : 0;
            _coverageColumns.Add(new TerrainCoverageColumn(
                key.X, key.Z, exactPresent, hasColumnLod, handoff,
                IsAuthoritativeSpatialChunk(key), _coverageSpatialConflicts.Contains(key),
                chunkLoaded));
        }

        var expectedColumnSeams =
            _desiredSolidSeams.Count(SeamRequiresGeometry) +
            _desiredTranslucentSeams.Count(SeamRequiresGeometry);
        var missingColumnSeams = 0;
        var pendingColumnSeams = 0;
        (int X, int Z)? firstMissingColumnSeam = null;
        CountMissing(_desiredSolidSeams, _solidSeams);
        CountMissing(_desiredTranslucentSeams, _translucentSeams);
        _coverageSnapshot = TerrainPresentationCoverageOracle.Evaluate(
            _coverageColumns,
            expectedColumnSeams,
            missingColumnSeams,
            pendingColumnSeams,
            _coverageSpatialSeams,
            _spatialPublication.Seams.Keys,
            firstMissingColumnSeam);
        return;

        double ColumnDistanceSquared(int x, int z)
        {
            var dx = x + 0.5 - cameraChunkX;
            var dz = z + 0.5 - cameraChunkZ;
            return dx * dx + dz * dz;
        }

        void CountMissing(
            IReadOnlyDictionary<TerrainLodSeamKey, TerrainLodSeamSelection> desired,
            IReadOnlyDictionary<TerrainLodSeamKey, GpuSeam> resident)
        {
            foreach (var group in desired.GroupBy(
                         static pair => (pair.Key.Owner, pair.Key.BoundarySide)))
            {
                var desiredParts = group.Where(SeamRequiresGeometry).ToArray();
                if (desiredParts.Length == 0) continue;
                var desiredReady = desiredParts.All(pair =>
                    resident.TryGetValue(pair.Key, out var seam) &&
                    seam.Selection == pair.Value);
                if (desiredReady) continue;
                pendingColumnSeams += desiredParts.Count(pair =>
                    !resident.TryGetValue(pair.Key, out var seam) ||
                    seam.Selection != pair.Value);
                // CollectVisibleSeams deliberately retains the previous complete edge until every
                // part of a split replacement is installed. That is valid displayed coverage, not
                // a hole; the pending counter still exposes the unfinished replacement.
                var hasFallback = resident.Any(pair =>
                    pair.Key.Owner == group.Key.Owner &&
                    pair.Key.BoundarySide == group.Key.BoundarySide);
                if (hasFallback) continue;
                missingColumnSeams += desiredParts.Length;
                firstMissingColumnSeam ??= group.Key.Owner;
            }
        }

    }

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
        if (!_world.BlockHost.HasChunk(key.X, key.Z))
        {
            RequestRetainedDetail(key, requestedLevel);
            return;
        }
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

    private void RequestRetainedDetail((int X, int Z) key, int requestedLevel)
    {
        if (!_refinementSources.TryGet(key, out var retained)) return;
        if (!_resident.TryGetValue(key, out var presentation) ||
            presentation.TerrainRevision != retained.Terrain.TerrainRevision ||
            !retained.Lifetime.IsCurrent(null))
        {
            _refinementSources.Remove(key);
            return;
        }

        var admission = _conversion.Submit(retained.Terrain);
        if (admission == TerrainLodAdmissionResult.RejectedAtCapacity)
        {
            _rejectedAdmissions++;
            return;
        }
        if (admission == TerrainLodAdmissionResult.RejectedStaleRevision) return;

        var visuals = retained.Visuals.Clone();
        try
        {
            _conversionVisuals.Replace(key, new(retained.Lifetime, visuals, retained.Terrain));
        }
        catch
        {
            visuals.Dispose();
            throw;
        }
        _detailLevelRequests[key] = requestedLevel;
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

        var groups = desired
            .GroupBy(static pair => (pair.Key.Owner, pair.Key.BoundarySide))
            .Select(group =>
            {
                var parts = group.ToArray();
                var missing = parts.Count(pair =>
                    !installed.TryGetValue(pair.Key, out var current) ||
                    current.Selection != pair.Value);
                var hasFallback = installed.Keys.Any(key =>
                    key.Owner == group.Key.Owner &&
                    key.BoundarySide == group.Key.BoundarySide);
                var critical = !hasFallback && parts.Any(SeamRequiresGeometry);
                return new SeamUploadGroup(group.Key, parts, missing, critical);
            })
            .ToArray();
        // A newly visible level-changing edge without an older complete artifact is coverage work,
        // not refinement. Admit every such group atomically this frame; ordinary replacements keep
        // the small per-frame budget because CollectVisibleSeams retains their predecessor.
        var effectiveBudget = Math.Max(uploadBudget,
            groups.Where(static group => group.Critical)
                .Sum(static group => group.Missing));
        var uploads = 0;
        foreach (var uploadGroup in groups
                     .OrderByDescending(static group => group.Critical)
                     .ThenBy(group => DistanceSquared(group.Key.Owner, viewPosition))
                     .ThenBy(static group => group.Key.Owner.X)
                     .ThenBy(static group => group.Key.Owner.Z)
                     .ThenBy(static group => group.Key.BoundarySide))
        {
            var missing = uploadGroup.Missing;
            if (missing == 0) continue;
            // Owner/neighbor split artifacts are one presentation replacement. Never publish only
            // half because the per-frame seam budget happened to end between them.
            if (uploads + missing > effectiveBudget) continue;
            foreach (var (key, selection) in uploadGroup.Parts
                         .OrderBy(static pair => pair.Key.MaterialSide))
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

    private static bool SeamRequiresGeometry(
        KeyValuePair<TerrainLodSeamKey, TerrainLodSeamSelection> pair) =>
        pair.Value.OwnerLevel != pair.Value.NeighborLevel ||
        pair.Key.MaterialSide != TerrainLodSeamMaterialSide.Either;

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
        var clientWorld = _world as ClientWorld;
        var terrainNetwork = clientWorld?.NetworkHandler;
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
            clientWorld?.TerrainLodTilesEnqueued ?? 0,
            clientWorld?.TerrainLodTilesDequeued ?? 0,
            clientWorld?.TerrainLodTileQueueDepth ?? 0,
            clientWorld?.TerrainLodTileQueuePeak ?? 0,
            terrainNetwork?.TerrainLodTransportQueueDepth ?? 0,
            terrainNetwork?.TerrainLodTransportQueuePeak ?? 0,
            _remotePendingResponses,
            _remoteMissingResponses,
            _remoteDeferredResponses,
            _remoteCoverageRequired,
            _remoteCoverageAvailable,
            _remoteCoverageInFlight,
            _remoteCoveragePending,
            _remoteCoverageMissing,
            _remoteCoverageDeferred,
            _coarseCoverSourceUnavailable,
            _coarseCoverBuilding,
            _coarseCoverTransportPending,
            _coarseCoverGpuPending,
            _coarseCoverReady,
            _coarseCoverAwaitingRequest,
            _coarseCoverFrontierUnknown,
            _coarseCoverComplete,
            _coarseCoverRetainingPrevious,
            _coldCoverMs,
            _firstCompleteHorizonMs,
            _refinementMs,
            new TerrainLodConvergenceSnapshot(
                _coarseCoverGeneration,
                _firstRequestMs,
                _firstSourceTileMs,
                _sourceCompleteMs,
                _firstBodyUploadMs,
                _bodiesCompleteMs,
                _firstSeamUploadMs,
                _seamsCompleteMs,
                _publicationMs,
                _bodyUploads,
                _bodyUploadBytes,
                _bodyInstallMs,
                _seamUploads,
                _seamUploadBytes,
                _seamInstallMs));
    }

    private double CoarseCoverElapsedMs() => _coarseCoverStartedTimestamp == 0
        ? -1
        : Stopwatch.GetElapsedTime(_coarseCoverStartedTimestamp).TotalMilliseconds;

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
        ChunkRenderer nearRenderer,
        bool requireBoundaryClean = true)
    {
        var nearDistance = Math.Max(0, renderDistance) * (double)SubChunkRenderer.Size;
        var nearPresent = distanceSquared < nearDistance * nearDistance &&
                          _world.BlockHost.HasChunk(key.X, key.Z) &&
                          _world.BlockHost.GetChunk(key.X, key.Z).Loaded;
        return (nearPresent,
            nearPresent && (requireBoundaryClean
                ? nearRenderer.IsMeshColumnReadyForLodHandoff(key.X, key.Z)
                : nearRenderer.IsMeshColumnReady(key.X, key.Z)));
    }

    /// <summary>
    ///     A streaming-boundary rebuild gates only the initial transfer of authority. Once an exact
    ///     column owns presentation, ordinary neighbor arrivals and remeshes keep its previous mesh
    ///     visible atomically; restoring LOD for each maintenance request causes visible blinking.
    ///     A genuinely missing exact section still fails <see cref="ChunkRenderer.IsMeshColumnReady" />
    ///     and reverses to LOD coverage.
    /// </summary>
    internal static bool RequiresBoundaryCleanHandoff(TerrainLodHandoffState state) =>
        state != TerrainLodHandoffState.NearOnly;

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
        var selection = presentation.SelectLayerLevel(requestedLevel, translucent);
        if (!selection.Available) return -1;

        // The exact/LOD handoff owns the single dither mask while it is active. Freeze a hierarchy
        // level transition during that short interval rather than trying to compose two masks.
        var blend = UpdateLevelTransition(presentation,
            translucent, selection.Level,
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
        uint fadeSeed,
        ulong hiddenColumns = 0)
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
            PresentationFadeSeed = fadeSeed,
            HiddenColumnsLow = (uint)hiddenColumns,
            HiddenColumnsHigh = (uint)(hiddenColumns >> 32)
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
            visible.Page.Origin, visible.Page.Maximum, viewPosition);
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
        double DistanceSquared,
        ulong HiddenColumns);

    private readonly record struct SpatialSeamHashCache(
        string OwnerCanonicalHash,
        string? NeighborCanonicalHash,
        int? CaveCullBelowY,
        string CanonicalHash);

    internal readonly record struct SpatialForestCacheKey(
        double CameraChunkX,
        double CameraChunkZ,
        int NearDistance,
        int HorizonDistance,
        long CoarseCoverGeneration,
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

    private readonly record struct SeamUploadGroup(
        ((int X, int Z) Owner, Side BoundarySide) Key,
        KeyValuePair<TerrainLodSeamKey, TerrainLodSeamSelection>[] Parts,
        int Missing,
        bool Critical);

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

        public TerrainLodLayerSelection SelectLayerLevel(int requested, bool translucent) => translucent
            ? TerrainLodLayerSelection.Select(Levels, requested, static gpu => gpu.TranslucentMesh is not null)
            : TerrainLodLayerSelection.Select(Levels, requested, static gpu => gpu.SolidMesh is not null);

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
    // A four-chunk exact radius used to leave only a two-chunk block-scale LOD band.
    // Retain 1x1 local columns through eight chunks so nearby cave mouths and cliff
    // silhouettes do not immediately fall to 2x2 after the exact handoff.
    private const double MaximumExactVoxelDistance = 128;
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
