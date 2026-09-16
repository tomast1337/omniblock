using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
using OmniBlock.Client.Worlds;
using OmniBlock.Profiling;
using OmniBlock.Util;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using Silk.NET.Maths;
using Silk.NET.WebGPU;

namespace OmniBlock.Client.Rendering.Chunks;

public class ChunkRenderer : IChunkVisibilityVisitor
{
    private const int MaxRenderDistance = 32 + 1;
    private const int MaxMeshWorkers = 8;
    internal const int MeshSafetyRingRadius = 3;
    internal const int MeshForegroundRingRadius = 8;
    internal const long MeshAgePromotionTicks = 120;
    internal const double MeshPredictionTicks = 10.0;
    internal const double MeshPrefetchMargin = SubChunkRenderer.Size;
    internal const int MeshSpeculativeRadius = 1;
    internal const int MeshRetentionMargin = 2;
    internal const int MeshEvictionGraceFrames = 30;
    internal const int MeshEvictionLimitPerFrame = 256;
    internal const int MinimumCriticalMeshDeadlineFrames = 2;
    internal const double CriticalMeshDeadlineMs = 50.0;
    internal const int MeshDiscoveryBacklogPerWorker = 8;
    internal const int MeshSafetyBacklogPerWorker = 2;
    internal const int MeshForegroundBacklogPerWorker = 4;
    internal const int MeshStreamingBoundaryBacklogPerWorker = 2;
    internal const int MeshLeadingEdgeBacklogPerWorker = 2;
    internal const int MeshLeadingEdgeInspectionPerTick = 64;
    internal const int CriticalDispatchReserve = MaxMeshWorkers;
    internal const int CriticalUploadReserve = MaxMeshWorkers * 2;
    internal const int LightEvaluationCapacity = 16;
    internal const int LightEvaluationDispatchLimitPerFrame = 4;
    internal const int LightCompletionLimitPerFrame = 16;
    internal const int LightUploadLimitPerFrame = 4;
    internal const int NearFieldGraphGraceFrames = 2;

    //TODO: MAKE THIS CONFIGURABLE
    private const double MeshUploadBudgetMs = 1.5;
    private const long MeshUploadBudgetBytes = 8L * 1024 * 1024;

    //TODO: MAKE THIS CONFIGURABLE
    private const double MeshDispatchBudgetMs = 1.5;

    /// <summary>Bytes of <see cref="ChunkDrawMetadata" />, as chunk.wgsl declares the block.</summary>
    private const uint ChunkDrawMetadataSize = 96;
    /// <summary>Bytes of <see cref="ChunkFrameUniforms" />, as chunk.wgsl declares the block.</summary>
    private const uint ChunkFrameUniformSize = 240;

    private static readonly Vector3D<int>[] s_spiralOffsets;
    private static readonly Vector2D<int>[] s_safetyColumnOffsets;
    private static readonly Vector2D<int>[] s_foregroundColumnOffsets;
    private readonly HashSet<Vector3D<int>> _everPresentedMeshes = [];
    private readonly ILogger<ChunkRenderer> _logger = Log.Instance.For<ChunkRenderer>();
    private readonly ChunkMeshGenerator _meshGenerator;
    private readonly MeshLifecycleDiagnostics _meshLifecycle = new();
    private readonly List<SubChunkRenderer> _occludedRenderersBuffer = [];
    private readonly List<SubChunkRenderer> _outsideRetentionRenderers = [];
    private readonly SectionVisibilityGraph _visibilityGraph = new();
    private readonly GameOptions _options;
    private readonly Dictionary<Vector3D<int>, SectionRenderState> _sections = [];
    private readonly ResidentSectionSpatialIndex _residentSpatialIndex = new();
    // Iteration index only. SectionRenderState remains the residency authority; excluding
    // scheduling-only states keeps per-frame culling independent of background queue size.
    private readonly HashSet<SectionRenderState> _residentSections = [];
    // Boundary arrivals are cheap invalidations until admitted. Keeping only section keys here
    // coalesces repeated neighbor notifications without allocating snapshots or worker jobs.
    private readonly Queue<Vector3D<int>> _deferredStreamingBoundaries = [];
    private readonly HashSet<Vector3D<int>> _deferredStreamingBoundaryKeys = [];
    private readonly Queue<Vector3D<int>> _leadingEdgeSections = [];
    private readonly HashSet<Vector3D<int>> _leadingEdgeSectionKeys = [];
    private readonly List<Vector3D<int>> _sectionsToRemove = [];
    private readonly List<SubChunkRenderer> _renderersToRemove = [];
    private readonly HashSet<Vector3D<int>> _activePresentationRegressions = [];
    private readonly HashSet<Vector3D<int>> _currentPresentationRegressions = [];
    private readonly HashSet<SectionRenderState> _activeNearFieldRescues = [];
    private readonly HashSet<SectionRenderState> _nearFieldRescuesThisFrame = [];
    private readonly HashSet<SectionRenderState> _evictionGraceSections = [];
    private readonly List<SectionRenderState> _evictionGraceToCancel = [];
    private readonly PriorityQueue<SectionEvictionCandidate, int> _evictionDeadlines = new();
    private readonly SectionLightEvaluationService _lightEvaluation;
    private TerrainGpuArenaSet? _terrainGpuArenas;
    private readonly Queue<Vector3D<int>> _pendingLightUpdates = [];
    private readonly HashSet<Vector3D<int>> _pendingLightUpdateKeys = [];
    private readonly HashSet<Vector3D<int>> _queuedLightUpdateKeys = [];
    private readonly HashSet<Vector3D<int>> _inFlightLightUpdateKeys = [];
    private readonly Dictionary<Vector3D<int>, long> _lightUpdateGenerations = [];
    private readonly SectionMeshRequestQueue _pendingMeshUpdates = new();
    private readonly TranslucentDistanceComparer _translucentDistanceComparer = new();
    private readonly List<SubChunkRenderer> _solidRenderers = [];
    private readonly List<SubChunkRenderer> _spatialCandidates = [];
    private readonly List<SubChunkRenderer> _translucentRenderers = [];
    // Includes empty presentations because portal traversal and near-field presentation
    // diagnostics operate on resident sections, not only drawable geometry layers.
    private readonly List<SubChunkRenderer> _visibleRenderers = [];

    /// <summary>
    ///     One chunk.wgsl pipeline per raster state the terrain is drawn under, built on demand.
    /// </summary>
    /// <remarks>
    ///     Empty under OpenGL. WebGPU bakes blend, depth and cull into the pipeline, so what the GL
    ///     path expresses by changing global state between the solid and the translucent pass has to
    ///     be a second pipeline here.
    /// </remarks>
    private readonly Dictionary<RenderState, WgpuPipeline> _wgpuPipelines = [];

    /// <summary>
    ///     Same raster state as <see cref="_wgpuPipelines" />, keyed separately because a wireframe
    ///     pipeline differs from the solid one in topology and fragment entry point — two things
    ///     <see cref="RenderState" /> does not carry, so the same key would collide with the solid
    ///     pipeline built for that state.
    /// </summary>
    private readonly Dictionary<RenderState, WgpuPipeline> _wgpuWireframePipelines = [];

    private readonly World _world;
    private double _averageFrameDurationMs = 1000.0 / 60.0;
    private int _criticalDispatchesSincePump;
    private int _currentIndex;
    private int _frameIndex;
    private long _lastPrepareFrameAt;
    private ICuller? _lastCamera;
    private int _lastRenderDistance;
    private Vector3D<int>? _lastRequestRankCenter;
    private Vector3D<int>? _lastLeadingEdgeCenter;
    private Vector2D<int>? _lastEvictionCenter;
    private int _lastEvictionRadius = -1;
    private Vector3D<double> _lastViewPos;
    private int _meshReadyRadius = int.MaxValue;
    private Matrix4X4<float> _modelView;
    private Vector3D<double> _predictedViewPos;
    private Matrix4X4<float> _projection;
    private long _presentationRegressionCount;
    private long _lightRefreshCompletedCount;
    private long _nextLightUpdateGeneration;
    private long _schedulerTick;
    private int _geometryUploadsThisFrame;
    private int _geometryUploadsLastFrame;
    private int _lightUploadsThisFrame;
    private int _lightUploadsLastFrame;
    private int _solidDrawsThisFrame;
    private int _solidDrawsLastFrame;
    private int _translucentDrawsThisFrame;
    private int _translucentDrawsLastFrame;
    private readonly FrameTimingWindow _findVisibleTimings = new();
    private readonly FrameTimingWindow _spatialCullTimings = new();
    private readonly FrameTimingWindow _candidateSortTimings = new();
    private readonly FrameTimingWindow _portalTraversalTimings = new();
    private readonly FrameTimingWindow _terrainSubmitTimings = new();
    private ChunkPresentationProfileSnapshot _presentationProfile;
    private ChunkVisibilityResult _visibilityThisFrame;
    private SpatialQueryDiagnostics _spatialQueryThisFrame;
    private bool _hasPreparedFrame;
    private int _safetyRescuedThisFrame;
    private int _incompleteAdjacencyRescuedThisFrame;
    private int _newPresentationRescuedThisFrame;
    private int _presentationRegressionRescuedThisFrame;
    private int _oldestSafetyRescueFramesThisFrame;
    private int _residentSolidLayersThisFrame;
    private int _residentTranslucentLayersThisFrame;
    private int _presentedSolidLayersThisFrame;
    private int _presentedTranslucentLayersThisFrame;
    private int _terrainUniformEntriesThisFrame;
    private int _availableQuadsThisFrame;
    private int _submittedQuadsThisFrame;
    private int _directionDrawRangesThisFrame;
    private int _unassignedQuadsThisFrame;
    private int _terrainSubmissionBatchesThisFrame;
    private int _terrainPipelineBindsThisFrame;
    private int _terrainTextureBindsThisFrame;
    private int _terrainStreamBindsThisFrame;
    private double _findVisibleMsThisFrame;
    private double _portalTraversalMsThisFrame;
    private double _terrainSubmitMsThisFrame;
    private FogState _terrainFog = FogState.Default;
    internal ITerrainPresentationHandoff? PresentationHandoff { get; set; }

    /// <summary>
    ///     Reused across frames so the solid pass's per-chunk uniform batch (see
    ///     <see cref="RenderSolidWebGpu" />) doesn't allocate one every frame — grown, never shrunk.
    /// </summary>
    private ChunkDrawMetadata[] _solidUniformScratch = [];
    private ChunkDrawMetadata[] _translucentUniformScratch = [];

    static ChunkRenderer()
    {
        var offsets = new List<Vector3D<int>>();

        for (var x = -MaxRenderDistance; x <= MaxRenderDistance; x++)
        {
            for (var y = -8; y <= 8; y++)
            {
                for (var z = -MaxRenderDistance; z <= MaxRenderDistance; z++)
                {
                    offsets.Add(new Vector3D<int>(x, y, z));
                }
            }
        }

        offsets.Sort(CompareDiscoveryOffsets);

        s_spiralOffsets = [.. offsets];

        var safetyColumns = new List<Vector2D<int>>();
        for (var x = -MeshSafetyRingRadius; x <= MeshSafetyRingRadius; x++)
        for (var z = -MeshSafetyRingRadius; z <= MeshSafetyRingRadius; z++)
        {
            if (x * x + z * z <= MeshSafetyRingRadius * MeshSafetyRingRadius)
                safetyColumns.Add(new Vector2D<int>(x, z));
        }

        safetyColumns.Sort(CompareBalancedHorizontalOffsets);
        s_safetyColumnOffsets = [.. safetyColumns];

        var foregroundColumns = new List<Vector2D<int>>();
        for (var x = -MeshForegroundRingRadius; x <= MeshForegroundRingRadius; x++)
        for (var z = -MeshForegroundRingRadius; z <= MeshForegroundRingRadius; z++)
        {
            var distanceSquared = x * x + z * z;
            if (distanceSquared <= MeshForegroundRingRadius * MeshForegroundRingRadius &&
                distanceSquared > MeshSafetyRingRadius * MeshSafetyRingRadius)
                foregroundColumns.Add(new Vector2D<int>(x, z));
        }

        foregroundColumns.Sort(CompareBalancedHorizontalOffsets);
        s_foregroundColumnOffsets = [.. foregroundColumns];
    }

    /// <summary>
    ///     Orders an equal-distance horizontal offset beside its opposite before moving to the
    ///     next direction. Plain stable distance sorting inherited the nested x/z construction
    ///     order, so a bounded discovery pass repeatedly filled one screen quadrant last.
    /// </summary>
    internal static int CompareBalancedHorizontalOffsets(Vector2D<int> left, Vector2D<int> right)
    {
        var distance = (left.X * left.X + left.Y * left.Y)
            .CompareTo(right.X * right.X + right.Y * right.Y);
        if (distance != 0) return distance;

        var leftKey = AntipodalKey(left.X, left.Y);
        var rightKey = AntipodalKey(right.X, right.Y);
        var x = leftKey.X.CompareTo(rightKey.X);
        if (x != 0) return x;
        var z = leftKey.Z.CompareTo(rightKey.Z);
        return z != 0 ? z : leftKey.Phase.CompareTo(rightKey.Phase);
    }

    private static int CompareDiscoveryOffsets(Vector3D<int> left, Vector3D<int> right)
    {
        var distance = (left.X * left.X + left.Y * left.Y + left.Z * left.Z)
            .CompareTo(right.X * right.X + right.Y * right.Y + right.Z * right.Z);
        if (distance != 0) return distance;

        var verticalDistance = Math.Abs(left.Y).CompareTo(Math.Abs(right.Y));
        if (verticalDistance != 0) return verticalDistance;
        var verticalSide = left.Y.CompareTo(right.Y);
        if (verticalSide != 0) return verticalSide;
        return CompareBalancedHorizontalOffsets(
            new Vector2D<int>(left.X, left.Z),
            new Vector2D<int>(right.X, right.Z));
    }

    private static (int X, int Z, int Phase) AntipodalKey(int x, int z)
    {
        if (x < 0 || x == 0 && z <= 0) return (x, z, 0);
        return (-x, -z, 1);
    }

    public ChunkRenderer(World world, GameOptions options)
    {
        _options = options;

        // Meshes are CPU-heavy. Reserving two logical processors is not enough on high-core-count
        // machines: dozens of workers contend with entity rendering, simulation and networking
        // even when the mesh queue is already draining immediately.
        _meshGenerator = new ChunkMeshGenerator((ushort)GetMeshWorkerCount(Environment.ProcessorCount), _meshLifecycle);
        _lightEvaluation = new SectionLightEvaluationService(LightEvaluationCapacity);
        _world = world;
    }

    /// <summary>
    ///     Debug toggle: draws the solid pass as flat-green triangle edges instead of textured
    ///     terrain. Set from <c>Diagnostics/Windows/RenderInfoWindow.cs</c>. Translucent geometry
    ///     (water, glass) still draws normally — wireframe is a solid-terrain debug view, not a
    ///     replacement for the whole frame.
    /// </summary>
    public bool WireframeEnabled { get; set; }

    public bool UseOcclusionCulling { get; set; } = true;
    internal ChunkMeshProfileSnapshot MeshProfile => _meshGenerator.Profile;
    internal ChunkMeshCostSnapshot MeshCostProfile => _meshGenerator.CostProfile;
    internal long CompletedMeshResultBytes => _meshGenerator.CompletedResultBytes;
    internal long InFlightEstimatedMeshResultBytes => _meshGenerator.InFlightEstimatedResultBytes;
    internal double InFlightEstimatedMeshBuildMs => _meshGenerator.InFlightEstimatedBuildMs;
    internal long MeshBuildAdmissionDeferrals => _meshGenerator.BuildAdmissionDeferrals;
    internal long MeshUploadAdmissionDeferrals => _meshGenerator.UploadAdmissionDeferrals;
    internal long MeshOversizedUploadAdmissions => _meshGenerator.OversizedUploadAdmissions;
    internal MeshLifecycleSnapshot MeshLifecycle => _meshLifecycle.Snapshot();
    internal string CreateMeshLifecycleDump() => _meshLifecycle.CreateDump();
    internal string CreateMeshSectionDump()
    {
        var text = new StringBuilder("sectionId\tx\ty\tz\tepoch\tlastMeshed\tpendingEpoch\tdirtyReasons\tdeferredReasons\toutsideRetentionSinceFrame\trequestId\trequestStage\trequestAgeMs\trequestStageAgeMs\trequestDeadlineFrame\tresidentRequestId\tresidentStage\tresidentAgeMs\tresidentStageAgeMs\tresidentDeadlineFrame\n");
        foreach (var section in _sections.Values.OrderBy(s => s.LifetimeId))
        {
            var version = section.Version.State;
            text.Append(section.LifetimeId).Append('\t').Append(section.Position.X).Append('\t')
                .Append(section.Position.Y).Append('\t').Append(section.Position.Z).Append('\t')
                .Append(version.Epoch).Append('\t').Append(version.LastMeshed).Append('\t').Append(version.Pending).Append('\t')
                .Append(section.DirtyReasons).Append('\t').Append(section.DeferredDirtyReasons).Append('\t')
                .Append(section.OutsideRetentionSinceFrame).Append('\t')
                .Append(_meshLifecycle.Describe(section.PendingTrace)).Append('\t')
                .Append(_meshLifecycle.Describe(section.ResidentTrace)).AppendLine();
        }
        return text.ToString();
    }

    internal int PendingMeshWork
    {
        get
        {
            var profile = MeshProfile;
            return _pendingMeshUpdates.Count + profile.Outstanding;
        }
    }

    public int TotalChunks => ResidentMeshCount;
    public int ChunksInFrustum { get; private set; }
    public int ChunksOccluded { get; private set; }
    public int ChunksRendered { get; private set; }
    public int TranslucentMeshes { get; private set; }
    internal int ResidentMeshCount => _residentSections.Count;
    internal int PresentedMeshCount => _visibleRenderers.Count;
    internal int ForegroundPending => CountPending(MeshWorkPriority.Foreground);
    internal int BackgroundPending => CountPending(MeshWorkPriority.Background);
    internal int DeferredStreamingBoundaryCount => CountDeferred(SectionDirtyReason.StreamingBoundary);
    internal int LeadingEdgePending => CountPendingWithReason(SectionDirtyReason.LeadingEdge);
    internal int LeadingEdgeQueued => _leadingEdgeSections.Count;
    internal int EvictionGraceMeshCount => _evictionGraceSections.Count;
    internal long OldestForegroundAge => OldestPendingAge(MeshWorkPriority.Foreground);
    internal long PresentationRegressionCount => _presentationRegressionCount;
    internal int LightRefreshPending => _pendingLightUpdateKeys.Count;
    internal long LightRefreshCompletedCount => _lightRefreshCompletedCount;
    internal int GeometryUploadsLastFrame => _geometryUploadsLastFrame;
    internal int LightUploadsLastFrame => _lightUploadsLastFrame;
    internal int SolidDrawsLastFrame => _solidDrawsLastFrame;
    internal int TranslucentDrawsLastFrame => _translucentDrawsLastFrame;
    internal ChunkPresentationProfileSnapshot PresentationProfile => _presentationProfile;
    internal TerrainGpuArenaSnapshot TerrainGpuArenaProfile =>
        _terrainGpuArenas?.Snapshot() ?? default;

    internal int MeshReadyRadius => _meshReadyRadius == int.MaxValue
        ? Math.Max(0, _lastRenderDistance)
        : _meshReadyRadius;

    /// <summary>
    ///     Accepts every resident mesh selected by the frustum/occlusion stage. MeshReadyRadius is
    ///     diagnostic state only: ordinary camera movement may make a new safety ring incomplete,
    ///     but must never hide valid meshes that were already uploaded and remain resident.
    /// </summary>
    public void Visit(SubChunkRenderer renderer) => _visibleRenderers.Add(renderer);

    internal static int GetMeshWorkerCount(int processorCount) =>
        Math.Clamp((processorCount - 2) / 2, 1, MaxMeshWorkers);

