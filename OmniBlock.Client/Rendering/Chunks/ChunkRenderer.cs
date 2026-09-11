using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using OmniBlock.Client.Options;
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
    internal const int MeshDiscoveryBacklogPerWorker = 8;
    internal const int MeshSafetyBacklogPerWorker = 2;
    internal const int MeshForegroundBacklogPerWorker = 4;

    //TODO: MAKE THIS CONFIGURABLE
    private const double MeshUploadBudgetMs = 1.5;

    //TODO: MAKE THIS CONFIGURABLE
    private const double MeshDispatchBudgetMs = 1.5;

    /// <summary>Bytes of <see cref="ChunkUniforms" />, as chunk.wgsl declares the block.</summary>
    private const uint ChunkUniformSize = 336;

    private static readonly Vector3D<int>[] s_spiralOffsets;
    private static readonly Vector2D<int>[] s_safetyColumnOffsets;
    private static readonly Vector2D<int>[] s_foregroundColumnOffsets;
    private readonly HashSet<Vector3D<int>> _everPresentedMeshes = [];
    private readonly ILogger<ChunkRenderer> _logger = Log.Instance.For<ChunkRenderer>();
    private readonly ChunkMeshGenerator _meshGenerator;
    private readonly List<SubChunkRenderer> _occludedRenderersBuffer = [];
    private readonly ChunkOcclusionCuller _occlusionCuller = new();
    private readonly GameOptions _options;
    private readonly Dictionary<Vector3D<int>, SectionRenderState> _sections = [];
    // Iteration index only. SectionRenderState remains the residency authority; excluding
    // scheduling-only states keeps per-frame culling independent of background queue size.
    private readonly HashSet<SectionRenderState> _residentSections = [];
    private readonly List<Vector3D<int>> _sectionsToRemove = [];
    private readonly List<SubChunkRenderer> _renderersToRemove = [];
    private readonly HashSet<Vector3D<int>> _activePresentationRegressions = [];
    private readonly HashSet<Vector3D<int>> _currentPresentationRegressions = [];
    private readonly HashSet<Vector3D<int>> _presentedThisFrame = [];
    private readonly SectionMeshRequestQueue _pendingMeshUpdates = new();
    private readonly TranslucentDistanceComparer _translucentDistanceComparer = new();
    private readonly List<SubChunkRenderer> _translucentRenderers = [];
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
    private int _currentIndex;
    private int _frameIndex;
    private ICuller? _lastCamera;
    private int _lastRenderDistance;
    private Vector3D<int>? _lastRequestRankCenter;
    private Vector3D<double> _lastViewPos;
    private int _meshReadyRadius = int.MaxValue;
    private Matrix4X4<float> _modelView;
    private Vector3D<double> _predictedViewPos;
    private Matrix4X4<float> _projection;
    private long _presentationRegressionCount;
    private long _schedulerTick;

    /// <summary>
    ///     Reused across frames so the solid pass's per-chunk uniform batch (see
    ///     <see cref="RenderSolidWebGpu" />) doesn't allocate one every frame — grown, never shrunk.
    /// </summary>
    private ChunkUniforms[] _solidUniformScratch = [];

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
        _meshGenerator = new ChunkMeshGenerator((ushort)GetMeshWorkerCount(Environment.ProcessorCount));
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
    internal long OldestForegroundAge => OldestPendingAge(MeshWorkPriority.Foreground);
    internal long PresentationRegressionCount => _presentationRegressionCount;

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
        text.Append("oldestForegroundAge\t").Append(OldestForegroundAge).AppendLine();
        text.Append("presentationRegressions\t").Append(PresentationRegressionCount).AppendLine();
        text.Append("pendingWork\t").Append(PendingMeshWork).AppendLine();
        text.AppendLine("chunkX\tchunkZ\tdistance2\tloaded\tmeshes\tvisible\tpending\tdirty\tforeground\tcritical\tbackground\tinitial\tstreamingBoundary\tblockChange\tlighting\tmaintenance\tyoungestMeshAge");

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
            var streamingBoundary = 0;
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
                    if ((section.DirtyReasons & SectionDirtyReason.StreamingBoundary) != 0) streamingBoundary++;
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
                .Append(initial).Append('\t').Append(streamingBoundary).Append('\t')
                .Append(blockChange).Append('\t').Append(lighting).Append('\t').Append(maintenance).Append('\t')
                .Append(youngestMeshAge == long.MaxValue ? -1 : youngestMeshAge)
                .AppendLine();
        }

        return text.ToString();
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
        _lastRenderDistance = renderParams.RenderDistance;
        _lastViewPos = renderParams.ViewPos;
        _lastCamera = renderParams.Camera;

        _modelView = renderParams.ModelView;
        _projection = renderParams.Projection;

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

        using (Profiler.Begin("FindVisible"))
        {
            ChunksInFrustum = _occlusionCuller.FindVisible(
                this,
                ResidentRenderers(),
                cameraState?.Renderer,
                renderParams.ViewPos,
                renderParams.Camera,
                renderDistWorld,
                UseOcclusionCulling,
                _frameIndex
            );
        }

        AddOcclusionSafetyRing(cameraChunkPos, _frameIndex, renderParams.Camera);

        var visitedVisibleCount = _visibleRenderers.Count;
        ChunksOccluded = ChunksInFrustum - visitedVisibleCount;
        ChunksRendered = visitedVisibleCount;

        if (renderParams.RenderOccluded)
        {
            _occludedRenderersBuffer.Clear();
            foreach (var state in _residentSections)
            {
                var renderer = state.Renderer!;
                if (renderer.LastVisibleFrame != _frameIndex)
                {
                    if (renderer.IsVisible(renderParams.Camera, renderParams.ViewPos, renderDistWorld))
                    {
                        _occludedRenderersBuffer.Add(renderer);
                    }
                }
            }

            _visibleRenderers.Clear();
            _visibleRenderers.AddRange(_occludedRenderersBuffer);
            ChunksRendered = _visibleRenderers.Count;
        }

        RecordPresentationState(cameraChunkPos, renderParams.Camera);

        var translucentCount = 0;
        foreach (var renderer in _visibleRenderers)
        {
            renderer.Update(renderParams.DeltaTime);

            if (renderer.HasTranslucentMesh)
            {
                translucentCount++;
                _translucentRenderers.Add(renderer);
            }
        }

        TranslucentMeshes = translucentCount;
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
        if (_lastCamera is not { } camera) return;

        foreach (var state in _residentSections)
        {
            var renderer = state.Renderer!;
            if (!IsChunkInMeshRetentionDistance(renderer.Position, _lastViewPos))
            {
                _renderersToRemove.Add(renderer);
            }
        }

        foreach (var renderer in _renderersToRemove)
        {
            UpdateAdjacency(renderer, false);
            if (_sections.Remove(renderer.Position, out var section))
            {
                _residentSections.Remove(section);
                section.DetachRenderer();
                section.Dispose();
            }
            renderer.Dispose();
        }

        _renderersToRemove.Clear();

        DispatchPendingMeshUpdates();
        LoadNewMeshes(_lastViewPos);
    }

    public unsafe void Render(ChunkRenderParams renderParams)
    {
        PrepareFrame(renderParams);

        if (TryGetWebGpuFrame(out var pass, out var array))
        {
            using (Profiler.Begin("DrawChunks"))
            {
                if (WireframeEnabled)
                {
                    RenderWireframeWebGpu(pass, WgpuWireframePipelineFor(RenderSystem.State.Current), array);
                }
                else
                {
                    RenderSolidWebGpu(pass, WgpuPipelineFor(RenderSystem.State.Current), array);
                }
            }
        }

        // No EndFrame here: it destroys mesh buffers, and the draws recorded above have not been
        // submitted yet. The WebGPU renderer calls it once the frame is presented.
    }

    public unsafe void RenderTransparent(ChunkRenderParams renderParams)
    {
        if (TryGetWebGpuFrame(out var pass, out var array))
        {
            using (Profiler.Begin("DrawChunksTranslucent"))
            {
                RenderTranslucentWebGpu(pass, WgpuPipelineFor(RenderSystem.State.Current), array,
                    renderParams.ViewPos);
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
        while (stopwatch.Elapsed.TotalMilliseconds < MeshUploadBudgetMs)
        {
            if (!_meshGenerator.TryDequeueMesh(out var mesh)) break;
            var uploadStart = Stopwatch.GetTimestamp();

            if (IsChunkInMeshRetentionDistance(mesh.Pos, viewPos))
            {
                var section = GetOrCreateSection(mesh.Pos);
                var version = section.Version;

                version.CompleteMesh(mesh.Version);

                if (version.IsStale(mesh.Version))
                {
                    var snapshot = version.SnapshotIfNeeded();
                    if (snapshot.HasValue)
                    {
                        var priority = MaxPriority(mesh.Priority, RequestedPriority(mesh.Pos));
                        _meshGenerator.MeshChunk(_world, mesh.Pos, snapshot.Value, _options.AlternateBlocksEnabled, priority);
                    }

                    // Superseded by the requeue above (or by whichever in-flight build already
                    // owns the next version) — this copy of the vertices is never uploaded, so
                    // nothing else will return it to the pool.
                    mesh.Dispose();
                    continue;
                }

                if (section.Renderer is { } existingRenderer)
                {
                    existingRenderer.UploadMeshData(mesh.Solid, mesh.Translucent);
                    section.IsLit = mesh.IsLit;
                    existingRenderer.VisibilityData = mesh.VisibilityData;
                }
                else
                {
                    var renderer = new SubChunkRenderer(mesh.Pos);
                    renderer.UploadMeshData(mesh.Solid, mesh.Translucent);
                    renderer.VisibilityData = mesh.VisibilityData;
                    section.Install(renderer, mesh.IsLit);
                    _residentSections.Add(section);
                    UpdateAdjacency(renderer, true);
                }

                section.RecordUploaded(_schedulerTick);
                section.ClearRequest();
                if (_world is ClientWorld clientWorld)
                    clientWorld.NetworkHandler.NotifyMeshUploaded(mesh.Pos);
            }
            else
            {
                // Finished after the chunk fell out of render distance — UploadMeshData (which
                // would normally return these to the pool) never runs for it.
                mesh.Dispose();
                if (_sections.TryGetValue(mesh.Pos, out var section)) section.ClearRequest();
            }

            var uploadedAt = Stopwatch.GetTimestamp();
            _meshGenerator.RecordUpload(
                uploadedAt - uploadStart,
                uploadedAt - mesh.FinishedAt,
                uploadedAt - mesh.RequestedAt);
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
    }

    /// <summary>
    ///     Conservatively draws completed meshes in the radial safety ring when they are inside
    ///     the camera frustum. Portal-style chunk occlusion is an optimization, not an authority on
    ///     whether nearby loaded terrain exists; a temporarily incomplete adjacency graph must not
    ///     turn collision-bearing terrain invisible until the player enters its immediate section.
    /// </summary>
    private void AddOcclusionSafetyRing(Vector3D<int> cameraChunkPos, int frame, ICuller camera)
    {
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
                        if (state.Renderer!.LastVisibleFrame != frame)
                        {
                            state.Renderer.LastVisibleFrame = frame;
                            if (camera.IsBoundingBoxInFrustum(state.Renderer.BoundingBox))
                            {
                                Visit(state.Renderer);
                            }
                        }
                    }
                }
            }
        }
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

    private bool IsMeshColumnReady(int chunkX, int chunkZ)
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
    ///     Records only conservative near-field regressions. A normal frustum exit or a distant
    ///     occlusion is not a regression; a loaded mesh that was presented before, lies inside the
    ///     safety ring and current frustum, but is absent from this frame's presentation set is.
    /// </summary>
    private void RecordPresentationState(Vector3D<int> cameraChunkPos, ICuller camera)
    {
        _presentedThisFrame.Clear();
        foreach (var renderer in _visibleRenderers) _presentedThisFrame.Add(renderer.Position);

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
                    !HasRenderableSourceChunk(_world, pos) ||
                    _presentedThisFrame.Contains(pos)) continue;

                var bounds = TryGetResidentState(pos, out var state)
                    ? state.Renderer!.BoundingBox
                    : new Box(pos.X - 6, pos.Y - 6, pos.Z - 6,
                        pos.X + size + 6, pos.Y + size + 6, pos.Z + size + 6);
                if (!camera.IsBoundingBoxInFrustum(bounds)) continue;

                _currentPresentationRegressions.Add(pos);
                if (_activePresentationRegressions.Add(pos)) _presentationRegressionCount++;
            }
        }

        _activePresentationRegressions.RemoveWhere(pos => !_currentPresentationRegressions.Contains(pos));
        _everPresentedMeshes.UnionWith(_presentedThisFrame);
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
        _pendingMeshUpdates.RemoveWhere(state =>
            !IsChunkInMeshRetentionDistance(state.Position, _lastViewPos));

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed.TotalMilliseconds < MeshDispatchBudgetMs)
        {
            if (!_pendingMeshUpdates.TryDequeue(out var section)) break;
            var pendingEpoch = section.Version.State.Pending;
            if (pendingEpoch == -1) continue;
            _meshGenerator.MeshChunk(
                _world,
                section.Position,
                pendingEpoch,
                _options.AlternateBlocksEnabled,
                section.RequestedPriority);
        }
    }

    private (int Tier, double DistanceSquared, long EnqueuedAt) RankPendingMesh(
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
            prefetched,
            !IsChunkInRenderDistance(section.Position, _lastViewPos),
            section.RequestedAt,
            _schedulerTick);
    }

    internal static (int Tier, double DistanceSquared, long EnqueuedAt) GetMeshSchedulingRank(
        Vector3D<int> position,
        Vector3D<double> viewPosition,
        bool urgent,
        bool visible,
        long enqueuedAt,
        long schedulerTick) => GetMeshSchedulingRank(
        position, viewPosition, viewPosition,
        urgent ? MeshWorkPriority.Critical : MeshWorkPriority.Background,
        visible, false, enqueuedAt, schedulerTick);

    internal static (int Tier, double DistanceSquared, long EnqueuedAt) GetMeshSchedulingRank(
        Vector3D<int> position,
        Vector3D<double> viewPosition,
        MeshWorkPriority priority,
        bool visible,
        long enqueuedAt,
        long schedulerTick) => GetMeshSchedulingRank(
        position, viewPosition, viewPosition, priority, visible, false, enqueuedAt, schedulerTick);

    internal static (int Tier, double DistanceSquared, long EnqueuedAt) GetMeshSchedulingRank(
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
        prefetched, speculative, enqueuedAt, schedulerTick);

    private static (int Tier, double DistanceSquared, long EnqueuedAt) GetMeshSchedulingRank(
        Vector3D<int> position,
        Vector3D<double> viewPosition,
        Vector3D<double> predictedViewPosition,
        MeshWorkPriority priority,
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
        return (promotedTier, distance, enqueuedAt);
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
            if (IsChunkInMeshRetentionDistance(renderer.Position, _lastViewPos) && state.IsLit)
            {
                var version = state.Version;
                version.MarkDirty();
                state.RememberRequest(
                    SectionDirtyReason.Lighting,
                    MeshWorkPriority.Background,
                    _schedulerTick);

                var snapshot = version.SnapshotIfNeeded();
                if (snapshot.HasValue)
                {
                    _pendingMeshUpdates.Enqueue(state, RankPendingMesh(state, _lastCamera));
                }
            }
        }
    }

    public void Tick(Vector3D<double> viewPos, Vector3D<double> velocity)
    {
        using var _chunkTick = Profiler.Begin("ChunkTick");

        _schedulerTick++;
        _lastViewPos = viewPos;
        _predictedViewPos = PredictMeshCenter(viewPos, velocity);

        var currentChunk = GetMeshDiscoveryCenter(viewPos);

        if (_lastRequestRankCenter != currentChunk || _schedulerTick % MeshAgePromotionTicks == 0)
        {
            _pendingMeshUpdates.Reprioritize(info => RankPendingMesh(info, _lastCamera));
            _lastRequestRankCenter = currentChunk;
        }

        // Requests were ordered for an earlier camera position when they entered the bounded
        // worker backlog. Re-sort the small queues as the player moves so a distant request cannot
        // remain ahead merely because it was discovered before a new nearby hole.
        _meshGenerator.Reprioritize(_lastViewPos, _predictedViewPos);

        var retentionRadius = _lastRenderDistance + MeshSpeculativeRadius;
        var radiusSq = retentionRadius * retentionRadius;
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

            if (chunkPos.Y < 0 || chunkPos.Y >= ChuckFormat.WorldHeight)
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
                // has been submitted. Tick may discard only non-resident scheduling state.
                if (section.Value.Renderer is null &&
                    !IsChunkInMeshRetentionDistance(section.Key, _lastViewPos))
                {
                    _sectionsToRemove.Add(section.Key);
                }
            }

            foreach (var pos in _sectionsToRemove)
            {
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

    public bool MarkDirty(Vector3D<int> chunkPos, bool priority = false) =>
        MarkDirty(
            chunkPos,
            priority ? SectionDirtyReason.BlockChange : SectionDirtyReason.InitialTerrain);

    private bool MarkDirty(Vector3D<int> chunkPos, SectionDirtyReason reason)
    {
        if (!IsChunkInMeshRetentionDistance(chunkPos, _lastViewPos))
            return false;

        // Full chunk arrival uses the same dirty notification as player edits. Only updates to
        // an existing mesh are critical. Startup meshes are foreground work: they beat ordinary
        // streaming without making a newly placed block wait behind the whole safety ring.
        var requestedPriority = ClassifyRequestedMeshPriority(
            reason,
            HasRenderer(chunkPos),
            _world is ClientWorld clientWorld && clientWorld.NetworkHandler.Preload.RequiresMesh(chunkPos),
            IsInMeshSafetyRing(chunkPos, _lastViewPos),
            IsInMeshForegroundRing(chunkPos, _lastViewPos));

        // The snapshot needs one cell of neighbor padding, but it already reads a missing column
        // through ChunkSource's empty-chunk fallback. Requiring the whole neighbor ring here made
        // a fully received, interactive chunk invisible until every adjacent streaming placeholder
        // arrived. When a real neighbor is decoded, its expanded dirty range rebuilds this shared
        // boundary with the newly available faces and lighting.
        if (!HasRenderableSourceChunk(_world, chunkPos))
            return false;

        var section = GetOrCreateSection(chunkPos);
        var version = section.Version;

        version.MarkDirty();
        section.RememberRequest(reason, requestedPriority, _schedulerTick);

        var snapshot = version.SnapshotIfNeeded();
        if (snapshot.HasValue)
        {
            _pendingMeshUpdates.Enqueue(section, RankPendingMesh(section, _lastCamera));
            return true;
        }

        if (requestedPriority != MeshWorkPriority.Background)
        {
            // SnapshotIfNeeded also reports pending while the request is still in our local
            // list. Promoting only the worker queue silently loses priority in that interval.
            if (_pendingMeshUpdates.Promote(
                    chunkPos,
                    RankPendingMesh(section, _lastCamera)))
                return false;

            _meshGenerator.Promote(chunkPos, section.RequestedPriority);
        }

        return false;
    }

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

        // A neighboring full-chunk arrival can touch this boundary repeatedly while the streaming
        // wave crosses it. Do not invalidate a build already using a coherent snapshot: doing so
        // every arrival makes each result stale before upload and can permanently starve nearby
        // holes. At worst that snapshot contains an internal face against the formerly absent
        // neighbor; it is hidden by the neighbor and a later ordinary update can clean it up.
        if (_sections.TryGetValue(chunkPos, out var section) && section.Version.State.Pending != -1)
            return;

        MarkDirty(chunkPos, SectionDirtyReason.StreamingBoundary);
    }

    internal static MeshWorkPriority ClassifyRequestedMeshPriority(
        SectionDirtyReason reason,
        bool hasRenderer,
        bool requiredForStartup,
        bool withinSafetyRing,
        bool withinForegroundRing)
    {
        if ((reason & SectionDirtyReason.BlockChange) != 0 && hasRenderer)
            return MeshWorkPriority.Critical;
        if ((reason & SectionDirtyReason.Lighting) != 0 && hasRenderer && withinSafetyRing)
            return MeshWorkPriority.Critical;
        if (requiredForStartup ||
            (reason & SectionDirtyReason.InitialTerrain) != 0 && withinForegroundRing)
            return MeshWorkPriority.Foreground;
        return MeshWorkPriority.Background;
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
        section = new SectionRenderState(chunkPos);
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

    private IEnumerable<SubChunkRenderer> ResidentRenderers()
    {
        foreach (var section in _residentSections) yield return section.Renderer!;
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

        section.Version.AbandonPendingMesh();
        if (_sections.Remove(chunkPos, out section)) section.Dispose();
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

    private bool IsChunkInMeshRetentionDistance(Vector3D<int> chunkWorldPos, Vector3D<double> viewPos) =>
        IsChunkInRenderDistance(chunkWorldPos, viewPos) ||
        IsSpeculativePrefetchChunk(chunkWorldPos, viewPos, _predictedViewPos, _lastRenderDistance);

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

        var retentionRadius = renderDistance + MeshSpeculativeRadius;
        if (currentDistanceSq > retentionRadius * retentionRadius)
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
    private static unsafe WgpuPipeline CreateWgpuPipeline(WebGpuDevice device, RenderState state,
        PrimitiveTopology topology = PrimitiveTopology.TriangleList, string fragmentEntryPoint = "fs_main")
    {
        var source = AssetManager.Instance.GetAsset("shaders/chunk.wgsl").GetTextContent();

        var attrs = stackalloc VertexAttribute[5];
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
            ShaderLocation = 3
        };
        attrs[4] = new VertexAttribute
        {
            Format = VertexFormat.Uint8x2,
            Offset = 18,
            ShaderLocation = 4
        };

        VertexBufferLayout bufferLayout = new()
        {
            ArrayStride = 20,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = 5,
            Attributes = attrs
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
                    MinBindingSize = ChunkUniformSize,
                    // Lets RenderSolidWebGpu batch every visible chunk's uniforms into one buffer,
                    // written with a single QueueWriteBuffer call instead of one per chunk — see
                    // WgpuPipeline.WriteDynamicUniforms. The translucent and wireframe passes still
                    // draw through the per-draw-buffer BindNextUniforms; that call site handles a
                    // dynamic-offset layout regardless of which of the two a pipeline was built with.
                    HasDynamicOffset = true
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
            ChunkUniformSize,
            uniformEntries,
            texEntries,
            &bufferLayout, 1,
            state,
            device.SurfaceFormat,
            TextureFormat.Depth32float,
            topology,
            fragmentEntryPoint: fragmentEntryPoint);
    }

    /// <summary>
    ///     Draws solid-pass chunks through the native WebGPU command encoder.
    ///     Binds the terrain pipeline and texture array once, then iterates chunks.
    /// </summary>
    private unsafe void RenderSolidWebGpu(
        RenderPassEncoder* pass, WgpuPipeline pipeline, WgpuTextureArray textureArray)
    {
        pipeline.Bind(pass);
        // Asked of the array per pass rather than held: a texture-pack switch rebuilds the array
        // underneath, and a bind group made against the old one points at a destroyed texture.
        WgpuPipeline.BindGroup(pass, 1,
            textureArray.BindGroupFor(pipeline.TextureBindGroupLayout), WebGpuDevice.Current!.Api);

        // The same set the GL pass draws, chosen by PrepareFrame — which the caller is responsible
        // for having run, since the view matrices this reads come off the stacks there too.
        //
        // Two passes instead of BindNextUniforms' one-buffer-per-draw: profiling (see git history on
        // this method) found the per-chunk QueueWriteBuffer call, not the draw call, was ~10ms of the
        // frame — CPU-side cost scaling with visible chunk count, which greedy meshing (a
        // triangle-count optimization) can't touch. Building every chunk's ChunkUniforms into one
        // scratch array and writing it with a single WriteDynamicUniforms call amortizes that away;
        // the second loop only binds a dynamic offset and issues the draw, both cheap.
        var count = _visibleRenderers.Count;
        if (_solidUniformScratch.Length < count)
        {
            _solidUniformScratch = new ChunkUniforms[count];
        }

        for (var i = 0; i < count; i++)
        {
            var renderer = _visibleRenderers[i];
            var fadeProgress = Math.Clamp(renderer.Age / SubChunkRenderer.FadeDuration, 0.0f, 1.0f);

            var camRel = new Vector3D<double>(
                renderer.PositionMinus.X - _lastViewPos.X,
                renderer.PositionMinus.Y - _lastViewPos.Y,
                renderer.PositionMinus.Z - _lastViewPos.Z);
            camRel += new Vector3D<double>(renderer.ClipPosition.X, renderer.ClipPosition.Y, renderer.ClipPosition.Z);

            var translation = Matrix4X4.CreateTranslation(
                new Vector3D<float>((float)camRel.X, (float)camRel.Y, (float)camRel.Z));
            var modelView = translation * _modelView;

            _solidUniformScratch[i] = BuildChunkUniforms(modelView, renderer.Position, fadeProgress);
        }

        var t0 = Stopwatch.GetTimestamp();
        pipeline.WriteDynamicUniforms(_solidUniformScratch.AsSpan(0, count));
        var t1 = Stopwatch.GetTimestamp();
        Profiler.Record("UniformUpload", (t1 - t0) * 1000.0 / Stopwatch.Frequency);

        for (var i = 0; i < count; i++)
        {
            pipeline.BindDynamicUniforms(pass, i);
            _visibleRenderers[i].RenderWebGpu(pass, 0);
        }

        var t2 = Stopwatch.GetTimestamp();
        Profiler.Record("DrawCall", (t2 - t1) * 1000.0 / Stopwatch.Frequency);
    }

    /// <summary>
    ///     Draws the solid pass as flat-green triangle edges instead of textured terrain, under
    ///     <see cref="WireframeEnabled" />. Same chunk set, transforms and uniforms as
    ///     <see cref="RenderSolidWebGpu" /> — only the pipeline and the mesh slot it draws differ.
    /// </summary>
    private unsafe void RenderWireframeWebGpu(
        RenderPassEncoder* pass, WgpuPipeline pipeline, WgpuTextureArray textureArray)
    {
        pipeline.Bind(pass);
        WgpuPipeline.BindGroup(pass, 1,
            textureArray.BindGroupFor(pipeline.TextureBindGroupLayout), WebGpuDevice.Current!.Api);

        foreach (var renderer in _visibleRenderers)
        {
            var fadeProgress = Math.Clamp(renderer.Age / SubChunkRenderer.FadeDuration, 0.0f, 1.0f);

            var camRel = new Vector3D<double>(
                renderer.PositionMinus.X - _lastViewPos.X,
                renderer.PositionMinus.Y - _lastViewPos.Y,
                renderer.PositionMinus.Z - _lastViewPos.Z);
            camRel += new Vector3D<double>(renderer.ClipPosition.X, renderer.ClipPosition.Y, renderer.ClipPosition.Z);

            var translation = Matrix4X4.CreateTranslation(
                new Vector3D<float>((float)camRel.X, (float)camRel.Y, (float)camRel.Z));
            var modelView = translation * _modelView;

            pipeline.BindNextUniforms(pass, BuildChunkUniforms(modelView, renderer.Position, fadeProgress));

            renderer.RenderWireframeWebGpu(pass);
        }
    }

    /// <summary>WebGPU translucent pass — sorted back-to-front, same as the GL path.</summary>
    private unsafe void RenderTranslucentWebGpu(
        RenderPassEncoder* pass, WgpuPipeline pipeline, WgpuTextureArray textureArray,
        Vector3D<double> viewPos)
    {
        pipeline.Bind(pass);
        // Asked of the array per pass rather than held: a texture-pack switch rebuilds the array
        // underneath, and a bind group made against the old one points at a destroyed texture.
        WgpuPipeline.BindGroup(pass, 1,
            textureArray.BindGroupFor(pipeline.TextureBindGroupLayout), WebGpuDevice.Current!.Api);

        _translucentDistanceComparer.Origin = viewPos;
        _translucentRenderers.Sort(_translucentDistanceComparer);

        foreach (var renderer in _translucentRenderers)
        {
            var fadeProgress = Math.Clamp(renderer.Age / SubChunkRenderer.FadeDuration, 0.0f, 1.0f);

            var camRel = new Vector3D<double>(
                renderer.PositionMinus.X - viewPos.X,
                renderer.PositionMinus.Y - viewPos.Y,
                renderer.PositionMinus.Z - viewPos.Z);
            camRel += new Vector3D<double>(renderer.ClipPosition.X, renderer.ClipPosition.Y, renderer.ClipPosition.Z);

            var translation = Matrix4X4.CreateTranslation(
                new Vector3D<float>((float)camRel.X, (float)camRel.Y, (float)camRel.Z));
            var modelView = translation * _modelView;

            pipeline.BindNextUniforms(pass, BuildChunkUniforms(modelView, renderer.Position, fadeProgress));

            renderer.RenderWebGpu(pass, 1);
        }

        _translucentRenderers.Clear();
    }

    /// <summary>
    ///     The per-chunk uniform block, from the frame's fog and world light rather than from state
    ///     of this renderer's own — the GL terrain shader is fed from the same two, so a chunk drawn
    ///     by either backend is lit and fogged alike.
    /// </summary>
    private ChunkUniforms BuildChunkUniforms(Matrix4X4<float> modelView, Vector3D<int> chunkPos, float fadeProgress)
    {
        var fog = RenderSystem.Fog;
        var light = RenderSystem.WorldLight;

        return new ChunkUniforms
        {
            ModelViewMatrix = modelView,
            ProjectionMatrix = WgpuClip.FromGl(_projection),
            ChunkPosX = chunkPos.X,
            ChunkPosY = chunkPos.Z,
            // Caller updates these per frame.
            TimeX = 0,
            TimeY = 0,
            TimeZ = 0,
            FadeProgress = fadeProgress,
            ChunkFadeEnabled = 1,
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

        foreach (var state in _sections.Values) state.Dispose();

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

        _translucentRenderers.Clear();
        _renderersToRemove.Clear();
        _pendingMeshUpdates.Clear();
        _sectionsToRemove.Clear();
        _everPresentedMeshes.Clear();
        _activePresentationRegressions.Clear();
        _currentPresentationRegressions.Clear();
        _presentedThisFrame.Clear();

    }

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

/// <summary>
///     Mirror of the WGSL <c>Uniforms</c> struct in <c>chunk.wgsl</c>, laid out to match
///     WGSL's default alignment rules (mat4x4 = 16, vec3 = 16, vec4 = 16, f32/u32 = 4).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 336)]
public struct ChunkUniforms
{
    // mat4x4<f32> modelViewMatrix at offset 0
    [FieldOffset(0)] public Matrix4X4<float> ModelViewMatrix;

    // mat4x4<f32> projectionMatrix at offset 64
    [FieldOffset(64)] public Matrix4X4<float> ProjectionMatrix;

    // vec2<f32> chunkPos at offset 128 (align 8, size 8)
    [FieldOffset(128)] public float ChunkPosX;
    [FieldOffset(132)] public float ChunkPosY;

    // vec3<f32> time at offset 144 (align 16, size 12)
    [FieldOffset(144)] public float TimeX;
    [FieldOffset(148)] public float TimeY;
    [FieldOffset(152)] public float TimeZ;

    // f32 ambientDarkness at offset 156
    [FieldOffset(156)] public float AmbientDarkness;

    // f32 luminanceOffset at offset 160
    [FieldOffset(160)] public float LuminanceOffset;

    // f32 wavyLeavesStrength at offset 164
    [FieldOffset(164)] public float WavyLeavesStrength;

    // f32 wavyLeavesSpeed at offset 168
    [FieldOffset(168)] public float WavyLeavesSpeed;

    // f32 wavyPlantStrength at offset 172
    [FieldOffset(172)] public float WavyPlantStrength;

    // f32 wavyPlantSpeed at offset 176
    [FieldOffset(176)] public float WavyPlantSpeed;

    // u32 wavyPlantMode at offset 180
    [FieldOffset(180)] public uint WavyPlantMode;

    // vec4<u32> wavyLeafLayers0 at offset 192 (align 16)
    [FieldOffset(192)] public uint WavyLeafLayer0;
    [FieldOffset(196)] public uint WavyLeafLayer1;
    [FieldOffset(200)] public uint WavyLeafLayer2;
    [FieldOffset(204)] public uint WavyLeafLayer3;

    // vec4<u32> wavyLeafLayers1 at offset 208
    [FieldOffset(208)] public uint WavyLeafLayer4;
    [FieldOffset(212)] public uint WavyLeafLayer5;
    [FieldOffset(216)] public uint WavyLeafLayer6;
    [FieldOffset(220)] public uint WavyLeafLayer7;

    // u32 wavyLeafCount at offset 224
    [FieldOffset(224)] public uint WavyLeafCount;

    // vec4<u32> wavyPlantLayers0 at offset 240 (align 16)
    [FieldOffset(240)] public uint WavyPlantLayer0;
    [FieldOffset(244)] public uint WavyPlantLayer1;
    [FieldOffset(248)] public uint WavyPlantLayer2;
    [FieldOffset(252)] public uint WavyPlantLayer3;

    // vec4<u32> wavyPlantLayers1 at offset 256
    [FieldOffset(256)] public uint WavyPlantLayer4;
    [FieldOffset(260)] public uint WavyPlantLayer5;
    [FieldOffset(264)] public uint WavyPlantLayer6;
    [FieldOffset(268)] public uint WavyPlantLayer7;

    // u32 wavyPlantCount at offset 272
    [FieldOffset(272)] public uint WavyPlantCount;

    // vec4<f32> fogColor at offset 288 (align 16)
    [FieldOffset(288)] public float FogColorR;
    [FieldOffset(292)] public float FogColorG;
    [FieldOffset(296)] public float FogColorB;
    [FieldOffset(300)] public float FogColorA;

    // f32 fogStart at offset 304
    [FieldOffset(304)] public float FogStart;

    // f32 fogEnd at offset 308
    [FieldOffset(308)] public float FogEnd;

    // f32 fogDensity at offset 312
    [FieldOffset(312)] public float FogDensity;

    // u32 fogMode at offset 316
    [FieldOffset(316)] public uint FogMode;

    // u32 chunkFadeEnabled at offset 320
    [FieldOffset(320)] public uint ChunkFadeEnabled;

    // f32 fadeProgress at offset 324
    [FieldOffset(324)] public float FadeProgress;
}