    internal static int GetMeshDiscoveryCapacity(int pending, int workerCount) =>
        Math.Max(0, workerCount * MeshDiscoveryBacklogPerWorker - pending);

    internal static int GetMeshSafetyDiscoveryCapacity(int foregroundPending, int workerCount) =>
        Math.Max(0, workerCount * MeshSafetyBacklogPerWorker - foregroundPending);

    internal static int GetMeshForegroundDiscoveryCapacity(int foregroundPending, int workerCount) =>
        Math.Max(0, workerCount * MeshForegroundBacklogPerWorker - foregroundPending);

    internal static int GetStreamingBoundaryAdmissionCapacity(int streamingPending, int workerCount) =>
        Math.Max(0, workerCount * MeshStreamingBoundaryBacklogPerWorker - streamingPending);

    internal static int GetLeadingEdgeAdmissionCapacity(int leadingEdgePending, int workerCount) =>
        Math.Max(0, workerCount * MeshLeadingEdgeBacklogPerWorker - leadingEdgePending);

    internal void ResetMeshProfile() => _meshGenerator.ResetProfile();

    internal MeshSafetyRingState GetMeshSafetyRingState(Vector3D<double> viewPosition)
    {
        var centerX = (int)Math.Floor(viewPosition.X / SubChunkRenderer.Size);
        var centerZ = (int)Math.Floor(viewPosition.Z / SubChunkRenderer.Size);
        var loadedColumns = 0;
        var expectedSections = 0;
        var missingMeshes = 0;

        for (var dx = -MeshSafetyRingRadius; dx <= MeshSafetyRingRadius; dx++)
        for (var dz = -MeshSafetyRingRadius; dz <= MeshSafetyRingRadius; dz++)
        {
            if (dx * dx + dz * dz > MeshSafetyRingRadius * MeshSafetyRingRadius) continue;

            var chunkX = centerX + dx;
            var chunkZ = centerZ + dz;
            if (!_world.BlockHost.HasChunk(chunkX, chunkZ) ||
                !_world.BlockHost.GetChunk(chunkX, chunkZ).Loaded) continue;

            loadedColumns++;
            for (var y = 0; y < ChuckFormat.WorldHeight; y += SubChunkRenderer.Size)
            {
                expectedSections++;
                if (!HasRenderer(new Vector3D<int>(
                        chunkX * SubChunkRenderer.Size, y, chunkZ * SubChunkRenderer.Size)))
                    missingMeshes++;
            }
        }

        return new MeshSafetyRingState(loadedColumns, expectedSections, missingMeshes);
    }

    /// <summary>
    ///     Captures the CPU-side streaming and mesh state as a compact TSV grid. Unlike a screenshot,
    ///     this neither copies the swap-chain texture back from the GPU nor encodes an image, so it
    ///     is suitable for timing-sensitive E2E diagnostics.
    /// </summary>
    internal string CreateTerrainStateDump(Vector3D<double> viewPosition)
    {
        var centerX = (int)Math.Floor(viewPosition.X / SubChunkRenderer.Size);
        var centerZ = (int)Math.Floor(viewPosition.Z / SubChunkRenderer.Size);
        var radius = Math.Max(MeshSafetyRingRadius, _lastRenderDistance);
        var visible = _visibleRenderers.Select(static renderer => renderer.Position).ToHashSet();
        var dirty = _pendingMeshUpdates.Items.Select(static state => state.Position).ToHashSet();
        var text = new StringBuilder(256 + (radius * 2 + 1) * (radius * 2 + 1) * 48);

        text.Append("center\t").Append(centerX).Append('\t').Append(centerZ).AppendLine();
        text.Append("viewDistance\t").Append(_lastRenderDistance).AppendLine();
        text.Append("meshReadyRadius\t").Append(MeshReadyRadius).AppendLine();
        text.Append("totalRenderers\t").Append(ResidentMeshCount).AppendLine();
        text.Append("presentedMeshes\t").Append(PresentedMeshCount).AppendLine();
        text.Append("foregroundPending\t").Append(ForegroundPending).AppendLine();
        text.Append("backgroundPending\t").Append(BackgroundPending).AppendLine();
        text.Append("lightRefreshPending\t").Append(LightRefreshPending).AppendLine();
        text.Append("lightRefreshCompleted\t").Append(LightRefreshCompletedCount).AppendLine();
        text.Append("geometryUploadsLastFrame\t").Append(GeometryUploadsLastFrame).AppendLine();
        text.Append("lightUploadsLastFrame\t").Append(LightUploadsLastFrame).AppendLine();
        text.Append("solidDrawsLastFrame\t").Append(SolidDrawsLastFrame).AppendLine();
        text.Append("translucentDrawsLastFrame\t").Append(TranslucentDrawsLastFrame).AppendLine();
        var presentation = PresentationProfile;
        text.Append("residentSolidLayers\t").Append(presentation.ResidentSolidLayers).AppendLine();
        text.Append("residentTranslucentLayers\t").Append(presentation.ResidentTranslucentLayers).AppendLine();
        text.Append("visibilityCandidates\t").Append(presentation.VisibilityCandidates).AppendLine();
        text.Append("spatialRegionTests\t").Append(presentation.SpatialRegionTests).AppendLine();
        text.Append("spatialColumnTests\t").Append(presentation.SpatialColumnTests).AppendLine();
        text.Append("spatialSectionTests\t").Append(presentation.SpatialSectionTests).AppendLine();
        text.Append("spatialFrustumCandidates\t").Append(presentation.SpatialFrustumCandidates).AppendLine();
        text.Append("spatialCandidatesOutsideRenderDistance\t")
            .Append(presentation.SpatialCandidatesOutsideRenderDistance).AppendLine();
        text.Append("spatialSortComparisons\t").Append(presentation.SpatialSortComparisons).AppendLine();
        text.Append("frustumTests\t").Append(presentation.FrustumTests).AppendLine();
        text.Append("portalVisited\t").Append(presentation.PortalVisited).AppendLine();
        text.Append("disconnectedSeeds\t").Append(presentation.DisconnectedSeeds).AppendLine();
        text.Append("portalQueuePops\t").Append(presentation.PortalQueuePops).AppendLine();
        text.Append("portalDrawFrustumTests\t").Append(presentation.PortalDrawFrustumTests).AppendLine();
        text.Append("portalEdgeAttempts\t").Append(presentation.PortalEdgeAttempts).AppendLine();
        text.Append("portalMissingNeighbors\t").Append(presentation.PortalMissingNeighbors).AppendLine();
        text.Append("portalMarginFrustumTests\t").Append(presentation.PortalMarginFrustumTests).AppendLine();
        text.Append("portalMarginRejected\t").Append(presentation.PortalMarginRejected).AppendLine();
        text.Append("portalDuplicateReaches\t").Append(presentation.PortalDuplicateReaches).AppendLine();
        text.Append("portalSuccessfulReaches\t").Append(presentation.PortalSuccessfulReaches).AppendLine();
        text.Append("portalMarginCacheHits\t").Append(presentation.PortalMarginCacheHits).AppendLine();
        text.Append("safetyRescued\t").Append(presentation.SafetyRescued).AppendLine();
        text.Append("incompleteAdjacencyRescued\t").Append(presentation.IncompleteAdjacencyRescued).AppendLine();
        text.Append("newPresentationRescued\t").Append(presentation.NewPresentationRescued).AppendLine();
        text.Append("presentationRegressionRescued\t").Append(presentation.PresentationRegressionRescued).AppendLine();
        text.Append("oldestSafetyRescueFrames\t").Append(presentation.OldestSafetyRescueFrames).AppendLine();
        text.Append("presentedSolidLayers\t").Append(presentation.PresentedSolidLayers).AppendLine();
        text.Append("presentedTranslucentLayers\t").Append(presentation.PresentedTranslucentLayers).AppendLine();
        text.Append("emptyLayersSubmitted\t").Append(presentation.EmptyLayersSubmitted).AppendLine();
        text.Append("terrainDrawCalls\t").Append(presentation.TerrainDrawCalls).AppendLine();
        text.Append("availableQuads\t").Append(presentation.AvailableQuads).AppendLine();
        text.Append("submittedQuads\t").Append(presentation.SubmittedQuads).AppendLine();
        text.Append("directionRejectedQuads\t").Append(presentation.DirectionRejectedQuads).AppendLine();
        text.Append("directionDrawRanges\t").Append(presentation.DirectionDrawRanges).AppendLine();
        text.Append("unassignedQuads\t").Append(presentation.UnassignedQuads).AppendLine();
        text.Append("terrainUniformEntries\t").Append(presentation.TerrainUniformEntries).AppendLine();
        text.Append("terrainSubmissionBatches\t").Append(presentation.TerrainSubmissionBatches).AppendLine();
        text.Append("terrainPipelineBinds\t").Append(presentation.TerrainPipelineBinds).AppendLine();
        text.Append("terrainTextureBinds\t").Append(presentation.TerrainTextureBinds).AppendLine();
        text.Append("terrainUniformArenaCapacity\t").Append(presentation.TerrainUniformArenaCapacity).AppendLine();
        text.Append("terrainUniformArenaGrowths\t").Append(presentation.TerrainUniformArenaGrowths).AppendLine();
        AppendTiming("findVisible", presentation.FindVisible);
        AppendTiming("spatialCull", presentation.SpatialCull);
        AppendTiming("candidateSort", presentation.CandidateSort);
        AppendTiming("portalTraversal", presentation.PortalTraversal);
        AppendTiming("terrainSubmitCpu", presentation.TerrainSubmit);
        foreach (var state in _residentSections)
        {
            if (state.ActiveRescueReasons == NearFieldRescueReason.None) continue;
            text.Append("nearFieldRescue\t")
                .Append(state.Position.X).Append('\t')
                .Append(state.Position.Y).Append('\t')
                .Append(state.Position.Z).Append('\t')
                .Append(state.ActiveRescueReasons).Append('\t')
                .Append(state.RescueDurationFrames).AppendLine();
        }
        text.Append("deferredStreamingBoundaries\t").Append(DeferredStreamingBoundaryCount).AppendLine();
        text.Append("leadingEdgeQueued\t").Append(_leadingEdgeSections.Count).AppendLine();
        text.Append("leadingEdgePending\t").Append(LeadingEdgePending).AppendLine();
        text.Append("evictionGraceMeshes\t").Append(EvictionGraceMeshCount).AppendLine();
        text.Append("oldestForegroundAge\t").Append(OldestForegroundAge).AppendLine();
        text.Append("presentationRegressions\t").Append(PresentationRegressionCount).AppendLine();
        text.Append("pendingWork\t").Append(PendingMeshWork).AppendLine();
        var meshProfile = MeshProfile;
        text.Append("meshBuilds\t").Append(meshProfile.Meshes).AppendLine();
        text.Append("meshPagesBuilt\t").Append(meshProfile.Pages).AppendLine();
        text.Append("meshBlockCellsVisited\t").Append(meshProfile.BlockCellsVisited).AppendLine();
        text.Append("meshFullSectionBuilds\t").Append(meshProfile.FullSectionBuilds).AppendLine();
        text.Append("meshPartialSectionBuilds\t").Append(meshProfile.PartialSectionBuilds).AppendLine();
        var meshCost = MeshCostProfile;
        text.Append("meshCostBuildSamples\t").Append(meshCost.BuildSamples).AppendLine();
        text.Append("meshCostUploadSamples\t").Append(meshCost.UploadSamples).AppendLine();
        text.Append("meshCostBuildMsPerPage\t").Append(meshCost.BuildMsPerPage).AppendLine();
        text.Append("meshCostResultBytesPerPage\t").Append(meshCost.ResultBytesPerPage).AppendLine();
        text.Append("meshCostUploadBaseMs\t").Append(meshCost.UploadBaseMs).AppendLine();
        text.Append("meshCostUploadMsPerMiB\t").Append(meshCost.UploadMsPerMiB).AppendLine();
        text.Append("meshCompletedResultBytes\t").Append(CompletedMeshResultBytes).AppendLine();
        text.Append("meshInFlightEstimatedResultBytes\t").Append(InFlightEstimatedMeshResultBytes).AppendLine();
        text.Append("meshInFlightEstimatedBuildMs\t").Append(InFlightEstimatedMeshBuildMs).AppendLine();
        text.Append("meshBuildAdmissionDeferrals\t").Append(MeshBuildAdmissionDeferrals).AppendLine();
        text.Append("meshUploadAdmissionDeferrals\t").Append(MeshUploadAdmissionDeferrals).AppendLine();
        text.Append("meshOversizedUploadAdmissions\t").Append(MeshOversizedUploadAdmissions).AppendLine();
        var lifecycle = MeshLifecycle;
        text.Append("meshLifecycle\t").Append(lifecycle).AppendLine();
        text.Append("criticalCompleted\t").Append(lifecycle.CriticalCompleted).AppendLine();
        text.Append("criticalDeadlineMisses\t").Append(lifecycle.CriticalDeadlineMisses).AppendLine();
        text.Append("criticalOverdue\t").Append(lifecycle.CriticalOverdue).AppendLine();
        text.Append("criticalDeadlineFrames\t")
            .Append(CriticalDeadlineFramesFor(_averageFrameDurationMs)).AppendLine();
        text.Append("cooperativeCancellations\t").Append(lifecycle.CooperativeCancellations).AppendLine();
        text.Append("cancelledBeforeBuild\t").Append(lifecycle.CancelledBeforeBuild).AppendLine();
        text.Append("cancelledDuringBuild\t").Append(lifecycle.CancelledDuringBuild).AppendLine();
        text.AppendLine("chunkX\tchunkZ\tdistance2\tloaded\tmeshes\tvisible\tpending\tdirty\tforeground\tcritical\tbackground\tinitial\tleadingEdge\tstreamingBoundary\tdeferredStreamingBoundary\tblockChange\tlighting\tmaintenance\tyoungestMeshAge");

        for (var dz = -radius; dz <= radius; dz++)
        for (var dx = -radius; dx <= radius; dx++)
        {
            if (dx * dx + dz * dz > radius * radius) continue;
            var chunkX = centerX + dx;
            var chunkZ = centerZ + dz;
            var loaded = _world.BlockHost.HasChunk(chunkX, chunkZ) &&
                         _world.BlockHost.GetChunk(chunkX, chunkZ).Loaded;
            var meshes = 0;
            var visibleMeshes = 0;
            var pending = 0;
            var dirtyMeshes = 0;
            var foreground = 0;
            var critical = 0;
            var background = 0;
            var initial = 0;
            var leadingEdge = 0;
            var streamingBoundary = 0;
            var deferredStreamingBoundary = 0;
            var blockChange = 0;
            var lighting = 0;
            var maintenance = 0;
            var youngestMeshAge = long.MaxValue;

            for (var y = 0; y < ChuckFormat.WorldHeight; y += SubChunkRenderer.Size)
            {
                var pos = new Vector3D<int>(chunkX * SubChunkRenderer.Size, y,
                    chunkZ * SubChunkRenderer.Size);
                if (HasRenderer(pos)) meshes++;
                if (visible.Contains(pos)) visibleMeshes++;
                if (_sections.TryGetValue(pos, out var section))
                {
                    if (section.Version.State.Pending != -1) pending++;
                    if ((section.DirtyReasons & SectionDirtyReason.InitialTerrain) != 0) initial++;
                    if ((section.DirtyReasons & SectionDirtyReason.LeadingEdge) != 0) leadingEdge++;
                    if ((section.DirtyReasons & SectionDirtyReason.StreamingBoundary) != 0) streamingBoundary++;
                    if ((section.DeferredDirtyReasons & SectionDirtyReason.StreamingBoundary) != 0)
                        deferredStreamingBoundary++;
                    if ((section.DirtyReasons & SectionDirtyReason.BlockChange) != 0) blockChange++;
                    if ((section.DirtyReasons & SectionDirtyReason.Lighting) != 0) lighting++;
                    if ((section.DirtyReasons & SectionDirtyReason.Maintenance) != 0) maintenance++;
                    if (section.Renderer is not null && section.FirstUploadedAt >= 0)
                        youngestMeshAge = Math.Min(
                            youngestMeshAge,
                            _schedulerTick - section.FirstUploadedAt);
                }
                if (dirty.Contains(pos)) dirtyMeshes++;
                switch (RequestedPriority(pos))
                {
                    case MeshWorkPriority.Critical: critical++; break;
                    case MeshWorkPriority.Foreground: foreground++; break;
                    case MeshWorkPriority.Background: background++; break;
                }
            }

            text.Append(chunkX).Append('\t').Append(chunkZ).Append('\t')
                .Append(dx * dx + dz * dz).Append('\t').Append(loaded ? 1 : 0).Append('\t')
                .Append(meshes).Append('\t').Append(visibleMeshes).Append('\t')
                .Append(pending).Append('\t').Append(dirtyMeshes).Append('\t')
                .Append(foreground).Append('\t').Append(critical).Append('\t').Append(background).Append('\t')
                .Append(initial).Append('\t').Append(leadingEdge).Append('\t').Append(streamingBoundary).Append('\t')
                .Append(deferredStreamingBoundary).Append('\t')
                .Append(blockChange).Append('\t').Append(lighting).Append('\t').Append(maintenance).Append('\t')
                .Append(youngestMeshAge == long.MaxValue ? -1 : youngestMeshAge)
                .AppendLine();
        }

        return text.ToString();

        void AppendTiming(string name, FrameTimingSnapshot timing)
        {
            text.Append(name).Append("Ms\t").Append(timing.LastMs.ToString("F3")).AppendLine();
            text.Append(name).Append("AverageMs\t").Append(timing.AverageMs.ToString("F3")).AppendLine();
            text.Append(name).Append("P50Ms\t").Append(timing.P50Ms.ToString("F3")).AppendLine();
            text.Append(name).Append("P95Ms\t").Append(timing.P95Ms.ToString("F3")).AppendLine();
            text.Append(name).Append("MaxMs\t").Append(timing.MaxMs.ToString("F3")).AppendLine();
        }
    }

    /// <summary>
    ///     Compact counters and rolling timings for automated frame-cost diagnosis. Unlike the
    ///     terrain-state dump, this deliberately omits the per-column grid.
    /// </summary>
    internal string CreatePresentationProfileDump()
    {
        var profile = PresentationProfile;
        var text = new StringBuilder(2048);
        text.AppendLine("metric\tvalue");
        Counter("residentSections", profile.ResidentSections);
        Counter("residentSolidLayers", profile.ResidentSolidLayers);
        Counter("residentTranslucentLayers", profile.ResidentTranslucentLayers);
        Counter("visibilityCandidates", profile.VisibilityCandidates);
        Counter("spatialRegionTests", profile.SpatialRegionTests);
        Counter("spatialColumnTests", profile.SpatialColumnTests);
        Counter("spatialSectionTests", profile.SpatialSectionTests);
        Counter("spatialFrustumCandidates", profile.SpatialFrustumCandidates);
        Counter("spatialCandidatesOutsideRenderDistance", profile.SpatialCandidatesOutsideRenderDistance);
        Counter("spatialSortComparisons", profile.SpatialSortComparisons);
        Counter("frustumTests", profile.FrustumTests);
        Counter("portalVisited", profile.PortalVisited);
        Counter("disconnectedSeeds", profile.DisconnectedSeeds);
        Counter("portalQueuePops", profile.PortalQueuePops);
        Counter("portalDrawFrustumTests", profile.PortalDrawFrustumTests);
        Counter("portalEdgeAttempts", profile.PortalEdgeAttempts);
        Counter("portalMissingNeighbors", profile.PortalMissingNeighbors);
        Counter("portalMarginFrustumTests", profile.PortalMarginFrustumTests);
        Counter("portalMarginRejected", profile.PortalMarginRejected);
        Counter("portalDuplicateReaches", profile.PortalDuplicateReaches);
        Counter("portalSuccessfulReaches", profile.PortalSuccessfulReaches);
        Counter("portalMarginCacheHits", profile.PortalMarginCacheHits);
        Counter("safetyRescued", profile.SafetyRescued);
        Counter("presentedSections", profile.PresentedSections);
        Counter("presentedSolidLayers", profile.PresentedSolidLayers);
        Counter("presentedTranslucentLayers", profile.PresentedTranslucentLayers);
        Counter("terrainDrawCalls", profile.TerrainDrawCalls);
        Counter("availableQuads", profile.AvailableQuads);
        Counter("submittedQuads", profile.SubmittedQuads);
        Counter("directionRejectedQuads", profile.DirectionRejectedQuads);
        Counter("directionDrawRanges", profile.DirectionDrawRanges);
        Counter("unassignedQuads", profile.UnassignedQuads);
        Counter("terrainUniformEntries", profile.TerrainUniformEntries);
        Counter("terrainSubmissionBatches", profile.TerrainSubmissionBatches);
        Counter("terrainStreamBinds", profile.TerrainStreamBinds);
        var cost = MeshCostProfile;
        Counter("meshCostBuildSamples", cost.BuildSamples);
        Counter("meshCostUploadSamples", cost.UploadSamples);
        Value("meshCostBuildMsPerPage", cost.BuildMsPerPage);
        Counter("meshCostResultBytesPerPage", cost.ResultBytesPerPage);
        Value("meshCostUploadBaseMs", cost.UploadBaseMs);
        Value("meshCostUploadMsPerMiB", cost.UploadMsPerMiB);
        Counter("meshCompletedResultBytes", CompletedMeshResultBytes);
        Counter("meshInFlightEstimatedResultBytes", InFlightEstimatedMeshResultBytes);
        Value("meshInFlightEstimatedBuildMs", InFlightEstimatedMeshBuildMs);
        Counter("meshBuildAdmissionDeferrals", MeshBuildAdmissionDeferrals);
        Counter("meshUploadAdmissionDeferrals", MeshUploadAdmissionDeferrals);
        Counter("meshOversizedUploadAdmissions", MeshOversizedUploadAdmissions);
        var arena = TerrainGpuArenaProfile;
        Counter("terrainArenaRegions", arena.Regions);
        Counter("terrainArenaPairedSegments", arena.Segments);
        Counter("terrainArenaCapacityBytes", arena.CapacityBytes);
        Counter("terrainArenaAllocatedBytes", arena.AllocatedBytes);
        Counter("terrainArenaFreeBytes", arena.FreeBytes);
        Counter("terrainArenaLargestFreeRangeBytes", arena.LargestFreeRangeBytes);
        Counter("terrainArenaFragmentedFreeBytes", arena.FragmentedFreeBytes);
        Counter("terrainArenaActiveAllocations", arena.ActiveAllocations);
        Counter("terrainArenaPendingRetirements", arena.PendingRetirements);
        Counter("terrainArenaSegmentGrowths", arena.SegmentGrowths);
        Counter("terrainArenaFailedAllocations", arena.FailedAllocations);
        Value("terrainArenaExternalFragmentation", arena.ExternalFragmentation);
        text.AppendLine("timing\tsamples\tlastMs\taverageMs\tp50Ms\tp95Ms\tmaxMs");
        Timing("findVisible", profile.FindVisible);
        Timing("spatialCull", profile.SpatialCull);
        Timing("candidateSort", profile.CandidateSort);
        Timing("portalTraversal", profile.PortalTraversal);
        Timing("terrainSubmitCpu", profile.TerrainSubmit);
        return text.ToString();

        void Counter(string name, long value) => text.Append(name).Append('\t').Append(value).AppendLine();

        void Value(string name, double value) => text.Append(name).Append('\t')
            .Append(value.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)).AppendLine();

        void Timing(string name, FrameTimingSnapshot timing) =>
            text.Append(name).Append('\t').Append(timing.Samples).Append('\t')
                .Append(timing.LastMs.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                .Append(timing.AverageMs.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                .Append(timing.P50Ms.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                .Append(timing.P95Ms.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)).Append('\t')
                .Append(timing.MaxMs.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
    }

    /// <summary>
    ///     Chooses which sub-chunks the frame draws and records the explicitly supplied world-view
    ///     matrices used by both terrain passes.
    /// </summary>
    /// <remarks>
    ///     Separate from <see cref="Render" /> because none of it is OpenGL, and the WebGPU pass
    ///     draws the same chosen set. A pass that skipped this would find every renderer's
    ///     <c>LastVisibleFrame</c> stale and draw nothing at all.
    /// </remarks>
    public void PrepareFrame(ChunkRenderParams renderParams)
    {
        FinalizePresentationProfile();
        _geometryUploadsLastFrame = _geometryUploadsThisFrame;
        _geometryUploadsThisFrame = 0;
        _lightUploadsLastFrame = _lightUploadsThisFrame;
        _lightUploadsThisFrame = 0;
        _solidDrawsLastFrame = _solidDrawsThisFrame;
        _solidDrawsThisFrame = 0;
        _translucentDrawsLastFrame = _translucentDrawsThisFrame;
        _translucentDrawsThisFrame = 0;
        _terrainUniformEntriesThisFrame = 0;
        _availableQuadsThisFrame = 0;
        _submittedQuadsThisFrame = 0;
        _directionDrawRangesThisFrame = 0;
        _unassignedQuadsThisFrame = 0;
        _terrainSubmissionBatchesThisFrame = 0;
        _terrainPipelineBindsThisFrame = 0;
        _terrainTextureBindsThisFrame = 0;
        _terrainStreamBindsThisFrame = 0;
        _terrainSubmitMsThisFrame = 0;

        var prepareFrameAt = Stopwatch.GetTimestamp();
        if (_lastPrepareFrameAt != 0)
        {
            var sampleMs = (prepareFrameAt - _lastPrepareFrameAt) * 1000.0 / Stopwatch.Frequency;
            // Ignore debugger/suspend gaps. This average only converts a wall-time response
            // target into scheduler frames; it is not the authoritative frame-time metric.
            if (sampleMs is > 0 and < 250)
                _averageFrameDurationMs = _averageFrameDurationMs * 0.9 + sampleMs * 0.1;
        }
        _lastPrepareFrameAt = prepareFrameAt;

        _lastRenderDistance = renderParams.RenderDistance;
        _lastViewPos = renderParams.ViewPos;
        _lastCamera = renderParams.Camera;

        _modelView = renderParams.ModelView;
        _projection = renderParams.Projection;
        _terrainFog = renderParams.Fog;

        // The frame that took buffers out of these pools has been submitted by now, so they are
        // free to hand out again. Both terrain passes of this frame draw from them.
        foreach (var pipeline in _wgpuPipelines.Values)
        {
            pipeline.ResetUniformPool();
        }

        foreach (var pipeline in _wgpuWireframePipelines.Values)
        {
            pipeline.ResetUniformPool();
        }

        _visibleRenderers.Clear();
        _solidRenderers.Clear();
        _translucentRenderers.Clear();
        _frameIndex++;

        Vector3D<int> cameraChunkPos = new(
            (int)Math.Floor(renderParams.ViewPos.X / SubChunkRenderer.Size) * SubChunkRenderer.Size,
            (int)Math.Floor(renderParams.ViewPos.Y / SubChunkRenderer.Size) * SubChunkRenderer.Size,
            (int)Math.Floor(renderParams.ViewPos.Z / SubChunkRenderer.Size) * SubChunkRenderer.Size
        );

        TryGetResidentState(cameraChunkPos, out var cameraState);

        var meshReadyCenterX = cameraChunkPos.X / SubChunkRenderer.Size;
        var meshReadyCenterZ = cameraChunkPos.Z / SubChunkRenderer.Size;
        _meshReadyRadius = GetContiguousMeshRadius(
            meshReadyCenterX,
            meshReadyCenterZ,
            MeshSafetyRingRadius);

        if (cameraState == null)
        {
            var y = Math.Clamp(cameraChunkPos.Y, 0, 112);
            TryGetResidentState(new Vector3D<int>(cameraChunkPos.X, y, cameraChunkPos.Z), out cameraState);
        }

        float renderDistWorld = renderParams.RenderDistance * SubChunkRenderer.Size;

        var findVisibleAt = Stopwatch.GetTimestamp();
        using (Profiler.Begin("FindVisible"))
        {
            var spatial = _residentSpatialIndex.Query(
                renderParams.Camera, renderParams.ViewPos, renderDistWorld, _spatialCandidates,
                orderNearToFar: false);
            _spatialQueryThisFrame = spatial;
            Profiler.Record("SpatialCull", spatial.CullMs);
            Profiler.Record("CandidateSort", spatial.SortMs);
            var portalStarted = Stopwatch.GetTimestamp();
            var visibility = _visibilityGraph.FindVisible(
                this,
                _spatialCandidates,
                cameraState?.Renderer,
                renderParams.ViewPos,
                renderParams.Camera,
                renderDistWorld,
                UseOcclusionCulling,
                _frameIndex,
                candidatesKnownInFrustum: true
            );
            _portalTraversalMsThisFrame = Stopwatch.GetElapsedTime(portalStarted).TotalMilliseconds;
            Profiler.Record("PortalTraversal", _portalTraversalMsThisFrame);
            _visibilityThisFrame = visibility with
            {
                FrustumTests = visibility.FrustumTests + spatial.FrustumTests
            };
        }
        _findVisibleMsThisFrame = Stopwatch.GetElapsedTime(findVisibleAt).TotalMilliseconds;
        ChunksInFrustum = _visibilityThisFrame.FrustumCandidates;

        var safetyDiagnostics = RescueUntrustedNearField(cameraChunkPos, _frameIndex, renderParams.Camera);
        _safetyRescuedThisFrame = safetyDiagnostics.Rescued;
        _incompleteAdjacencyRescuedThisFrame = safetyDiagnostics.IncompleteAdjacency;
        _newPresentationRescuedThisFrame = safetyDiagnostics.NewPresentation;
        _presentationRegressionRescuedThisFrame = safetyDiagnostics.PresentationRegression;
        _oldestSafetyRescueFramesThisFrame = safetyDiagnostics.OldestDurationFrames;
        _visibilityThisFrame = _visibilityThisFrame with
        {
            FrustumTests = _visibilityThisFrame.FrustumTests + safetyDiagnostics.FrustumTests
        };

        RecordMissingNearFieldRegressions(cameraChunkPos, renderParams.Camera);

        var visitedVisibleCount = _visibleRenderers.Count;
        ChunksOccluded = ChunksInFrustum - visitedVisibleCount;
        ChunksRendered = visitedVisibleCount;

        if (renderParams.RenderOccluded)
        {
            _occludedRenderersBuffer.Clear();
            foreach (var renderer in _spatialCandidates)
            {
                if (renderer.LastVisibleFrame != _frameIndex)
                {
                    if (renderer.IsWithinRenderDistance(renderParams.ViewPos, renderDistWorld))
                    {
                        _occludedRenderersBuffer.Add(renderer);
                    }
                }
            }

            _visibleRenderers.Clear();
            _visibleRenderers.AddRange(_occludedRenderersBuffer);
            ChunksRendered = _visibleRenderers.Count;
        }

        _residentSolidLayersThisFrame = _residentSpatialIndex.SolidLayerCount;
        _residentTranslucentLayersThisFrame = _residentSpatialIndex.TranslucentLayerCount;

#if DEBUG
        // Full validation is deliberately sampled: membership mutations perform immediate local
        // checks, while this catches a stale unrelated slot without restoring a per-frame scan.
        if ((_frameIndex & 255) == 0) _residentSpatialIndex.Validate(_residentSections);
#endif

        foreach (var renderer in _visibleRenderers)
        {
            renderer.Update(renderParams.DeltaTime);
        }

        BuildLayerVisibleLists(_visibleRenderers, _solidRenderers, _translucentRenderers);
        // Opaque order is semantically irrelevant. Group regions deterministically so consecutive
        // pages can retain the same paired arena buffers; translucent order remains back-to-front.
        _solidRenderers.Sort(CompareSolidRegionOrder);
        _presentedSolidLayersThisFrame = _solidRenderers.Count;
        _presentedTranslucentLayersThisFrame = _translucentRenderers.Count;
        TranslucentMeshes = _translucentRenderers.Count;
    }

    internal static void BuildLayerVisibleLists(
        IReadOnlyList<SubChunkRenderer> visible,
        List<SubChunkRenderer> solid,
        List<SubChunkRenderer> translucent)
    {
        solid.Clear();
        translucent.Clear();
        for (var i = 0; i < visible.Count; i++)
        {
            var renderer = visible[i];
            if (renderer.HasSolidGeometry) solid.Add(renderer);
            if (renderer.HasTranslucentGeometry) translucent.Add(renderer);
        }
    }

    private static int CompareSolidRegionOrder(SubChunkRenderer left, SubChunkRenderer right)
    {
        var a = TerrainRenderRegionKey.FromSectionPosition(left.Position);
        var b = TerrainRenderRegionKey.FromSectionPosition(right.Position);
        var x = a.X.CompareTo(b.X);
        if (x != 0) return x;
        var y = a.Y.CompareTo(b.Y);
        if (y != 0) return y;
        var z = a.Z.CompareTo(b.Z);
        if (z != 0) return z;
        x = left.Position.X.CompareTo(right.Position.X);
        if (x != 0) return x;
        y = left.Position.Y.CompareTo(right.Position.Y);
        return y != 0 ? y : left.Position.Z.CompareTo(right.Position.Z);
    }

    /// <summary>
    ///     The frame's housekeeping: sub-chunks that fell out of range are dropped, and one queued
    ///     mesh update is taken.
    /// </summary>
    /// <remarks>
    ///     After the draw rather than before it, because a mesh replaced here is one the pass just
    ///     recorded from. Takes no parameters so that a backend which has to defer it past the
    ///     submit does not have to carry the frame's <see cref="ChunkRenderParams" /> along with it;
    ///     what it needs was kept by <see cref="PrepareFrame" />.
    /// </remarks>
    public void EndFrame()
    {
        // No frame has been prepared, so there is nothing this one drew to tidy up after.
        if (_lastCamera is null) return;

        using (Profiler.Begin("EvictionScan"))
            CollectDueEvictions();

        using (Profiler.Begin("EvictionApply"))
        {
            foreach (var renderer in _renderersToRemove)
            {
                if (!_sections.TryGetValue(renderer.Position, out var section)) continue;
                if (!_residentSpatialIndex.Remove(renderer))
                    throw new InvalidOperationException(
                        $"Resident spatial index did not contain evicted section {renderer.Position}.");

                UpdateAdjacency(renderer, false);
                _sections.Remove(renderer.Position);
                _residentSections.Remove(section);
                _evictionGraceSections.Remove(section);
                _pendingMeshUpdates.Remove(renderer.Position);
                ClearLightEvaluation(renderer.Position);
                section.DetachRenderer();
                section.Dispose();
                renderer.Dispose();
            }
        }

        _renderersToRemove.Clear();

        using (Profiler.Begin("MeshDispatch"))
            DispatchPendingMeshUpdates();
        using (Profiler.Begin("MeshInstall"))
            LoadNewMeshes(_lastViewPos);
        // Lighting has independent storage and runs after geometry admission/upload. A lava cast
        // can coalesce here, but cannot spend the frame budget before a critical block change.
        using (Profiler.Begin("LightRefresh"))
            RefreshPendingLights();
        // Replacements and evictions happen after the frame's command buffer was submitted. The
        // retired byte ranges can now be reused without invalidating an encoded draw.
        _terrainGpuArenas?.EndFrame();
    }

    private void CollectDueEvictions()
    {
        var center = new Vector2D<int>(
            (int)Math.Floor(_lastViewPos.X / SubChunkRenderer.Size),
            (int)Math.Floor(_lastViewPos.Z / SubChunkRenderer.Size));
        var radius = _lastRenderDistance + MeshRetentionMargin;
        if (_lastEvictionCenter != center || _lastEvictionRadius != radius)
        {
            // Only states already counting down need a re-entry check. Ordinary resident sections
            // inside the circle are represented by the spatial index and never touched here.
            foreach (var state in _evictionGraceSections)
            {
                if (state.IsDisposed || state.Renderer is null ||
                    IsInHorizontalChunkRadius(state.Position, _lastViewPos, radius))
                    _evictionGraceToCancel.Add(state);
            }
            foreach (var state in _evictionGraceToCancel)
            {
                state.CancelEvictionGrace();
                _evictionGraceSections.Remove(state);
            }
            _evictionGraceToCancel.Clear();

            _residentSpatialIndex.CollectOutsideHorizontalRadius(
                _lastViewPos, radius, _outsideRetentionRenderers);
            foreach (var renderer in _outsideRetentionRenderers)
            {
                if (!_sections.TryGetValue(renderer.Position, out var state) ||
                    !ReferenceEquals(state.Renderer, renderer) ||
                    !_evictionGraceSections.Add(state)) continue;

                state.BeginEvictionGrace(_frameIndex);
                _evictionDeadlines.Enqueue(
                    new SectionEvictionCandidate(
                        state, state.LifetimeId, state.OutsideRetentionSinceFrame),
                    state.OutsideRetentionSinceFrame + MeshEvictionGraceFrames);
            }
            _outsideRetentionRenderers.Clear();
            _lastEvictionCenter = center;
            _lastEvictionRadius = radius;
        }

        var admitted = 0;
        while (admitted < MeshEvictionLimitPerFrame &&
               _evictionDeadlines.TryPeek(out _, out var deadline) &&
               deadline <= _frameIndex)
        {
            var candidate = _evictionDeadlines.Dequeue();
            var state = candidate.State;
            if (!_evictionGraceSections.Contains(state) ||
                !state.OwnsResult(candidate.SectionId) ||
                state.OutsideRetentionSinceFrame != candidate.StartedFrame)
                continue;

            if (IsInHorizontalChunkRadius(state.Position, _lastViewPos, radius))
            {
                state.CancelEvictionGrace();
                _evictionGraceSections.Remove(state);
                continue;
            }
            if (!state.IsEvictionDue(_frameIndex, MeshEvictionGraceFrames))
            {
                _evictionDeadlines.Enqueue(
                    candidate,
                    state.OutsideRetentionSinceFrame + MeshEvictionGraceFrames);
                continue;
            }

            if (state.Renderer is not { } renderer)
            {
                state.CancelEvictionGrace();
                _evictionGraceSections.Remove(state);
                continue;
            }
            _renderersToRemove.Add(renderer);
            admitted++;
        }
    }

    public unsafe void Render(ChunkRenderParams renderParams)
    {
        PrepareFrame(renderParams);

        // Reject an absent solid layer before even resolving/binding its pipeline. Empty sections
        // remain in _visibleRenderers for traversal, but never enter render submission.
        if (_solidRenderers.Count > 0 && TryGetWebGpuFrame(out var pass, out var array))
        {
            using (Profiler.Begin("DrawChunks"))
            {
                var submitAt = Stopwatch.GetTimestamp();
                if (WireframeEnabled)
                {
                    RenderWireframeWebGpu(pass, WgpuWireframePipelineFor(RenderSystem.State.Current), array);
                }
                else
                {
                    RenderSolidWebGpu(pass, WgpuPipelineFor(RenderSystem.State.Current), array);
                }
                _terrainSubmitMsThisFrame += Stopwatch.GetElapsedTime(submitAt).TotalMilliseconds;
            }
        }

        // No EndFrame here: it destroys mesh buffers, and the draws recorded above have not been
        // submitted yet. The WebGPU renderer calls it once the frame is presented.
    }

    public unsafe void RenderTransparent(ChunkRenderParams renderParams)
    {
        if (_translucentRenderers.Count > 0 && TryGetWebGpuFrame(out var pass, out var array))
        {
            using (Profiler.Begin("DrawChunksTranslucent"))
            {
                var submitAt = Stopwatch.GetTimestamp();
                RenderTranslucentWebGpu(pass, WgpuPipelineFor(RenderSystem.State.Current), array,
                    renderParams.ViewPos);
                _terrainSubmitMsThisFrame += Stopwatch.GetElapsedTime(submitAt).TotalMilliseconds;
            }
        }
        else
        {
            // The sorted set is filled per frame by PrepareFrame and drained by whichever pass
            // draws it. Dropping the draw without dropping these would carry them into the next
            // frame and draw them twice.
            _translucentRenderers.Clear();
        }
    }

    /// <summary>
    ///     Drains completed meshes for <see cref="MeshUploadBudgetMs" /> instead of a fixed count
    ///     per frame. The fixed count (8) was sized for the old dispatch rate; now that
    ///     <see cref="DispatchPendingMeshUpdates" /> can issue far more per frame, a fixed drain
    ///     cap would let <c>_results</c> back up — completed meshes sitting queued instead of
    ///     uploaded is exactly the "visual delay" a bigger dispatch rate would otherwise cause.
    /// </summary>
    private void LoadNewMeshes(Vector3D<double> viewPos)
    {
        var stopwatch = Stopwatch.StartNew();
        var criticalUploads = 0;
        var admittedUploads = 0;
        var admittedBytes = 0L;
        while (true)
        {
            if (!_meshGenerator.TryPeekMesh(out var candidate, out var candidatePriority)) break;
            var remainingMs = Math.Max(0, MeshUploadBudgetMs - stopwatch.Elapsed.TotalMilliseconds);
            var remainingBytes = Math.Max(0, MeshUploadBudgetBytes - admittedBytes);
            var predictedUploadMs = _meshGenerator.EstimateUploadMs(candidate.UploadBytes);
            var regularAdmission = predictedUploadMs <= remainingMs && candidate.UploadBytes <= remainingBytes;
            var reserveAdmission = !regularAdmission &&
                                   criticalUploads < CriticalUploadReserve &&
                                   _meshGenerator.TryPeekMesh(MeshWorkPriority.Critical, out candidate);
            if (reserveAdmission) candidatePriority = MeshWorkPriority.Critical;

            var oversizedAdmission = !regularAdmission && !reserveAdmission && admittedUploads == 0;
            if (!regularAdmission && !reserveAdmission && !oversizedAdmission)
            {
                _meshGenerator.NoteUploadAdmissionDeferred();
                break;
            }

            if (oversizedAdmission) _meshGenerator.NoteOversizedUploadAdmission();
            if (!_meshGenerator.TryDequeueMesh(
                    candidatePriority,
                    // Oversized progress still consumed the lane selected by fairness. Only the
                    // critical reserve bypasses that selection and therefore must not advance it.
                    advanceFairness: !reserveAdmission,
                    out var mesh))
            {
                continue;
            }

            if (mesh.Priority == MeshWorkPriority.Critical) criticalUploads++;
            admittedUploads++;
            admittedBytes += mesh.UploadBytes;
            var uploadStart = Stopwatch.GetTimestamp();

            if (IsChunkInMeshRetentionDistance(mesh.Pos, viewPos))
            {
                // Pooled epochs can be reused after eviction. Do not confuse the old lifetime's
                // completed result with a new section occupying the same coordinates.
                if (!_sections.TryGetValue(mesh.Pos, out var owner) || !owner.OwnsResult(mesh.SectionId))
                {
                    _meshLifecycle.Cancel(mesh.Trace, MeshCancellationReason.SectionReplaced);
                    mesh.Dispose();
                    continue;
                }
                var section = owner;
                var version = section.Version;

                if (mesh.Cancelled)
                {
                    version.CancelMesh(mesh.Version);
                    var cancelledRetry = version.SnapshotIfNeeded();
                    if (cancelledRetry.HasValue)
                    {
                        var priority = MaxPriority(mesh.Priority, RequestedPriority(mesh.Pos));
                        _meshGenerator.MeshChunk(
                            _world, mesh.Pos, cancelledRetry.Value, _options.AlternateBlocksEnabled,
                            priority, section.BeginTrace(cancelledRetry.Value, _frameIndex), section.LifetimeId,
                            section.RebuildPlan,
                            _meshGenerator.EstimateCost(
                                section.RebuildPlan, section.LastMeshResultBytesPerPage));
                    }
                    mesh.Dispose();
                    continue;
                }

                var stale = version.IsStale(mesh.Version);
                var installIntermediate = stale &&
                                          mesh.Priority == MeshWorkPriority.Critical &&
                                          section.Renderer != null;
                if (stale && !installIntermediate)
                {
                    version.CancelMesh(mesh.Version);
                    _meshLifecycle.Cancel(mesh.Trace, MeshCancellationReason.Superseded);
                    var snapshot = version.SnapshotIfNeeded();
                    if (snapshot.HasValue)
                    {
                        var priority = MaxPriority(mesh.Priority, RequestedPriority(mesh.Pos));
                        _meshGenerator.MeshChunk(_world, mesh.Pos, snapshot.Value, _options.AlternateBlocksEnabled, priority,
                            section.BeginTrace(snapshot.Value, _frameIndex), section.LifetimeId, section.RebuildPlan,
                            _meshGenerator.EstimateCost(
                                section.RebuildPlan, section.LastMeshResultBytesPerPage));
                    }

                    // Superseded by the requeue above (or by whichever in-flight build already
                    // owns the next version) — this copy of the vertices is never uploaded, so
                    // nothing else will return it to the pool.
                    mesh.Dispose();
                    continue;
                }

                // A critical section which changes faster than one build must still advance
                // visually. Accept this coherent snapshot, then build the coalesced latest epoch;
                // discarding every stale result makes fluids and piston bursts freeze indefinitely.
                var followUpReasons = section.DirtyReasons;
                var followUpPriority = section.RequestedPriority;
                var followUpPlan = section.RebuildPlan;

                var device = WebGpuDevice.Current;
                if (device == null)
                {
                    mesh.Dispose();
                    throw new InvalidOperationException("Cannot install a chunk presentation without a WebGPU device.");
                }

                // Construct all buffers and mesh-derived metadata before touching live state. If
                // any allocation/upload fails, Create disposes the partial candidate and the old
                // presentation remains fully authoritative.
                var presentation = SectionPresentation.Create(
                    device,
                    _terrainGpuArenas ??= new TerrainGpuArenaSet(device),
                    mesh.Pos,
                    mesh.Pages,
                    section.Renderer?.Presentation,
                    mesh.RebuildPlan,
                    mesh.VisibilityData,
                    mesh.IsLit,
                    mesh.Version,
                    _meshLifecycle,
                    mesh.Trace);

                var isNewResident = section.Renderer == null;
                var resident = section.Renderer ?? new SubChunkRenderer(mesh.Pos);
                try
                {
                    section.CommitPresentation(resident, presentation);
                }
                catch
                {
                    // Commit validates before publishing. Ownership transfers only on success.
                    presentation.Dispose();
                    if (isNewResident) resident.Dispose();
                    throw;
                }

                if (isNewResident)
                {
                    _residentSections.Add(section);
                    UpdateAdjacency(resident, true);
                }

                section.NotePresentationInstalled(_frameIndex);
                section.RecordMeshCost(mesh.BuildMs, mesh.RetainedBytes, mesh.RebuildPlan.PageBuildCount);
                _residentSpatialIndex.AddOrUpdate(resident);
#if DEBUG
                if (_residentSpatialIndex.Count != _residentSections.Count ||
                    !_residentSpatialIndex.Contains(resident))
                    throw new InvalidOperationException(
                        $"Resident spatial index diverged while installing {resident.Position}.");
#endif

                var empty = presentation.IsEmpty;
                section.RecordUploaded(_schedulerTick, mesh.Trace, empty);
                _geometryUploadsThisFrame++;
                section.ClearRequest();
                if (_world is ClientWorld clientWorld)
                    clientWorld.NetworkHandler.NotifyMeshUploaded(mesh.Pos);

                if (stale)
                {
                    var followUpDeadline = followUpPriority == MeshWorkPriority.Critical
                        ? _frameIndex + CriticalDeadlineFramesFor(_averageFrameDurationMs)
                        : -1;
                    section.RememberRequest(
                        followUpReasons, followUpPriority, _schedulerTick, followUpDeadline, followUpPlan);
                    var snapshot = version.SnapshotIfNeeded();
                    if (snapshot.HasValue)
                    {
                        _meshGenerator.MeshChunk(
                            _world, mesh.Pos, snapshot.Value, _options.AlternateBlocksEnabled,
                            followUpPriority, section.BeginTrace(snapshot.Value, _frameIndex), section.LifetimeId,
                            section.RebuildPlan,
                            _meshGenerator.EstimateCost(
                                section.RebuildPlan, section.LastMeshResultBytesPerPage));
                    }
                }
            }
            else
            {
                _meshLifecycle.Cancel(mesh.Trace, MeshCancellationReason.OutsideRetention);
                // Finished after the chunk fell out of render distance — SectionPresentation.Create
                // (which normally returns these CPU lists to the pool) never runs for it.
                mesh.Dispose();
                if (_sections.TryGetValue(mesh.Pos, out var section) &&
                    section.OwnsResult(mesh.SectionId) &&
                    section.Version.State.Pending == mesh.Version)
                    section.AbandonRequest(MeshCancellationReason.OutsideRetention);
                continue;
            }

            var uploadedAt = Stopwatch.GetTimestamp();
            _meshGenerator.RecordUpload(
                uploadedAt - uploadStart,
                uploadedAt - mesh.FinishedAt,
                uploadedAt - mesh.RequestedAt,
                mesh.UploadBytes);
        }
    }

    private void UpdateAdjacency(SubChunkRenderer renderer, bool added)
    {
        var pos = renderer.Position;
        var size = SubChunkRenderer.Size;

        SubChunkRenderer? Get(Vector3D<int> p) =>
            _sections.TryGetValue(p, out var section) ? section.Renderer : null;

        var down = Get(pos + new Vector3D<int>(0, -size, 0));
        var up = Get(pos + new Vector3D<int>(0, size, 0));
        var north = Get(pos + new Vector3D<int>(0, 0, -size));
        var south = Get(pos + new Vector3D<int>(0, 0, size));
        var west = Get(pos + new Vector3D<int>(-size, 0, 0));
        var east = Get(pos + new Vector3D<int>(size, 0, 0));

        if (added)
        {
            renderer.AdjacentDown = down;
            renderer.AdjacentUp = up;
            renderer.AdjacentNorth = north;
            renderer.AdjacentSouth = south;
            renderer.AdjacentWest = west;
            renderer.AdjacentEast = east;

            down?.AdjacentUp = renderer;
            up?.AdjacentDown = renderer;
            north?.AdjacentSouth = renderer;
            south?.AdjacentNorth = renderer;
            west?.AdjacentEast = renderer;
            east?.AdjacentWest = renderer;
        }
        else
        {
            down?.AdjacentUp = null;
            up?.AdjacentDown = null;
            north?.AdjacentSouth = null;
            south?.AdjacentNorth = null;
            west?.AdjacentEast = null;
            east?.AdjacentWest = null;
        }

        NoteGraphChanged(renderer);
        NoteGraphChanged(down);
        NoteGraphChanged(up);
        NoteGraphChanged(north);
        NoteGraphChanged(south);
        NoteGraphChanged(west);
        NoteGraphChanged(east);

        void NoteGraphChanged(SubChunkRenderer? changed)
        {
            if (changed != null && _sections.TryGetValue(changed.Position, out var state))
                state.NoteAdjacencyChanged(_frameIndex);
        }
    }

    /// <summary>
    ///     Rescues only near-field presentations whose portal result is not yet trustworthy. The
    ///     safety ring remains prepared and resident independently; a stable, complete graph is
    ///     subject to normal frustum and portal occlusion even inside that ring.
    /// </summary>
    private NearFieldRescueDiagnostics RescueUntrustedNearField(
        Vector3D<int> cameraChunkPos,
        int frame,
        ICuller camera)
    {
        _nearFieldRescuesThisFrame.Clear();
        var rescued = 0;
        var frustumTests = 0;
        var incompleteAdjacency = 0;
        var newPresentation = 0;
        var presentationRegression = 0;
        var oldestDuration = 0;
        var size = SubChunkRenderer.Size;
        for (var chunkX = -MeshSafetyRingRadius; chunkX <= MeshSafetyRingRadius; chunkX++)
        {
            for (var chunkZ = -MeshSafetyRingRadius; chunkZ <= MeshSafetyRingRadius; chunkZ++)
            {
                if (chunkX * chunkX + chunkZ * chunkZ > MeshSafetyRingRadius * MeshSafetyRingRadius)
                    continue;

                for (var y = 0; y < ChuckFormat.WorldHeight; y += size)
                {
                    var pos = new Vector3D<int>(
                        cameraChunkPos.X + chunkX * size,
                        y,
                        cameraChunkPos.Z + chunkZ * size);
                    if (TryGetResidentState(pos, out var state))
                    {
                        var renderer = state.Renderer!;
                        if (renderer.LastVisibleFrame == frame)
                        {
                            state.ClearNearFieldRescue();
                            continue;
                        }

                        var reasons = GetNearFieldRescueReasons(state, frame);
                        if (reasons == NearFieldRescueReason.None)
                        {
                            state.ClearNearFieldRescue();
                            continue;
                        }

                        frustumTests++;
                        if (!camera.IsBoundingBoxInFrustum(renderer.BoundingBox))
                        {
                            state.ClearNearFieldRescue();
                            continue;
                        }

                        renderer.LastVisibleFrame = frame;
                        Visit(renderer);
                        state.RecordNearFieldRescue(reasons, frame);
                        _nearFieldRescuesThisFrame.Add(state);
                        rescued++;
                        oldestDuration = Math.Max(oldestDuration, state.RescueDurationFrames);
                        if ((reasons & NearFieldRescueReason.IncompleteAdjacency) != 0) incompleteAdjacency++;
                        if ((reasons & NearFieldRescueReason.NewPresentation) != 0) newPresentation++;
                        if ((reasons & NearFieldRescueReason.PresentationRegression) != 0)
                        {
                            presentationRegression++;
                        }
                    }
                }
            }
        }

        foreach (var state in _activeNearFieldRescues)
        {
            if (!_nearFieldRescuesThisFrame.Contains(state)) state.ClearNearFieldRescue();
        }
        _activeNearFieldRescues.Clear();
        _activeNearFieldRescues.UnionWith(_nearFieldRescuesThisFrame);

        return new NearFieldRescueDiagnostics(
            rescued, frustumTests, incompleteAdjacency, newPresentation,
            presentationRegression, oldestDuration);
    }

    internal static NearFieldRescueReason GetNearFieldRescueReasons(SectionRenderState state, int frame)
    {
        var renderer = state.Renderer!;
        var reasons = NearFieldRescueReason.None;
        if (!HasCompleteAdjacency(renderer)) reasons |= NearFieldRescueReason.IncompleteAdjacency;
        if (IsWithinNearFieldGrace(state.PresentationInstalledFrame, frame))
            reasons |= NearFieldRescueReason.NewPresentation;
        if (renderer.LastVisibleFrame == frame - 1 &&
            IsWithinNearFieldGrace(state.AdjacencyChangedFrame, frame))
            reasons |= NearFieldRescueReason.PresentationRegression;
        return reasons;
    }

    private static bool IsWithinNearFieldGrace(int eventFrame, int frame) =>
        eventFrame >= 0 && frame >= eventFrame && frame - eventFrame < NearFieldGraphGraceFrames;

    internal static bool HasCompleteAdjacency(SubChunkRenderer renderer) =>
        (renderer.Position.Y == 0 || renderer.AdjacentDown != null) &&
        (renderer.Position.Y + SubChunkRenderer.Size >= ChuckFormat.WorldHeight || renderer.AdjacentUp != null) &&
        renderer.AdjacentNorth != null && renderer.AdjacentSouth != null &&
        renderer.AdjacentWest != null && renderer.AdjacentEast != null;

    /// <summary>
    ///     Tracks an actual near-field hole: source terrain that had a resident presentation but
    ///     no longer has one. Portal-occluded residents and successfully rescued graph transitions
    ///     are deliberately not regressions.
    /// </summary>
    private void RecordMissingNearFieldRegressions(Vector3D<int> cameraChunkPos, ICuller camera)
    {
        _currentPresentationRegressions.Clear();
        var size = SubChunkRenderer.Size;
        for (var chunkX = -MeshSafetyRingRadius; chunkX <= MeshSafetyRingRadius; chunkX++)
        for (var chunkZ = -MeshSafetyRingRadius; chunkZ <= MeshSafetyRingRadius; chunkZ++)
        {
            if (chunkX * chunkX + chunkZ * chunkZ > MeshSafetyRingRadius * MeshSafetyRingRadius)
                continue;

            for (var y = 0; y < ChuckFormat.WorldHeight; y += size)
            {
                var pos = new Vector3D<int>(
                    cameraChunkPos.X + chunkX * size,
                    y,
                    cameraChunkPos.Z + chunkZ * size);
                if (!_everPresentedMeshes.Contains(pos) ||
                    TryGetResidentState(pos, out _) ||
                    !HasRenderableSourceChunk(_world, pos)) continue;

                var bounds = new Box(pos.X - 6, pos.Y - 6, pos.Z - 6,
                    pos.X + size + 6, pos.Y + size + 6, pos.Z + size + 6);
                if (!camera.IsBoundingBoxInFrustum(bounds)) continue;

                _currentPresentationRegressions.Add(pos);
                if (_activePresentationRegressions.Add(pos)) _presentationRegressionCount++;
            }
        }

        _activePresentationRegressions.RemoveWhere(pos => !_currentPresentationRegressions.Contains(pos));
        foreach (var renderer in _visibleRenderers) _everPresentedMeshes.Add(renderer.Position);
    }

    private void FinalizePresentationProfile()
    {
        if (!_hasPreparedFrame)
        {
            _hasPreparedFrame = true;
            return;
        }

        _findVisibleTimings.Record(_findVisibleMsThisFrame);
        _spatialCullTimings.Record(_spatialQueryThisFrame.CullMs);
        _candidateSortTimings.Record(_spatialQueryThisFrame.SortMs);
        _portalTraversalTimings.Record(_portalTraversalMsThisFrame);
        _terrainSubmitTimings.Record(_terrainSubmitMsThisFrame);
        var draws = _solidDrawsThisFrame + _translucentDrawsThisFrame;
        _presentationProfile = new ChunkPresentationProfileSnapshot(
            ResidentMeshCount,
            _residentSolidLayersThisFrame,
            _residentTranslucentLayersThisFrame,
            _visibilityThisFrame.ResidentCandidates,
            _spatialQueryThisFrame.RegionTests,
            _spatialQueryThisFrame.ColumnTests,
            _spatialQueryThisFrame.SectionTests,
            _spatialQueryThisFrame.Candidates,
            _spatialQueryThisFrame.OutsideRenderDistance,
            _spatialQueryThisFrame.SortComparisons,
            _visibilityThisFrame.FrustumTests,
            _visibilityThisFrame.PortalVisited,
            _visibilityThisFrame.DisconnectedSeeds,
            _visibilityThisFrame.PortalQueuePops,
            _visibilityThisFrame.PortalDrawFrustumTests,
            _visibilityThisFrame.PortalEdgeAttempts,
            _visibilityThisFrame.PortalMissingNeighbors,
            _visibilityThisFrame.PortalMarginFrustumTests,
            _visibilityThisFrame.PortalMarginRejected,
            _visibilityThisFrame.PortalDuplicateReaches,
            _visibilityThisFrame.PortalSuccessfulReaches,
            _visibilityThisFrame.PortalMarginCacheHits,
            _safetyRescuedThisFrame,
            _incompleteAdjacencyRescuedThisFrame,
            _newPresentationRescuedThisFrame,
            _presentationRegressionRescuedThisFrame,
            _oldestSafetyRescueFramesThisFrame,
            _visibleRenderers.Count,
            _presentedSolidLayersThisFrame,
            _presentedTranslucentLayersThisFrame,
            Math.Max(0, _terrainUniformEntriesThisFrame - draws),
            draws,
            _availableQuadsThisFrame,
            _submittedQuadsThisFrame,
            _availableQuadsThisFrame - _submittedQuadsThisFrame,
            _directionDrawRangesThisFrame,
            _unassignedQuadsThisFrame,
            _terrainUniformEntriesThisFrame,
            _terrainSubmissionBatchesThisFrame,
            _terrainStreamBindsThisFrame,
            _terrainPipelineBindsThisFrame,
            _terrainTextureBindsThisFrame,
            _wgpuPipelines.Values.Sum(static pipeline => pipeline.DrawStorageCapacity) +
            _wgpuWireframePipelines.Values.Sum(static pipeline => pipeline.DrawStorageCapacity),
            _wgpuPipelines.Values.Sum(static pipeline => pipeline.DrawStorageGrowthCount) +
            _wgpuWireframePipelines.Values.Sum(static pipeline => pipeline.DrawStorageGrowthCount),
            _findVisibleTimings.Snapshot(),
            _spatialCullTimings.Snapshot(),
            _candidateSortTimings.Snapshot(),
            _portalTraversalTimings.Snapshot(),
            _terrainSubmitTimings.Snapshot());
    }

    /// <summary>
    ///     Returns the largest complete radial ring, or <see cref="int.MaxValue" /> once the whole
    ///     safety area is ready and ordinary render-distance visibility may resume.
    /// </summary>
    private int GetContiguousMeshRadius(int centerX, int centerZ, int requiredRadius)
    {
        for (var radius = 0; radius <= requiredRadius; radius++)
        {
            var radiusSquared = radius * radius;
            for (var dx = -radius; dx <= radius; dx++)
            for (var dz = -radius; dz <= radius; dz++)
            {
                if (dx * dx + dz * dz > radiusSquared || IsMeshColumnReady(centerX + dx, centerZ + dz))
                    continue;
                return radius - 1;
            }
        }

        return int.MaxValue;
    }

    internal bool IsMeshColumnReady(int chunkX, int chunkZ)
    {
        if (!_world.BlockHost.HasChunk(chunkX, chunkZ) ||
            !_world.BlockHost.GetChunk(chunkX, chunkZ).Loaded) return false;

        for (var y = 0; y < ChuckFormat.WorldHeight; y += SubChunkRenderer.Size)
        {
            if (!HasRenderer(new Vector3D<int>(
                    chunkX * SubChunkRenderer.Size, y, chunkZ * SubChunkRenderer.Size)))
                return false;
        }

        return true;
    }

    private int CountPending(MeshWorkPriority priority)
    {
        var count = 0;
        foreach (var section in _sections.Values)
        {
            if (section.Version.State.Pending != -1 && section.RequestedPriority == priority) count++;
        }

        return count;
    }

    private int CountDeferred(SectionDirtyReason reason)
    {
        var count = 0;
        foreach (var section in _sections.Values)
        {
            if ((section.DeferredDirtyReasons & reason) != 0) count++;
        }

        return count;
    }

    private int CountPendingWithReason(SectionDirtyReason reason)
    {
        var count = 0;
        foreach (var section in _sections.Values)
        {
            if (section.Version.State.Pending != -1 && (section.DirtyReasons & reason) != 0)
                count++;
        }

        return count;
    }

    private int CountPendingInHorizontalRing(MeshWorkPriority priority, int radius)
    {
        var count = 0;
        foreach (var section in _sections.Values)
        {
            if (section.Version.State.Pending != -1 &&
                section.RequestedPriority == priority &&
                IsInHorizontalMeshRing(section.Position, _lastViewPos, radius))
                count++;
        }

        return count;
    }

    private long OldestPendingAge(MeshWorkPriority priority)
    {
        var oldest = 0L;
        foreach (var section in _sections.Values)
        {
            if (section.RequestedPriority != priority ||
                section.RequestedAt < 0 ||
                section.Version.State.Pending == -1) continue;

            oldest = Math.Max(oldest, _schedulerTick - section.RequestedAt);
        }

        return oldest;
    }

    /// <summary>
    ///     Issues as many <see cref="ChunkMeshGenerator.MeshChunk" /> calls as fit in
    ///     <see cref="MeshDispatchBudgetMs" />, instead of the fixed one-dirty-plus-one-lighting
    ///     cap this used to have. That fixed cap throttled how fast a burst of newly-loaded chunks
    ///     could drain regardless of how fast the mesh workers or the snapshot copy underneath them
    ///     could go; a wall-clock budget lets it drain as fast as those actually allow, and still
    ///     bounds the frame-thread cost of dispatching regardless of how large the backlog gets.
    /// </summary>
    private void DispatchPendingMeshUpdates()
    {
        var stopwatch = Stopwatch.StartNew();
        var criticalDispatches = _criticalDispatchesSincePump;
        using (Profiler.Begin("SnapshotSubmit"))
        {
            while (true)
            {
                SectionRenderState section;
                if (stopwatch.Elapsed.TotalMilliseconds < MeshDispatchBudgetMs)
                {
                    if (!_pendingMeshUpdates.TryDequeue(out section)) break;
                }
                else if (criticalDispatches >= CriticalDispatchReserve ||
                         !_pendingMeshUpdates.TryDequeue(MeshWorkPriority.Critical, out section))
                {
                    break;
                }

                // Retention cleanup removes keyed entries when the camera domain changes. Keep
                // this dequeue-time guard for a request which crossed the boundary between ticks;
                // it costs O(dispatched work), never O(the whole backlog).
                if (section.IsDisposed) continue;
                if (!IsChunkInMeshPrepareDistance(section.Position, _lastViewPos))
                {
                    section.AbandonRequest(MeshCancellationReason.OutsideRetention);
                    continue;
                }

                if (section.RequestedPriority == MeshWorkPriority.Critical) criticalDispatches++;
                var pendingEpoch = section.Version.State.Pending;
                if (pendingEpoch == -1) continue;
                var estimate = _meshGenerator.EstimateCost(
                    section.RebuildPlan,
                    section.LastMeshResultBytesPerPage);
                if (!_meshGenerator.CanAdmitBuild(estimate, section.RequestedPriority))
                {
                    // Preserve the request and its deterministic rank. Work already running or
                    // waiting for upload must free estimated time/bytes before ordinary streaming
                    // expands the backlog further.
                    _pendingMeshUpdates.Enqueue(section, RankPendingMesh(section, _lastCamera));
                    break;
                }
                _meshGenerator.MeshChunk(
                    _world,
                    section.Position,
                    pendingEpoch,
                    _options.AlternateBlocksEnabled,
                    section.RequestedPriority,
                    section.PendingTrace,
                    section.LifetimeId,
                    section.RebuildPlan,
                    estimate);
            }
        }
        _criticalDispatchesSincePump = 0;
    }

    private (int Tier, int DeadlineFrame, double DistanceSquared, long EnqueuedAt) RankPendingMesh(
        SectionRenderState section,
        ICuller? camera)
    {
        var aabb = new Box(
            section.Position.X, section.Position.Y, section.Position.Z,
            section.Position.X + SubChunkRenderer.Size,
            section.Position.Y + SubChunkRenderer.Size,
            section.Position.Z + SubChunkRenderer.Size);
        var prefetched = camera?.IsBoundingBoxInFrustum(aabb.Expand(
            MeshPrefetchMargin, MeshPrefetchMargin, MeshPrefetchMargin)) ?? false;
        return GetMeshSchedulingRank(
            section.Position,
            _lastViewPos,
            _predictedViewPos,
            section.RequestedPriority,
            section.RequestedDeadlineFrame,
            prefetched,
            !IsChunkInRenderDistance(section.Position, _lastViewPos),
            section.RequestedAt,
            _schedulerTick);
    }

    internal static (int Tier, int DeadlineFrame, double DistanceSquared, long EnqueuedAt) GetMeshSchedulingRank(
        Vector3D<int> position,
        Vector3D<double> viewPosition,
        bool urgent,
        bool visible,
        long enqueuedAt,
        long schedulerTick) => GetMeshSchedulingRank(
        position, viewPosition, viewPosition,
        urgent ? MeshWorkPriority.Critical : MeshWorkPriority.Background,
        int.MaxValue,
        visible, false, enqueuedAt, schedulerTick);

    internal static (int Tier, int DeadlineFrame, double DistanceSquared, long EnqueuedAt) GetMeshSchedulingRank(
        Vector3D<int> position,
        Vector3D<double> viewPosition,
        MeshWorkPriority priority,
        bool visible,
        long enqueuedAt,
        long schedulerTick) => GetMeshSchedulingRank(
        position, viewPosition, viewPosition, priority, int.MaxValue,
        visible, false, enqueuedAt, schedulerTick);

    internal static (int Tier, int DeadlineFrame, double DistanceSquared, long EnqueuedAt) GetMeshSchedulingRank(
        Vector3D<int> position,
        Vector3D<double> viewPosition,
        Vector3D<double> predictedViewPosition,
        bool urgent,
        bool prefetched,
        bool speculative,
        long enqueuedAt,
        long schedulerTick) => GetMeshSchedulingRank(
        position, viewPosition, predictedViewPosition,
        urgent ? MeshWorkPriority.Critical : MeshWorkPriority.Background,
        int.MaxValue,
        prefetched, speculative, enqueuedAt, schedulerTick);

    private static (int Tier, int DeadlineFrame, double DistanceSquared, long EnqueuedAt) GetMeshSchedulingRank(
        Vector3D<int> position,
        Vector3D<double> viewPosition,
        Vector3D<double> predictedViewPosition,
        MeshWorkPriority priority,
        int deadlineFrame,
        bool prefetched,
        bool speculative,
        long enqueuedAt,
        long schedulerTick)
    {
        var baseTier = priority switch
        {
            MeshWorkPriority.Critical => 0,
            MeshWorkPriority.Foreground => 1,
            _ when IsInMeshSafetyRing(position, viewPosition) => 1,
            _ => speculative
                ? 4
                : prefetched
                    ? 2
                    : 3
        };
        var age = Math.Max(0, schedulerTick - enqueuedAt);
        // Old background work eventually competes with visible work. It never displaces the
        // permanent safety ring or urgent gameplay work.
        var minimumTier = baseTier <= 1 ? baseTier : baseTier == 4 ? 3 : 2;
        var promotedTier = Math.Max(minimumTier, baseTier - (int)(age / MeshAgePromotionTicks));
        var meshPosition = ToDoubleVec(position);
        var distance = Math.Min(
            Vector3D.DistanceSquared(meshPosition, viewPosition),
            Vector3D.DistanceSquared(meshPosition, predictedViewPosition));
        // Distance dominates inside a tier. Enqueue time is only a tie-breaker because age already
        // earns explicit tier promotion above. Comparing age first made an old frontier mesh beat
        // a newly discovered hole beside a moving player.
        return (promotedTier,
            priority == MeshWorkPriority.Critical && deadlineFrame >= 0 ? deadlineFrame : int.MaxValue,
            distance, enqueuedAt);
    }

    internal static bool IsInMeshSafetyRing(Vector3D<int> position, Vector3D<double> viewPosition)
        => IsInHorizontalMeshRing(position, viewPosition, MeshSafetyRingRadius);

    internal static bool IsInMeshForegroundRing(Vector3D<int> position, Vector3D<double> viewPosition)
        => IsInHorizontalMeshRing(position, viewPosition, MeshForegroundRingRadius);

    private static bool IsInHorizontalMeshRing(
        Vector3D<int> position,
        Vector3D<double> viewPosition,
        int radius)
    {
        var centerX = (int)Math.Floor(viewPosition.X / SubChunkRenderer.Size);
        var centerZ = (int)Math.Floor(viewPosition.Z / SubChunkRenderer.Size);
        var chunkX = position.X / SubChunkRenderer.Size;
        var chunkZ = position.Z / SubChunkRenderer.Size;
        var deltaX = chunkX - centerX;
        var deltaZ = chunkZ - centerZ;
        return deltaX * deltaX + deltaZ * deltaZ <= radius * radius;
    }

    public void UpdateAllRenderers()
    {
        foreach (var state in _residentSections)
        {
            var renderer = state.Renderer!;
            if (IsChunkInMeshPrepareDistance(renderer.Position, _lastViewPos) && state.IsLit)
                MarkLightDirty(renderer.Position);
        }
    }

    /// <summary>Coalesces a pure light invalidation without advancing the geometry epoch.</summary>
    public bool MarkLightDirty(Vector3D<int> sectionPosition)
    {
        if (!_sections.TryGetValue(sectionPosition, out var section) || section.Renderer == null)
            return false;
        _lightUpdateGenerations[sectionPosition] = ++_nextLightUpdateGeneration;
        section.RecordInvalidation(SectionDirtyReason.Lighting);
        if (!_pendingLightUpdateKeys.Add(sectionPosition)) return false;
        QueueLightEvaluation(sectionPosition);
        return true;
    }

    private void RefreshPendingLights()
    {
        var device = WebGpuDevice.Current;
        if (device == null) return;

        using (Profiler.Begin("Install"))
        {
            var completed = 0;
            var uploaded = 0;
            while (completed < LightCompletionLimitPerFrame &&
                   uploaded < LightUploadLimitPerFrame &&
                   _lightEvaluation.TryTakeCompleted(out var result) && result is not null)
            {
                completed++;
                _inFlightLightUpdateKeys.Remove(result.Position);
                if (result.Failure is not null)
                    throw new InvalidOperationException(
                        $"Section light evaluation failed for {result.Position}.", result.Failure);

                if (!_pendingLightUpdateKeys.Contains(result.Position)) continue;
                if (!_sections.TryGetValue(result.Position, out var section) ||
                    !section.OwnsResult(result.SectionId) ||
                    section.Renderer?.Presentation is not { } presentation)
                {
                    ClearLightEvaluation(result.Position);
                    continue;
                }

                if (!_lightUpdateGenerations.TryGetValue(result.Position, out var generation) ||
                    generation != result.Generation ||
                    presentation.Epoch != result.Evaluation.PresentationEpoch ||
                    !presentation.TryInstallLighting(device, result.Evaluation))
                {
                    QueueLightEvaluation(result.Position);
                    continue;
                }

                ClearLightEvaluation(result.Position);
                _lightRefreshCompletedCount++;
                _lightUploadsThisFrame++;
                uploaded++;
            }
        }

        using (Profiler.Begin("SnapshotDispatch"))
        {
            var dispatched = 0;
            while (dispatched < LightEvaluationDispatchLimitPerFrame &&
                   _lightEvaluation.HasCapacity &&
                   _pendingLightUpdates.TryDequeue(out var pos))
            {
                _queuedLightUpdateKeys.Remove(pos);
                if (!_pendingLightUpdateKeys.Contains(pos) || _inFlightLightUpdateKeys.Contains(pos))
                    continue;
                if (!_sections.TryGetValue(pos, out var section) ||
                    section.Renderer?.Presentation is not { } presentation ||
                    !_lightUpdateGenerations.TryGetValue(pos, out var generation))
                {
                    ClearLightEvaluation(pos);
                    continue;
                }

                var plan = presentation.CaptureLightingPlan();
                if (!plan.HasWork)
                {
                    ClearLightEvaluation(pos);
                    _lightRefreshCompletedCount++;
                    continue;
                }

                // Two cells cover the outward corner probe plus the neighbor-light lookup used by
                // slabs, farmland and stairs at a section boundary.
                const int padding = 2;
                var snapshot = new WorldRegionSnapshot(
                    _world,
                    pos.X - padding, pos.Y - padding, pos.Z - padding,
                    pos.X + SubChunkRenderer.Size - 1 + padding,
                    pos.Y + SubChunkRenderer.Size - 1 + padding,
                    pos.Z + SubChunkRenderer.Size - 1 + padding);
                var request = new SectionLightEvaluationRequest(
                    pos, section.LifetimeId, generation, plan, snapshot);
                if (!_lightEvaluation.TrySubmit(request))
                {
                    snapshot.Dispose();
                    QueueLightEvaluation(pos);
                    break;
                }

                _inFlightLightUpdateKeys.Add(pos);
                dispatched++;
            }
        }
    }

    private void QueueLightEvaluation(Vector3D<int> position)
    {
        if (!_pendingLightUpdateKeys.Contains(position) ||
            _inFlightLightUpdateKeys.Contains(position) ||
            !_queuedLightUpdateKeys.Add(position)) return;
        _pendingLightUpdates.Enqueue(position);
    }

    private void ClearLightEvaluation(Vector3D<int> position)
    {
        _pendingLightUpdateKeys.Remove(position);
        _queuedLightUpdateKeys.Remove(position);
        _inFlightLightUpdateKeys.Remove(position);
        _lightUpdateGenerations.Remove(position);
    }

    public void Tick(Vector3D<double> viewPos, Vector3D<double> velocity)
    {
        using var _chunkTick = Profiler.Begin("ChunkTick");

        _schedulerTick++;
        _lastViewPos = viewPos;
        _predictedViewPos = PredictMeshCenter(viewPos, velocity);

        var currentChunk = GetMeshDiscoveryCenter(viewPos);

        var crossedHorizontalChunk = _lastLeadingEdgeCenter is { } oldCenter &&
                                     (oldCenter.X != currentChunk.X || oldCenter.Z != currentChunk.Z);
        if (_lastLeadingEdgeCenter is { } previousCenter &&
            crossedHorizontalChunk &&
            Math.Abs(previousCenter.X - currentChunk.X) <= 1 &&
            Math.Abs(previousCenter.Z - currentChunk.Z) <= 1)
        {
            QueueLeadingEdge(previousCenter, currentChunk, _lastRenderDistance);
        }
        else if (crossedHorizontalChunk)
        {
            // A teleport or view-distance bootstrap must not materialize a full render disk in a
            // side queue. The ordinary bounded radial producer handles it from the new center.
            _leadingEdgeSections.Clear();
            _leadingEdgeSectionKeys.Clear();
        }
        _lastLeadingEdgeCenter = currentChunk;

        if (_lastRequestRankCenter != currentChunk || _schedulerTick % MeshAgePromotionTicks == 0)
        {
            _pendingMeshUpdates.Reprioritize(info => RankPendingMesh(info, _lastCamera));
            _lastRequestRankCenter = currentChunk;
        }

        // Requests were ordered for an earlier camera position when they entered the bounded
        // worker backlog. Re-sort the small queues as the player moves so a distant request cannot
        // remain ahead merely because it was discovered before a new nearby hole.
        _meshGenerator.Reprioritize(_lastViewPos, _predictedViewPos);

        var prepareRadius = _lastRenderDistance;
        var radiusSq = prepareRadius * prepareRadius;
        var enqueuedCount = 0;
        //TODO: MAKE THESE CONFIGURABLE
        const int MAX_CHUNKS_PER_FRAME = 32;
        const int PRIORITY_PASS_LIMIT = 1024;
        const int BACKGROUND_PASS_LIMIT = 2048;

        // The ordinary spherical discovery scan is deliberately bounded and skips positions that
        // already own version state. Initial loading has a stronger contract: every section that
        // can make the central 3x3 playable area ready must be discovered, and an existing background job
        // must be promoted instead of skipped. This also prevents a whole edge stripe from waiting
        // for the background cursor to wrap around the render distance.
        if (_world is ClientWorld clientWorld)
        {
            foreach (var required in clientWorld.NetworkHandler.Preload.RequiredMeshSections())
                PrioritizeMesh(required);

            if (_frameIndex % 300 == 0 && !clientWorld.NetworkHandler.Preload.IsReady)
                LogBlockingStartupMeshes(clientWorld);
        }

        // Reserve a small admission window for missing meshes around the player's current column.
        // The general backlog may already be full of work selected before the player moved. If the
        // safety ring shared that capacity, flying into a loaded-but-unmeshed area could not even
        // promote its nearby sections until distant work drained.
        var safetyForegroundPending = CountPendingInHorizontalRing(
            MeshWorkPriority.Foreground,
            MeshSafetyRingRadius);
        var safetyDiscoveryBudget = GetMeshSafetyDiscoveryCapacity(
            safetyForegroundPending,
            _meshGenerator.MaxConcurrentTasks);

        // Do not derive this traversal from the render-distance 3D spiral. At an aerial camera the
        // bottom sections are seven vertical steps from the clamped discovery center, and more
        // than PRIORITY_PASS_LIMIT unrelated spiral entries can sort ahead of them. Walking the
        // small safety set explicitly guarantees all 29 * 8 sections remain discoverable.
        AdmitMissingMeshes(currentChunk, s_safetyColumnOffsets, safetyDiscoveryBudget);

        // Fill a larger, circular preparation area without making it part of the loading-screen
        // contract. This foreground wave follows the player and predicted path; the strict radius
        // three pass above keeps its own reserved admission capacity and therefore cannot be
        // blocked by the larger ring.
        var foregroundDiscoveryBudget = Math.Min(
            MAX_CHUNKS_PER_FRAME,
            GetMeshForegroundDiscoveryCapacity(ForegroundPending, _meshGenerator.MaxConcurrentTasks));
        AdmitMissingMeshes(currentChunk, s_foregroundColumnOffsets, foregroundDiscoveryBudget);

        // Neighbor arrivals invalidate already-presented boundaries, but those cosmetic cleanup
        // builds must not bypass admission control. Keep only a small admitted window; the cheap,
        // keyed deferred queue retains the rest and repeated arrivals coalesce per section.
        var streamingBoundaryBudget = GetStreamingBoundaryAdmissionCapacity(
            CountPendingWithReason(SectionDirtyReason.StreamingBoundary),
            _meshGenerator.MaxConcurrentTasks);
        AdmitDeferredStreamingBoundaries(streamingBoundaryBudget);

        // A one-column crossing changes only a crescent at the preparation frontier. Admit that
        // exact leading edge before the general spiral. Entries are coalesced and stale ones are
        // discarded lazily, so continuous flight cannot grow a second unbounded work queue.
        var leadingEdgeBudget = Math.Min(
            MAX_CHUNKS_PER_FRAME,
            GetLeadingEdgeAdmissionCapacity(LeadingEdgePending, _meshGenerator.MaxConcurrentTasks));
        AdmitLeadingEdgeMeshes(leadingEdgeBudget);

        // Discovery is a producer and the mesh workers/uploads are the consumers. Letting the
        // producer run 32 sections every frame regardless of consumer progress grows a distance-32
        // world into a ten-thousand-entry dirty list, making the scheduler's priority scan itself
        // a frame stall. Keep only a small multiple of worker count buffered; urgent notifications
        // and startup prerequisites above are never rejected by this background limit.
        var pendingMeshWork = _pendingMeshUpdates.Count + _meshGenerator.Profile.Outstanding;
        var discoveryBudget = Math.Min(
            MAX_CHUNKS_PER_FRAME,
            GetMeshDiscoveryCapacity(pendingMeshWork, _meshGenerator.MaxConcurrentTasks));

        for (var i = 0; discoveryBudget > 0 && i < PRIORITY_PASS_LIMIT && i < s_spiralOffsets.Length; i++)
        {
            var offset = s_spiralOffsets[i];
            var distSq = offset.X * offset.X + offset.Y * offset.Y + offset.Z * offset.Z;

            if (distSq > radiusSq)
                break;

            var chunkPos = (currentChunk + offset) * SubChunkRenderer.Size;

            if (!IsValidWorldSectionY(chunkPos.Y))
                continue;

            if (HasRenderer(chunkPos))
                continue;

            RecoverOrphanedMesh(chunkPos);
            if (_sections.ContainsKey(chunkPos)) continue;

            if (MarkDirty(chunkPos))
            {
                enqueuedCount++;
            }

            if (enqueuedCount >= discoveryBudget)
                break;
        }

        // Keep advancing the discovery cursor whenever the near-camera pass leaves capacity.
        // A position can fail MarkDirty merely because its neighbor ring has not arrived yet. The
        // old "priority pass clean" gate treated that temporary hole as a reason to stop scanning,
        // so loaded sections beyond the fixed priority window could remain invisible indefinitely.
        if (enqueuedCount < discoveryBudget)
        {
            for (var i = 0; i < BACKGROUND_PASS_LIMIT; i++)
            {
                var offset = s_spiralOffsets[_currentIndex];
                var distSq = offset.X * offset.X + offset.Y * offset.Y + offset.Z * offset.Z;

                if (distSq <= radiusSq)
                {
                    var chunkPos = (currentChunk + offset) * SubChunkRenderer.Size;
                    // Match the priority pass above. The old background cursor could publish
                    // empty render sections above/below the finite world while the camera flew
                    // outside its height, wasting mesh work and violating fixed column slots.
                    if (!IsValidWorldSectionY(chunkPos.Y))
                    {
                        _currentIndex = (_currentIndex + 1) % s_spiralOffsets.Length;
                        continue;
                    }
                    if (!HasRenderer(chunkPos))
                    {
                        RecoverOrphanedMesh(chunkPos);
                        if (!_sections.ContainsKey(chunkPos) && MarkDirty(chunkPos))
                        {
                            enqueuedCount++;
                        }
                    }
                }

                _currentIndex = (_currentIndex + 1) % s_spiralOffsets.Length;

                if (enqueuedCount >= discoveryBudget)
                    break;
            }
        }

        using (Profiler.Begin("RemoveVersions"))
        {
            foreach (var section in _sections)
            {
                // Resident buffers are retired only by EndFrame, after their last command buffer
                // has been submitted. Keep non-resident state through the same retention boundary
                // so a build which started inside prepare can still install after a crossing; the
                // keyed removal below cancels work that never reached a worker.
                if (section.Value.Renderer is null &&
                    !IsChunkInMeshRetentionDistance(section.Key, _lastViewPos))
                {
                    _sectionsToRemove.Add(section.Key);
                }
            }

            foreach (var pos in _sectionsToRemove)
            {
                _pendingMeshUpdates.Remove(pos);
                if (_sections.Remove(pos, out var section)) section.Dispose();
            }

            _sectionsToRemove.Clear();
        }

        // The terrain-loading screen ticks the world renderer but does not necessarily open and
        // finish a world render pass. EndFrame is therefore not a reliable pump for startup mesh
        // work. Drain it here until the playable area is complete; normal gameplay keeps the
        // post-pass path so mesh replacement remains outside command recording.
        if (_world is ClientWorld loadingWorld && !loadingWorld.NetworkHandler.Preload.IsReady)
        {
            DispatchPendingMeshUpdates();
            LoadNewMeshes(_lastViewPos);
            RefreshPendingLights();
        }
    }

    internal static Vector3D<double> PredictMeshCenter(Vector3D<double> position, Vector3D<double> velocity) =>
        position + velocity * MeshPredictionTicks;

    internal static Vector3D<int> GetMeshDiscoveryCenter(Vector3D<double> viewPosition) => new(
        (int)Math.Floor(viewPosition.X / SubChunkRenderer.Size),
        Math.Clamp(
            (int)Math.Floor(viewPosition.Y / SubChunkRenderer.Size),
            0,
            (ChuckFormat.WorldHeight - 1) / SubChunkRenderer.Size),
        (int)Math.Floor(viewPosition.Z / SubChunkRenderer.Size));

    internal static bool IsValidWorldSectionY(int blockY) =>
        blockY >= 0 && blockY < ChuckFormat.WorldHeight &&
        blockY % SubChunkRenderer.Size == 0;

    public void MarkAllVisibleChunksDirty()
    {
        if (_lastRenderDistance <= 0) return;

        foreach (var pos in new List<Vector3D<int>>(_sections.Keys))
            MarkDirty(pos, SectionDirtyReason.Maintenance);
    }

    /// <summary>
    ///     The mesh bookkeeping for the sub-chunk containing a block, for the debug view.
    /// </summary>
    public bool TryGetMeshState(int blockX, int blockY, int blockZ, out (long Epoch, long LastMeshed, long Pending) state, out bool hasRenderer)
    {
        Vector3D<int> pos = new(
            (int)Math.Floor(blockX / (double)SubChunkRenderer.Size) * SubChunkRenderer.Size,
            (int)Math.Floor(blockY / (double)SubChunkRenderer.Size) * SubChunkRenderer.Size,
            (int)Math.Floor(blockZ / (double)SubChunkRenderer.Size) * SubChunkRenderer.Size);

        hasRenderer = HasRenderer(pos);

        if (_sections.TryGetValue(pos, out var section))
        {
            state = section.Version.State;
            return true;
        }

        state = default;
        return false;
    }

    public bool IsMeshCurrent(int blockX, int blockY, int blockZ) =>
        TryGetMeshState(blockX, blockY, blockZ, out var state, out var hasRenderer) &&
        hasRenderer && state.Pending == -1 && state.Epoch == state.LastMeshed;

    public long CriticalDeadlineMissesAt(int blockX, int blockY, int blockZ)
    {
        var pos = new Vector3D<int>(
            (int)Math.Floor(blockX / (double)SubChunkRenderer.Size) * SubChunkRenderer.Size,
            (int)Math.Floor(blockY / (double)SubChunkRenderer.Size) * SubChunkRenderer.Size,
            (int)Math.Floor(blockZ / (double)SubChunkRenderer.Size) * SubChunkRenderer.Size);
        return _meshLifecycle.CriticalDeadlineMissesAt(pos);
    }

    public bool MarkDirty(Vector3D<int> chunkPos, bool priority = false) =>
        MarkDirty(
            chunkPos,
            priority ? SectionDirtyReason.BlockChange : SectionDirtyReason.InitialTerrain);

    internal bool MarkDirty(
        Vector3D<int> chunkPos,
        bool priority,
        SectionMeshRebuildPlan rebuildPlan) =>
        MarkDirty(
            chunkPos,
            priority ? SectionDirtyReason.BlockChange : SectionDirtyReason.InitialTerrain,
            rebuildPlan);

    private bool MarkDirty(
        Vector3D<int> chunkPos,
        SectionDirtyReason reason,
        SectionMeshRebuildPlan rebuildPlan = default)
    {
        if (!IsChunkInMeshPrepareDistance(chunkPos, _lastViewPos))
            return false;

        // The snapshot needs one cell of neighbor padding, but it already reads a missing column
        // through ChunkSource's empty-chunk fallback. Requiring the whole neighbor ring here made
        // a fully received, interactive chunk invisible until every adjacent streaming placeholder
        // arrived. When a real neighbor is decoded, its expanded dirty range rebuilds this shared
        // boundary with the newly available faces and lighting.
        if (!HasRenderableSourceChunk(_world, chunkPos))
            return false;

        var hasRenderer = HasRenderer(chunkPos);
        var requiredForStartup = _world is ClientWorld startupWorld &&
                                 startupWorld.NetworkHandler.Preload.RequiresMesh(chunkPos);
        var hasPendingBuild = _sections.TryGetValue(chunkPos, out var existingSection) &&
                              existingSection.Version.State.Pending != -1;
        if (ShouldWaitForMissingMeshDiscovery(
                reason, hasRenderer, requiredForStartup, hasPendingBuild))
            return false;

        var section = GetOrCreateSection(chunkPos);
        var requestedAt = _schedulerTick;
        if (section.TryConsumeDeferredRequest(out var deferredReasons, out var deferredAt))
        {
            reason |= deferredReasons;
            if (deferredAt >= 0) requestedAt = Math.Min(requestedAt, deferredAt);
        }

        // Only an exact local block invalidation has dependency bounds precise enough for a page
        // rebuild. Initial terrain, lighting, streaming boundaries and maintenance all retain the
        // conservative full-section fallback. A missing presentation also needs all four pages.
        if (!hasRenderer || (reason & ~SectionDirtyReason.BlockChange) != 0)
            rebuildPlan = SectionMeshRebuildPlan.Full;
        else if (rebuildPlan.PageMask == 0)
            rebuildPlan = SectionMeshRebuildPlan.Full;

        // Full chunk arrival uses the same dirty notification as player edits. Only updates to
        // an existing mesh are critical. Startup meshes are foreground work: they beat ordinary
        // streaming without making a newly placed block wait behind the whole safety ring.
        var requestedPriority = ClassifyRequestedMeshPriority(
            reason,
            hasRenderer,
            requiredForStartup,
            IsInMeshSafetyRing(chunkPos, _lastViewPos),
            IsInMeshForegroundRing(chunkPos, _lastViewPos),
            section.Renderer is { LastVisibleFrame: > 0 } renderer &&
            renderer.LastVisibleFrame >= _frameIndex - 1);

        var version = section.Version;

        version.MarkDirty();
        var deadlineFrame = requestedPriority == MeshWorkPriority.Critical
            ? _frameIndex + CriticalDeadlineFramesFor(_averageFrameDurationMs)
            : -1;
        section.RememberRequest(reason, requestedPriority, requestedAt, deadlineFrame, rebuildPlan);
        section.RecordInvalidation(reason);

        var snapshot = version.SnapshotIfNeeded();
        if (snapshot.HasValue)
        {
            section.BeginTrace(snapshot.Value, _frameIndex);
            var estimate = _meshGenerator.EstimateCost(
                section.RebuildPlan, section.LastMeshResultBytesPerPage);
            if (requestedPriority == MeshWorkPriority.Critical &&
                _criticalDispatchesSincePump < CriticalDispatchReserve &&
                _meshGenerator.CanAdmitBuild(estimate, requestedPriority))
            {
                _criticalDispatchesSincePump++;
                _meshGenerator.MeshChunk(
                    _world, chunkPos, snapshot.Value, _options.AlternateBlocksEnabled,
                    requestedPriority, section.PendingTrace, section.LifetimeId, section.RebuildPlan,
                    estimate);
            }
            else
            {
                _pendingMeshUpdates.Enqueue(section, RankPendingMesh(section, _lastCamera));
            }
            return true;
        }

        section.PromoteTrace(section.RequestedPriority, section.RequestedDeadlineFrame);

        // A local request owns no world snapshot yet: advance that one keyed entry to the latest
        // desired epoch in place without building a result already known to be stale.
        if (_pendingMeshUpdates.Contains(chunkPos))
        {
            var latestEpoch = version.ReplaceQueuedSnapshotWithLatest();
            section.BeginTrace(latestEpoch, _frameIndex);
            _pendingMeshUpdates.Promote(chunkPos, RankPendingMesh(section, _lastCamera));
            return false;
        }


        // Once a critical revision has a snapshot, finishing it is normally cheaper than
        // repeatedly throwing away partial geometry. Further changes coalesce in Version.Epoch;
        // the completed coherent mesh is installed as intermediate progress and the latest epoch
        // is scheduled immediately afterwards.
        if (section.RequestedPriority == MeshWorkPriority.Critical &&
            _meshGenerator.HasOutstandingAtPriority(chunkPos, MeshWorkPriority.Critical))
            return false;

        if (_meshGenerator.CancelObsolete(chunkPos, section.RequestedPriority))
        {
            _meshLifecycle.Cancel(section.PendingTrace, MeshCancellationReason.Superseded);
            return false;
        }

        // No queue, worker, or completed-result slot owns the pending epoch. Recover it here for
        // resident meshes too; missing-mesh discovery has a separate orphan recovery path.
        version.AbandonPendingMesh();
        var recovered = version.SnapshotIfNeeded();
        if (recovered.HasValue)
        {
            section.BeginTrace(recovered.Value, _frameIndex);
            _pendingMeshUpdates.Enqueue(section, RankPendingMesh(section, _lastCamera));
        }

        return false;
    }

    /// <summary>
    ///     An update to terrain which has never been presented needs no replacement build: the
    ///     bounded radial discovery pass will eventually snapshot its latest authoritative state.
    ///     Eagerly snapshotting every network delta in an unmeshed distance-32 world bypasses all
    ///     scheduler admission limits and creates thousands of invisible background jobs.
    /// </summary>
    internal static bool ShouldWaitForMissingMeshDiscovery(
        SectionDirtyReason reason,
        bool hasRenderer,
        bool requiredForStartup,
        bool hasPendingBuild) =>
        !hasRenderer && !requiredForStartup && !hasPendingBuild &&
        (reason & (SectionDirtyReason.BlockChange | SectionDirtyReason.StreamingBoundary)) != 0;

    /// <summary>
    ///     Rebuilds an already presented boundary when an adjacent streamed chunk arrives, while
    ///     leaving brand-new, off-screen sections to the bounded radial discovery pass. Bulk chunk
    ///     arrival previously inserted every section in a 3x3-column region directly into the
    ///     dirty list, allowing distance 32 to create a ten-thousand-entry scheduler scan.
    /// </summary>
    internal void MarkStreamingDirty(Vector3D<int> chunkPos)
    {
        var requiredForStartup = _world is ClientWorld clientWorld &&
                                 clientWorld.NetworkHandler.Preload.RequiresMesh(chunkPos);
        if (!HasRenderer(chunkPos) && !requiredForStartup) return;

        // Startup has a small explicit contract and must not wait behind deferred maintenance.
        if (requiredForStartup)
        {
            if (!_sections.TryGetValue(chunkPos, out var startupSection) ||
                startupSection.Version.State.Pending == -1)
            {
                MarkDirty(chunkPos, SectionDirtyReason.StreamingBoundary);
                return;
            }

            // Do not invalidate the initial coherent snapshot while it is building. Remember one
            // boundary cleanup for after installation instead of making startup chase a moving
            // epoch as adjacent chunks arrive.
            startupSection.DeferRequest(SectionDirtyReason.StreamingBoundary, _schedulerTick);
            RequeueDeferredStreamingBoundary(chunkPos);
            return;
        }

        var section = GetOrCreateSection(chunkPos);
        section.DeferRequest(SectionDirtyReason.StreamingBoundary, _schedulerTick);
        RequeueDeferredStreamingBoundary(chunkPos);
    }

    internal static MeshWorkPriority ClassifyRequestedMeshPriority(
        SectionDirtyReason reason,
        bool hasRenderer,
        bool requiredForStartup,
        bool withinSafetyRing,
        bool withinForegroundRing,
        bool recentlyPresented = false)
    {
        // Residency is not visibility. Random ticks and fluids behind the camera must keep their
        // meshes current, but they do not share the presentation deadline of something the player
        // can presently see. The radial safety ring remains foreground work through discovery.
        var immediatePresentation = recentlyPresented;
        if ((reason & SectionDirtyReason.BlockChange) != 0 && hasRenderer && immediatePresentation)
            return MeshWorkPriority.Critical;
        if ((reason & SectionDirtyReason.Lighting) != 0 && hasRenderer && immediatePresentation)
            return MeshWorkPriority.Critical;
        if ((reason & SectionDirtyReason.StreamingBoundary) != 0 && hasRenderer && immediatePresentation)
            return MeshWorkPriority.Critical;
        if (requiredForStartup ||
            ((reason & SectionDirtyReason.InitialTerrain) != 0 && withinForegroundRing) ||
            ((reason & SectionDirtyReason.StreamingBoundary) != 0 && hasRenderer && withinSafetyRing))
            return MeshWorkPriority.Foreground;
        return MeshWorkPriority.Background;
    }

    internal static int CriticalDeadlineFramesFor(double averageFrameDurationMs)
    {
        var safeFrameDuration = double.IsFinite(averageFrameDurationMs)
            ? Math.Clamp(averageFrameDurationMs, 1.0, CriticalMeshDeadlineMs)
            : 1000.0 / 60.0;
        return Math.Max(
            MinimumCriticalMeshDeadlineFrames,
            (int)Math.Ceiling(CriticalMeshDeadlineMs / safeFrameDuration));
    }

    internal static MeshWorkPriority ClassifyRequestedMeshPriority(
        bool updateRequested,
        bool hasRenderer,
        bool requiredForStartup,
        bool withinSafetyRing = false) =>
        updateRequested && hasRenderer
            ? MeshWorkPriority.Critical
            : requiredForStartup || withinSafetyRing
                ? MeshWorkPriority.Foreground
                : MeshWorkPriority.Background;

    private static MeshWorkPriority MaxPriority(MeshWorkPriority left, MeshWorkPriority right) =>
        left >= right ? left : right;

    private MeshWorkPriority RequestedPriority(Vector3D<int> chunkPos) =>
        _sections.TryGetValue(chunkPos, out var section)
            ? section.RequestedPriority
            : MeshWorkPriority.Background;

    private SectionRenderState GetOrCreateSection(Vector3D<int> chunkPos)
    {
        if (_sections.TryGetValue(chunkPos, out var section)) return section;
        section = new SectionRenderState(chunkPos, _meshLifecycle);
        _sections.Add(chunkPos, section);
        return section;
    }

    private bool HasRenderer(Vector3D<int> chunkPos) =>
        _sections.TryGetValue(chunkPos, out var section) && section.Renderer is not null;

    private bool TryGetResidentState(Vector3D<int> chunkPos, out SectionRenderState state)
    {
        if (_sections.TryGetValue(chunkPos, out state!) && state.Renderer is not null) return true;
        state = null!;
        return false;
    }

    private void RememberPriority(Vector3D<int> chunkPos, MeshWorkPriority priority)
    {
        if (priority == MeshWorkPriority.Background) return;
        GetOrCreateSection(chunkPos).RememberRequest(
            SectionDirtyReason.InitialTerrain,
            priority,
            _schedulerTick);
    }

    /// <summary>
    ///     Drops version state that claims a missing renderer has work pending when no local,
    ///     worker, or result queue actually owns that work. Discovery can then schedule it again.
    /// </summary>
    private void RecoverOrphanedMesh(Vector3D<int> chunkPos)
    {
        if (!_sections.TryGetValue(chunkPos, out var section)) return;
        if (_pendingMeshUpdates.Contains(chunkPos) ||
            _meshGenerator.HasOutstanding(chunkPos))
            return;

        section.AbandonRequest();
        if (_sections.Remove(chunkPos, out section)) section.Dispose(MeshCancellationReason.Orphaned);
    }

    private int AdmitMissingMeshes(
        Vector3D<int> center,
        IReadOnlyList<Vector2D<int>> columnOffsets,
        int budget)
    {
        var admitted = 0;
        for (var columnIndex = 0; columnIndex < columnOffsets.Count;)
        {
            if (admitted >= budget) break;
            var firstOffset = columnOffsets[columnIndex++];
            var hasOpposite = firstOffset != Vector2D<int>.Zero &&
                              columnIndex < columnOffsets.Count &&
                              columnOffsets[columnIndex] == -firstOffset;
            var oppositeOffset = hasOpposite
                ? columnOffsets[columnIndex++]
                : Vector2D<int>.Zero;

            for (var verticalDistance = 0;
                 verticalDistance < ChuckFormat.WorldHeight / SubChunkRenderer.Size;
                 verticalDistance++)
            {
                // Submit matching vertical sections from both sides together. Processing a whole
                // column first filled every worker with one half of each pair and merely flipped
                // the old left/right bias instead of removing it.
                TrySection(firstOffset, center.Y - verticalDistance);
                if (hasOpposite) TrySection(oppositeOffset, center.Y - verticalDistance);
                if (admitted >= budget) break;
                if (verticalDistance != 0)
                {
                    TrySection(firstOffset, center.Y + verticalDistance);
                    if (hasOpposite) TrySection(oppositeOffset, center.Y + verticalDistance);
                }
                if (admitted >= budget) break;
            }
        }

        return admitted;

        void TrySection(Vector2D<int> offset, int sectionY)
        {
            if (sectionY < 0 || sectionY >= ChuckFormat.WorldHeight / SubChunkRenderer.Size)
                return;
            var chunkPos = new Vector3D<int>(
                center.X + offset.X,
                sectionY,
                center.Z + offset.Y) * SubChunkRenderer.Size;
            if (HasRenderer(chunkPos)) return;
            RecoverOrphanedMesh(chunkPos);
            if (PrioritizeMissingMesh(chunkPos)) admitted++;
        }
    }

    internal static Vector2D<int>[] GetLeadingEdgeColumns(
        Vector3D<int> previousCenter,
        Vector3D<int> currentCenter,
        int prepareRadius)
    {
        var stepX = Math.Sign(currentCenter.X - previousCenter.X);
        var stepZ = Math.Sign(currentCenter.Z - previousCenter.Z);
        if (prepareRadius <= 0 || stepX == 0 && stepZ == 0) return [];

        // The disk one crossing ahead includes the current disk's newly entered crescent plus a
        // small predicted cap. Subtracting the previous disk is what makes this incremental.
        var futureX = currentCenter.X + stepX;
        var futureZ = currentCenter.Z + stepZ;
        var radiusSq = prepareRadius * prepareRadius;
        var capRadius = prepareRadius + MeshSpeculativeRadius;
        var capRadiusSq = capRadius * capRadius;
        var columns = new List<Vector2D<int>>();
        for (var x = futureX - prepareRadius; x <= futureX + prepareRadius; x++)
        for (var z = futureZ - prepareRadius; z <= futureZ + prepareRadius; z++)
        {
            var futureDx = x - futureX;
            var futureDz = z - futureZ;
            if (futureDx * futureDx + futureDz * futureDz > radiusSq) continue;
            var currentDx = x - currentCenter.X;
            var currentDz = z - currentCenter.Z;
            if (currentDx * currentDx + currentDz * currentDz > capRadiusSq) continue;
            var oldDx = x - previousCenter.X;
            var oldDz = z - previousCenter.Z;
            if (oldDx * oldDx + oldDz * oldDz <= radiusSq) continue;
            columns.Add(new Vector2D<int>(x, z));
        }

        columns.Sort((left, right) =>
        {
            // Most forward first; equal projections stay radially balanced across the path.
            var projection = ((long)right.X * stepX + (long)right.Y * stepZ)
                .CompareTo((long)left.X * stepX + (long)left.Y * stepZ);
            if (projection != 0) return projection;
            var leftOffset = new Vector2D<int>(left.X - currentCenter.X, left.Y - currentCenter.Z);
            var rightOffset = new Vector2D<int>(right.X - currentCenter.X, right.Y - currentCenter.Z);
            return CompareBalancedHorizontalOffsets(leftOffset, rightOffset);
        });
        return [.. columns];
    }

    private void QueueLeadingEdge(Vector3D<int> previousCenter, Vector3D<int> currentCenter, int prepareRadius)
    {
        _leadingEdgeSections.Clear();
        _leadingEdgeSectionKeys.Clear();
        foreach (var column in GetLeadingEdgeColumns(previousCenter, currentCenter, prepareRadius))
        {
            for (var verticalDistance = 0;
                 verticalDistance < ChuckFormat.WorldHeight / SubChunkRenderer.Size;
                 verticalDistance++)
            {
                Enqueue(currentCenter.Y - verticalDistance);
                if (verticalDistance != 0) Enqueue(currentCenter.Y + verticalDistance);
            }

            void Enqueue(int sectionY)
            {
                if (sectionY < 0 || sectionY >= ChuckFormat.WorldHeight / SubChunkRenderer.Size) return;
                var pos = new Vector3D<int>(column.X, sectionY, column.Y) * SubChunkRenderer.Size;
                if (_leadingEdgeSectionKeys.Add(pos)) _leadingEdgeSections.Enqueue(pos);
            }
        }
    }

    private int AdmitLeadingEdgeMeshes(int budget)
    {
        var admitted = 0;
        var attempts = Math.Min(_leadingEdgeSections.Count, MeshLeadingEdgeInspectionPerTick);
        while (admitted < budget && attempts-- > 0 && _leadingEdgeSections.TryDequeue(out var pos))
        {
            _leadingEdgeSectionKeys.Remove(pos);
            if (!IsChunkInMeshPrepareDistance(pos, _lastViewPos) || HasRenderer(pos)) continue;
            if (!HasRenderableSourceChunk(_world, pos))
            {
                if (_leadingEdgeSectionKeys.Add(pos)) _leadingEdgeSections.Enqueue(pos);
                continue;
            }

            RecoverOrphanedMesh(pos);
            if (_sections.ContainsKey(pos)) continue;
            if (MarkDirty(pos, SectionDirtyReason.LeadingEdge)) admitted++;
        }

        return admitted;
    }

    private int AdmitDeferredStreamingBoundaries(int budget)
    {
        var admitted = 0;
        // Inspect each currently queued key at most once. Entries whose section already has a
        // queued/running build go to the tail and wait for that coherent snapshot to install.
        // Stale keys are discarded lazily, keeping producer-side invalidation O(1).
        var attempts = _deferredStreamingBoundaries.Count;
        while (admitted < budget && attempts-- > 0 &&
               _deferredStreamingBoundaries.TryDequeue(out var chunkPos))
        {
            _deferredStreamingBoundaryKeys.Remove(chunkPos);
            if (!_sections.TryGetValue(chunkPos, out var section) ||
                (section.DeferredDirtyReasons & SectionDirtyReason.StreamingBoundary) == 0)
                continue;

            if (!IsChunkInMeshPrepareDistance(chunkPos, _lastViewPos) ||
                !HasRenderableSourceChunk(_world, chunkPos))
            {
                section.TryConsumeDeferredRequest(out _, out _);
                continue;
            }

            if (section.Version.State.Pending != -1)
            {
                RequeueDeferredStreamingBoundary(chunkPos);
                continue;
            }

            // MarkDirty folds all deferred reasons and their original age into this one epoch.
            if (MarkDirty(chunkPos, SectionDirtyReason.None)) admitted++;
        }

        return admitted;
    }

    private void RequeueDeferredStreamingBoundary(Vector3D<int> chunkPos)
    {
        if (_deferredStreamingBoundaryKeys.Add(chunkPos))
            _deferredStreamingBoundaries.Enqueue(chunkPos);
    }

    private void PrioritizeMesh(Vector3D<int> chunkPos)
    {
        if (HasRenderer(chunkPos))
            return;

        // Do not consume the one-shot orphan recovery while this is still a network placeholder.
        // The startup loop calls us again every frame; once the full chunk blob marks it Loaded,
        // the retry can create a real snapshot instead of being lost on MarkDirty's source guard.
        if (!HasRenderableSourceChunk(_world, chunkPos))
            return;

        if (_sections.TryGetValue(chunkPos, out var section))
        {
            var version = section.Version;
            RememberPriority(chunkPos, MeshWorkPriority.Foreground);

            if (_pendingMeshUpdates.Contains(chunkPos))
            {
                _pendingMeshUpdates.Promote(
                    chunkPos,
                    RankPendingMesh(section, _lastCamera));
                return;
            }

            if (version.State.Pending != -1)
            {
                // A queued job can be promoted; an already-running job cannot, but remains the
                // authoritative pending build and will be handled when its result arrives.
                _meshGenerator.Promote(chunkPos, MeshWorkPriority.Foreground);
                return;
            }

            // Version state can outlive the request that created it (for example when an early
            // result was discarded). With no renderer and no pending epoch, this is an actual
            // orphan, so enqueue a fresh foreground build. MarkDirty immediately records a pending
            // epoch, preventing the next frame from duplicating it.
            MarkDirty(chunkPos, SectionDirtyReason.InitialTerrain);

            return;
        }

        MarkDirty(chunkPos, SectionDirtyReason.InitialTerrain);
    }

    /// <summary>
    ///     Admits or promotes one missing safety-ring mesh without changing the content epoch of
    ///     work already in flight. Calling MarkDirty repeatedly here makes every worker result
    ///     stale before it can upload, which is a self-sustaining invisible-hole loop.
    /// </summary>
    private bool PrioritizeMissingMesh(Vector3D<int> chunkPos)
    {
        if (HasRenderer(chunkPos) || !HasRenderableSourceChunk(_world, chunkPos))
            return false;

        var previousPriority = RequestedPriority(chunkPos);
        if (_sections.TryGetValue(chunkPos, out var section))
        {
            var version = section.Version;
            RememberPriority(chunkPos, MeshWorkPriority.Foreground);

            if (_pendingMeshUpdates.Contains(chunkPos))
            {
                _pendingMeshUpdates.Promote(
                    chunkPos,
                    RankPendingMesh(section, _lastCamera));
                return previousPriority < MeshWorkPriority.Foreground;
            }

            if (version.State.Pending != -1)
            {
                _meshGenerator.Promote(chunkPos, MeshWorkPriority.Foreground);
                return previousPriority < MeshWorkPriority.Foreground;
            }
        }

        MarkDirty(chunkPos, SectionDirtyReason.InitialTerrain);
        return previousPriority < MeshWorkPriority.Foreground &&
               RequestedPriority(chunkPos) >= MeshWorkPriority.Foreground;
    }

    private void LogBlockingStartupMeshes(ClientWorld clientWorld)
    {
        foreach (var pos in clientWorld.NetworkHandler.Preload.RequiredMeshSections())
        {
            var state = _sections.TryGetValue(pos, out var section)
                ? section.Version.State.ToString()
                : "none";
            _logger.LogInformation(
                "Blocking startup mesh {Pos}: source={Source}, version={Version}, dirty={Dirty}, " +
                "renderer={Renderer}, priority={Priority}",
                pos,
                HasRenderableSourceChunk(_world, pos),
                state,
                _pendingMeshUpdates.Contains(pos),
                HasRenderer(pos),
                RequestedPriority(pos));
        }
    }

    internal static bool HasRenderableSourceChunk(World world, Vector3D<int> sectionPos)
    {
        var chunkX = sectionPos.X >> 4;
        var chunkZ = sectionPos.Z >> 4;
        return world.BlockHost.HasChunk(chunkX, chunkZ) && world.BlockHost.GetChunk(chunkX, chunkZ).Loaded;
    }


    private bool IsChunkInRenderDistance(Vector3D<int> chunkWorldPos, Vector3D<double> viewPos)
    {
        var chunkX = chunkWorldPos.X / SubChunkRenderer.Size;
        var chunkZ = chunkWorldPos.Z / SubChunkRenderer.Size;

        var viewChunkX = (int)Math.Floor(viewPos.X / SubChunkRenderer.Size);
        var viewChunkZ = (int)Math.Floor(viewPos.Z / SubChunkRenderer.Size);

        var dx = chunkX - viewChunkX;
        var dz = chunkZ - viewChunkZ;
        return dx * dx + dz * dz <= _lastRenderDistance * _lastRenderDistance;
    }

    private bool IsChunkInMeshPrepareDistance(Vector3D<int> chunkWorldPos, Vector3D<double> viewPos) =>
        IsChunkInRenderDistance(chunkWorldPos, viewPos) ||
        IsSpeculativePrefetchChunk(chunkWorldPos, viewPos, _predictedViewPos, _lastRenderDistance);

    private bool IsChunkInMeshRetentionDistance(Vector3D<int> chunkWorldPos, Vector3D<double> viewPos) =>
        IsInHorizontalChunkRadius(chunkWorldPos, viewPos, _lastRenderDistance + MeshRetentionMargin);

    internal static bool IsInHorizontalChunkRadius(
        Vector3D<int> chunkWorldPos, Vector3D<double> viewPos, int radius)
    {
        var chunkX = chunkWorldPos.X / SubChunkRenderer.Size;
        var chunkZ = chunkWorldPos.Z / SubChunkRenderer.Size;
        var viewChunkX = (int)Math.Floor(viewPos.X / SubChunkRenderer.Size);
        var viewChunkZ = (int)Math.Floor(viewPos.Z / SubChunkRenderer.Size);
        var dx = chunkX - viewChunkX;
        var dz = chunkZ - viewChunkZ;
        return dx * dx + dz * dz <= radius * radius;
    }

    internal static bool IsSpeculativePrefetchChunk(
        Vector3D<int> chunkWorldPos,
        Vector3D<double> viewPos,
        Vector3D<double> predictedViewPos,
        int renderDistance)
    {
        var chunkX = chunkWorldPos.X / SubChunkRenderer.Size;
        var chunkZ = chunkWorldPos.Z / SubChunkRenderer.Size;
        var viewChunkX = (int)Math.Floor(viewPos.X / SubChunkRenderer.Size);
        var viewChunkZ = (int)Math.Floor(viewPos.Z / SubChunkRenderer.Size);
        var predictedChunkX = predictedViewPos.X / SubChunkRenderer.Size;
        var predictedChunkZ = predictedViewPos.Z / SubChunkRenderer.Size;
        var dx = chunkX - viewChunkX;
        var dz = chunkZ - viewChunkZ;
        var currentDistanceSq = dx * dx + dz * dz;
        if (currentDistanceSq <= renderDistance * renderDistance)
            return false;

        var preparationCap = renderDistance + MeshSpeculativeRadius;
        if (currentDistanceSq > preparationCap * preparationCap)
            return false;

        var predictedDx = chunkX - predictedChunkX;
        var predictedDz = chunkZ - predictedChunkZ;
        return predictedDx * predictedDx + predictedDz * predictedDz <= renderDistance * renderDistance;
    }

    public void GetMeshSizeStats(out int minSize, out int maxSize, out int avgSize, out Dictionary<int, int> buckets)
    {
        var curMin = int.MaxValue;
        var curMax = 0;
        long totalSize = 0;
        var count = 0;
        var b = new Dictionary<int, int>();

        foreach (var state in _residentSections)
        {
            var renderer = state.Renderer!;

            void AddSize(int size)
            {
                if (size == 0) return;
                if (size < curMin) curMin = size;
                if (size > curMax) curMax = size;
                totalSize += size;
                count++;

                var sizeKb = (int)Math.Ceiling(size / 1024.0);
                if (sizeKb <= 0) sizeKb = 1;
                var po2 = 1;
                while (po2 < sizeKb) po2 *= 2;

                if (!b.TryGetValue(po2, out var val))
                    val = 0;
                b[po2] = val + 1;
            }

            AddSize(renderer.SolidMeshSizeBytes);
            AddSize(renderer.TranslucentMeshSizeBytes);
        }

        minSize = count == 0 ? 0 : curMin;
        maxSize = curMax;
        avgSize = count > 0 ? (int)(totalSize / count) : 0;
        buckets = b;
    }

    private static Vector3D<double> ToDoubleVec(Vector3D<int> vec) => new(vec.X, vec.Y, vec.Z);

    /// <summary>
    ///     The pass the terrain is recorded into and the array its layers index, or false when the
    ///     frame has neither yet.
    /// </summary>
    /// <remarks>
    ///     Both come from the draw target rather than being passed in, because the terrain draw is
    ///     reached through the same shared frame the rest of the world is: whoever opened the pass
    ///     handed it to the target, and the array is restated there each frame since a pack switch
    ///     replaces it outright. There is no array until a pack has been read into one, and a draw
    ///     then would sample nothing at all.
    /// </remarks>
    private unsafe bool TryGetWebGpuFrame(out RenderPassEncoder* pass, out WgpuTextureArray textureArray)
    {
        pass = null;
        textureArray = null!;

        if (RenderSystem.DrawTargetOrNull is not WebGpuDrawTarget target) return false;
        if (target.CurrentPass is null || target.TerrainArray is not { } array) return false;

        pass = target.CurrentPass;
        textureArray = array;
        return true;
    }

    private WgpuPipeline WgpuPipelineFor(RenderState state)
    {
        if (_wgpuPipelines.TryGetValue(state, out var cached)) return cached;

        var pipeline = CreateWgpuPipeline(WebGpuDevice.Current!, state);
        _wgpuPipelines[state] = pipeline;
        return pipeline;
    }

    private WgpuPipeline WgpuWireframePipelineFor(RenderState state)
    {
        if (_wgpuWireframePipelines.TryGetValue(state, out var cached)) return cached;

        var pipeline = CreateWgpuPipeline(WebGpuDevice.Current!, state,
            PrimitiveTopology.LineList, "fs_wireframe");
        _wgpuWireframePipelines[state] = pipeline;
        return pipeline;
    }

    /// <summary>The chunk.wgsl pipeline for one raster state, matching the chunk vertex layout.</summary>
    internal static unsafe WgpuPipeline CreateWgpuPipeline(WebGpuDevice device, RenderState state,
        PrimitiveTopology topology = PrimitiveTopology.TriangleList, string fragmentEntryPoint = "fs_main")
    {
        var source = AssetManager.Instance.GetAsset("shaders/chunk.wgsl").GetTextContent();

        var attrs = stackalloc VertexAttribute[4];
        attrs[0] = new VertexAttribute
        {
            Format = VertexFormat.Sint16x4,
            Offset = 0,
            ShaderLocation = 0
        };
        attrs[1] = new VertexAttribute
        {
            Format = VertexFormat.Uint16x2,
            Offset = 12,
            ShaderLocation = 1
        };
        attrs[2] = new VertexAttribute
        {
            Format = VertexFormat.Unorm8x4,
            Offset = 8,
            ShaderLocation = 2
        };
        attrs[3] = new VertexAttribute
        {
            Format = VertexFormat.Uint8x2,
            Offset = 16,
            ShaderLocation = 4
        };

        var lightAttr = stackalloc VertexAttribute[1];
        lightAttr[0] = new VertexAttribute
        {
            Format = VertexFormat.Uint8x2,
            Offset = 0,
            ShaderLocation = 3
        };

        var bufferLayouts = stackalloc VertexBufferLayout[2];
        bufferLayouts[0] = new VertexBufferLayout
        {
            ArrayStride = 20,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 4,
            Attributes = attrs
        };
        bufferLayouts[1] = new VertexBufferLayout
        {
            ArrayStride = WgpuMesh.ChunkLightVertexStride,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 1,
            Attributes = lightAttr
        };

        BindGroupLayoutEntry[] uniformEntries =
        [
            new()
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    MinBindingSize = ChunkFrameUniformSize,
                    HasDynamicOffset = false
                }
            }
        ];

        BindGroupLayoutEntry[] drawMetadataEntries =
        [
            new()
            {
                Binding = 0,
                Visibility = ShaderStage.Vertex,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.ReadOnlyStorage,
                    MinBindingSize = ChunkDrawMetadataSize
                }
            }
        ];

        BindGroupLayoutEntry[] texEntries =
        [
            new()
            {
                Binding = 0,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.Dimension2DArray
                }
            },
            new()
            {
                Binding = 1,
                Visibility = ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout
                {
                    Type = SamplerBindingType.Filtering
                }
            }
        ];

        return new WgpuPipeline(
            device, source, "vs_main",
            ChunkFrameUniformSize,
            uniformEntries,
            texEntries,
            bufferLayouts, 2,
            state,
            device.SurfaceFormat,
            TextureFormat.Depth32float,
            topology,
            textureArrayEntries: drawMetadataEntries,
            fragmentEntryPoint: fragmentEntryPoint);
    }

    /// <summary>
    ///     Draws solid-pass chunks through the native WebGPU command encoder.
    ///     Binds the terrain pipeline and texture array once, then iterates chunks.
    /// </summary>
    private unsafe void RenderSolidWebGpu(
        RenderPassEncoder* pass, WgpuPipeline pipeline, WgpuTextureArray textureArray)
    {
        var count = _solidRenderers.Count;
        if (count == 0) return;

        pipeline.Bind(pass);
        _terrainPipelineBindsThisFrame++;
        pipeline.UploadUniforms(BuildChunkFrameUniforms());
        pipeline.BindUniformGroup(pass);
        // Asked of the array per pass rather than held: a texture-pack switch rebuilds the array
        // underneath, and a bind group made against the old one points at a destroyed texture.
        WgpuPipeline.BindGroup(pass, 1,
            textureArray.BindGroupFor(pipeline.TextureBindGroupLayout), WebGpuDevice.Current!.Api);
        _terrainTextureBindsThisFrame++;

        // The same set the GL pass draws, chosen by PrepareFrame — which the caller is responsible
        // for having run, since the view matrices this reads come off the stacks there too.
        //
        // Two passes instead of one write and bind per draw: profiling found per-section uniform
        // submission was ~10ms of the frame. The first loop builds a tightly packed storage array;
        // the second selects each record with firstInstance, so group 2 stays bound for the pass.
        _terrainUniformEntriesThisFrame += count;
        if (_solidUniformScratch.Length < count)
        {
            _solidUniformScratch = new ChunkDrawMetadata[count];
        }

        for (var i = 0; i < count; i++)
        {
            var renderer = _solidRenderers[i];
            var fadeProgress = Math.Clamp(renderer.Age / SubChunkRenderer.FadeDuration, 0.0f, 1.0f);

            var camRel = new Vector3D<double>(
                renderer.PositionMinus.X - _lastViewPos.X,
                renderer.PositionMinus.Y - _lastViewPos.Y,
                renderer.PositionMinus.Z - _lastViewPos.Z);
            camRel += new Vector3D<double>(renderer.ClipPosition.X, renderer.ClipPosition.Y, renderer.ClipPosition.Z);

            var translation = Matrix4X4.CreateTranslation(
                new Vector3D<float>((float)camRel.X, (float)camRel.Y, (float)camRel.Z));
            var modelView = translation * _modelView;

            _solidUniformScratch[i] = BuildChunkDrawMetadata(
                modelView, renderer.Position, fadeProgress, translucent: false);
        }

        var t0 = Stopwatch.GetTimestamp();
        pipeline.WriteDrawStorage(_solidUniformScratch.AsSpan(0, count));
        pipeline.BindDrawStorage(pass);
        _terrainSubmissionBatchesThisFrame++;
        var t1 = Stopwatch.GetTimestamp();
        Profiler.Record("UniformUpload", (t1 - t0) * 1000.0 / Stopwatch.Frequency);

        var streamBinding = new TerrainStreamBindingState();
        for (var i = 0; i < count; i++)
        {
            var stats = _solidRenderers[i].RenderWebGpu(
                pass, 0, _lastViewPos, ref streamBinding, (uint)i);
            RecordDirectionalDraw(stats);
            _solidDrawsThisFrame += stats.DrawRanges;
        }

        var t2 = Stopwatch.GetTimestamp();
        Profiler.Record("DrawCall", (t2 - t1) * 1000.0 / Stopwatch.Frequency);
    }

    /// <summary>
    ///     Draws the solid pass as flat-green triangle edges instead of textured terrain, under
    ///     <see cref="WireframeEnabled" />. Same chunk set, transforms and uniforms as
    ///     <see cref="RenderSolidWebGpu" /> — the ordinary vertex streams are drawn through the
    ///     line pipeline and the device-wide quad-wireframe indices.
    /// </summary>
    private unsafe void RenderWireframeWebGpu(
        RenderPassEncoder* pass, WgpuPipeline pipeline, WgpuTextureArray textureArray)
    {
        if (_solidRenderers.Count == 0) return;

        pipeline.Bind(pass);
        _terrainPipelineBindsThisFrame++;
        pipeline.UploadUniforms(BuildChunkFrameUniforms());
        pipeline.BindUniformGroup(pass);
        WgpuPipeline.BindGroup(pass, 1,
            textureArray.BindGroupFor(pipeline.TextureBindGroupLayout), WebGpuDevice.Current!.Api);
        _terrainTextureBindsThisFrame++;

        var count = _solidRenderers.Count;
        _terrainUniformEntriesThisFrame += count;
        if (_solidUniformScratch.Length < count)
            _solidUniformScratch = new ChunkDrawMetadata[count];

        for (var i = 0; i < count; i++)
        {
            var renderer = _solidRenderers[i];
            var fadeProgress = Math.Clamp(renderer.Age / SubChunkRenderer.FadeDuration, 0.0f, 1.0f);
            var camRel = new Vector3D<double>(
                renderer.PositionMinus.X - _lastViewPos.X,
                renderer.PositionMinus.Y - _lastViewPos.Y,
                renderer.PositionMinus.Z - _lastViewPos.Z);
            camRel += new Vector3D<double>(renderer.ClipPosition.X, renderer.ClipPosition.Y, renderer.ClipPosition.Z);
            var translation = Matrix4X4.CreateTranslation(
                new Vector3D<float>((float)camRel.X, (float)camRel.Y, (float)camRel.Z));
            _solidUniformScratch[i] = BuildChunkDrawMetadata(
                translation * _modelView, renderer.Position, fadeProgress,
                translucent: false, applyHandoff: false);
        }

        pipeline.WriteDrawStorage(_solidUniformScratch.AsSpan(0, count));
        pipeline.BindDrawStorage(pass);
        _terrainSubmissionBatchesThisFrame++;

        var streamBinding = new TerrainStreamBindingState();
        for (var i = 0; i < count; i++)
        {
            var draws = _solidRenderers[i].RenderWireframeWebGpu(
                pass, ref streamBinding, out var streamBinds, (uint)i);
            _solidDrawsThisFrame += draws;
            _terrainStreamBindsThisFrame += streamBinds;
        }
    }

    /// <summary>WebGPU translucent pass — sorted back-to-front, same as the GL path.</summary>
    private unsafe void RenderTranslucentWebGpu(
        RenderPassEncoder* pass, WgpuPipeline pipeline, WgpuTextureArray textureArray,
        Vector3D<double> viewPos)
    {
        var count = _translucentRenderers.Count;
        if (count == 0) return;

        pipeline.Bind(pass);
        _terrainPipelineBindsThisFrame++;
        pipeline.UploadUniforms(BuildChunkFrameUniforms());
        pipeline.BindUniformGroup(pass);
        // Asked of the array per pass rather than held: a texture-pack switch rebuilds the array
        // underneath, and a bind group made against the old one points at a destroyed texture.
        WgpuPipeline.BindGroup(pass, 1,
            textureArray.BindGroupFor(pipeline.TextureBindGroupLayout), WebGpuDevice.Current!.Api);
        _terrainTextureBindsThisFrame++;

        _translucentDistanceComparer.Origin = viewPos;
        _translucentRenderers.Sort(_translucentDistanceComparer);

        _terrainUniformEntriesThisFrame += count;
        if (_translucentUniformScratch.Length < count)
            _translucentUniformScratch = new ChunkDrawMetadata[count];

        for (var i = 0; i < count; i++)
        {
            var renderer = _translucentRenderers[i];
            var fadeProgress = Math.Clamp(renderer.Age / SubChunkRenderer.FadeDuration, 0.0f, 1.0f);

            var camRel = new Vector3D<double>(
                renderer.PositionMinus.X - viewPos.X,
                renderer.PositionMinus.Y - viewPos.Y,
                renderer.PositionMinus.Z - viewPos.Z);
            camRel += new Vector3D<double>(renderer.ClipPosition.X, renderer.ClipPosition.Y, renderer.ClipPosition.Z);

            var translation = Matrix4X4.CreateTranslation(
                new Vector3D<float>((float)camRel.X, (float)camRel.Y, (float)camRel.Z));
            var modelView = translation * _modelView;

            _translucentUniformScratch[i] = BuildChunkDrawMetadata(
                modelView, renderer.Position, fadeProgress, translucent: true);
        }

        pipeline.WriteDrawStorage(_translucentUniformScratch.AsSpan(0, count));
        pipeline.BindDrawStorage(pass);
        _terrainSubmissionBatchesThisFrame++;

        var streamBinding = new TerrainStreamBindingState();
        for (var i = 0; i < count; i++)
        {
            var stats = _translucentRenderers[i].RenderWebGpu(
                pass, 1, viewPos, ref streamBinding, (uint)i);
            RecordDirectionalDraw(stats);
            _translucentDrawsThisFrame += stats.DrawRanges;
        }

        _translucentRenderers.Clear();
    }

    private void RecordDirectionalDraw(in DirectionalDrawStats stats)
    {
        _availableQuadsThisFrame += stats.AvailableQuads;
        _submittedQuadsThisFrame += stats.SubmittedQuads;
        _directionDrawRangesThisFrame += stats.DrawRanges;
        _unassignedQuadsThisFrame += stats.UnassignedQuads;
        _terrainStreamBindsThisFrame += stats.StreamBinds;
    }

    /// <summary>
    ///     Builds the indexed record selected by a terrain draw's first-instance value. Frame-wide
    ///     projection, fog and lighting deliberately do not appear here.
    /// </summary>
    private ChunkDrawMetadata BuildChunkDrawMetadata(
        Matrix4X4<float> modelView,
        Vector3D<int> chunkPos,
        float fadeProgress,
        bool translucent,
        bool applyHandoff = true)
    {
        var handoff = applyHandoff
            ? PresentationHandoff?.GetNearHandoff(
                chunkPos.X >> 4, chunkPos.Z >> 4, translucent) ?? TerrainNearHandoff.Inactive
            : TerrainNearHandoff.Inactive;

        return new ChunkDrawMetadata
        {
            ModelViewMatrix = modelView,
            ChunkPosX = chunkPos.X,
            ChunkPosY = chunkPos.Z,
            FadeProgress = handoff.Active ? handoff.Progress : fadeProgress,
            ChunkFadeEnabled = handoff.Active ? 0u : 1u,
            PresentationFadeMode = handoff.Active ? 1u : 0u,
            PresentationFadeSeed = handoff.Seed
        };
    }

    private ChunkFrameUniforms BuildChunkFrameUniforms()
    {
        var fog = _terrainFog;
        var light = RenderSystem.WorldLight;
        return new ChunkFrameUniforms
        {
            ProjectionMatrix = WgpuClip.FromGl(_projection),
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

    public void Dispose()
    {
        _meshGenerator.Dispose();
        _lightEvaluation.Dispose();

        foreach (var state in _sections.Values) state.Dispose(MeshCancellationReason.RendererDisposed);
        _terrainGpuArenas?.Dispose();
        _terrainGpuArenas = null;

        foreach (var pipeline in _wgpuPipelines.Values)
        {
            pipeline.Dispose();
        }

        _wgpuPipelines.Clear();

        foreach (var pipeline in _wgpuWireframePipelines.Values)
        {
            pipeline.Dispose();
        }

        _wgpuWireframePipelines.Clear();

        _sections.Clear();
        _residentSections.Clear();
        _residentSpatialIndex.Clear();

        _solidRenderers.Clear();
        _spatialCandidates.Clear();
        _translucentRenderers.Clear();
        _renderersToRemove.Clear();
        _pendingMeshUpdates.Clear();
        _pendingLightUpdates.Clear();
        _pendingLightUpdateKeys.Clear();
        _queuedLightUpdateKeys.Clear();
        _inFlightLightUpdateKeys.Clear();
        _lightUpdateGenerations.Clear();
        _deferredStreamingBoundaries.Clear();
        _deferredStreamingBoundaryKeys.Clear();
        _sectionsToRemove.Clear();
        _activePresentationRegressions.Clear();
        _currentPresentationRegressions.Clear();
        _everPresentedMeshes.Clear();
        _activeNearFieldRescues.Clear();
        _nearFieldRescuesThisFrame.Clear();
        _evictionGraceSections.Clear();
        _evictionGraceToCancel.Clear();
        _evictionDeadlines.Clear();
        _outsideRetentionRenderers.Clear();

    }

    private readonly record struct SectionEvictionCandidate(
        SectionRenderState State,
        long SectionId,
        int StartedFrame);

    private sealed class TranslucentDistanceComparer : IComparer<SubChunkRenderer>
    {
        public Vector3D<double> Origin;

        public int Compare(SubChunkRenderer? a, SubChunkRenderer? b)
        {
            if (a == null || b == null) return 0;
            var distA = Vector3D.DistanceSquared(ToDoubleVec(a.Position), Origin);
            var distB = Vector3D.DistanceSquared(ToDoubleVec(b.Position), Origin);
            return distB.CompareTo(distA); // descending
        }
    }
}

internal readonly record struct MeshSafetyRingState(
    int LoadedColumns,
    int ExpectedSections,
    int MissingMeshes);

internal readonly record struct NearFieldRescueDiagnostics(
    int Rescued,
    int FrustumTests,
    int IncompleteAdjacency,
    int NewPresentation,
    int PresentationRegression,
    int OldestDurationFrames);

/// <summary>
///     Per-draw storage record consumed by chunk.wgsl. Frame-wide projection, light and fog state
///     deliberately live in <see cref="ChunkFrameUniforms" /> so this repeated block stays small.
///     WGSL's default alignment rules (mat4x4 = 16, vec3 = 16, vec4 = 16, f32/u32 = 4).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 96)]
public struct ChunkDrawMetadata
{
    // mat4x4<f32> modelViewMatrix at offset 0
    [FieldOffset(0)] public Matrix4X4<float> ModelViewMatrix;

    // vec2<f32> chunkPos at offset 64 (align 8, size 8)
    [FieldOffset(64)] public float ChunkPosX;
    [FieldOffset(68)] public float ChunkPosY;

    [FieldOffset(72)] public float FadeProgress;
    [FieldOffset(76)] public uint ChunkFadeEnabled;
    [FieldOffset(80)] public uint PresentationFadeMode;
    [FieldOffset(84)] public uint PresentationFadeSeed;
}

/// <summary>Frame/pass-wide half of chunk.wgsl's terrain uniforms.</summary>
[StructLayout(LayoutKind.Explicit, Size = 240)]
public struct ChunkFrameUniforms
{
    [FieldOffset(0)] public Matrix4X4<float> ProjectionMatrix;

    // vec3<f32> time at offset 64 (align 16, size 12)
    [FieldOffset(64)] public float TimeX;
    [FieldOffset(68)] public float TimeY;
    [FieldOffset(72)] public float TimeZ;

    [FieldOffset(76)] public float AmbientDarkness;

    [FieldOffset(80)] public float LuminanceOffset;

    [FieldOffset(84)] public float WavyLeavesStrength;

    [FieldOffset(88)] public float WavyLeavesSpeed;

    [FieldOffset(92)] public float WavyPlantStrength;

    [FieldOffset(96)] public float WavyPlantSpeed;

    [FieldOffset(100)] public uint WavyPlantMode;

    // vec4<u32> wavyLeafLayers0 at offset 192 (align 16)
    [FieldOffset(112)] public uint WavyLeafLayer0;
    [FieldOffset(116)] public uint WavyLeafLayer1;
    [FieldOffset(120)] public uint WavyLeafLayer2;
    [FieldOffset(124)] public uint WavyLeafLayer3;

    // vec4<u32> wavyLeafLayers1 at offset 208
    [FieldOffset(128)] public uint WavyLeafLayer4;
    [FieldOffset(132)] public uint WavyLeafLayer5;
    [FieldOffset(136)] public uint WavyLeafLayer6;
    [FieldOffset(140)] public uint WavyLeafLayer7;

    // u32 wavyLeafCount at offset 224
    [FieldOffset(144)] public uint WavyLeafCount;

    // vec4<u32> wavyPlantLayers0 at offset 240 (align 16)
    [FieldOffset(160)] public uint WavyPlantLayer0;
    [FieldOffset(164)] public uint WavyPlantLayer1;
    [FieldOffset(168)] public uint WavyPlantLayer2;
    [FieldOffset(172)] public uint WavyPlantLayer3;

    // vec4<u32> wavyPlantLayers1 at offset 256
    [FieldOffset(176)] public uint WavyPlantLayer4;
    [FieldOffset(180)] public uint WavyPlantLayer5;
    [FieldOffset(184)] public uint WavyPlantLayer6;
    [FieldOffset(188)] public uint WavyPlantLayer7;

    // u32 wavyPlantCount at offset 272
    [FieldOffset(192)] public uint WavyPlantCount;

    // vec4<f32> fogColor at offset 288 (align 16)
    [FieldOffset(208)] public float FogColorR;
    [FieldOffset(212)] public float FogColorG;
    [FieldOffset(216)] public float FogColorB;
    [FieldOffset(220)] public float FogColorA;

    // f32 fogStart at offset 304
    [FieldOffset(224)] public float FogStart;

    // f32 fogEnd at offset 308
    [FieldOffset(228)] public float FogEnd;

    // f32 fogDensity at offset 312
    [FieldOffset(232)] public float FogDensity;

    // u32 fogMode at offset 316
    [FieldOffset(236)] public uint FogMode;
}
