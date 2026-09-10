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
    internal const long MeshAgePromotionTicks = 120;
    internal const double MeshPredictionTicks = 10.0;
    internal const double MeshPrefetchMargin = SubChunkRenderer.Size;
    internal const int MeshSpeculativeRadius = 1;
    internal const int MeshDiscoveryBacklogPerWorker = 8;
    internal const int MeshSafetyBacklogPerWorker = 2;

    //TODO: MAKE THIS CONFIGURABLE
    private const double MeshUploadBudgetMs = 1.5;

    //TODO: MAKE THIS CONFIGURABLE
    private const double MeshDispatchBudgetMs = 1.5;

    /// <summary>Bytes of <see cref="ChunkUniforms" />, as chunk.wgsl declares the block.</summary>
    private const uint ChunkUniformSize = 336;

    private static readonly Vector3D<int>[] s_spiralOffsets;
    private static readonly Vector2D<int>[] s_safetyColumnOffsets;
    private readonly Dictionary<Vector3D<int>, ChunkMeshVersion> _chunkVersions = [];
    private readonly List<Vector3D<int>> _chunkVersionsToRemove = [];
    private readonly List<ChunkToMeshInfo> _dirtyChunks = [];
    private readonly MeshPriorityFairness _dispatchFairness = new();
    private readonly List<ChunkToMeshInfo> _lightingUpdates = [];
    private readonly ILogger<ChunkRenderer> _logger = Log.Instance.For<ChunkRenderer>();
    private readonly ChunkMeshGenerator _meshGenerator;
    private readonly List<SubChunkRenderer> _occludedRenderersBuffer = [];
    private readonly ChunkOcclusionCuller _occlusionCuller = new();
    private readonly GameOptions _options;
    private readonly Dictionary<Vector3D<int>, SubChunkState> _renderers = [];
    private readonly List<SubChunkRenderer> _renderersToRemove = [];
    private readonly Dictionary<Vector3D<int>, MeshWorkPriority> _requestedPriorities = [];
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
    private Vector3D<double> _lastViewPos;
    private int _meshReadyRadius = int.MaxValue;
    private Matrix4X4<float> _modelView;
    private Vector3D<double> _predictedViewPos;
    private Matrix4X4<float> _projection;
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

        offsets.Sort((a, b) =>
            (a.X * a.X + a.Y * a.Y + a.Z * a.Z).CompareTo(b.X * b.X + b.Y * b.Y + b.Z * b.Z));

        s_spiralOffsets = [.. offsets];

        var safetyColumns = new List<Vector2D<int>>();
        for (var x = -MeshSafetyRingRadius; x <= MeshSafetyRingRadius; x++)
        for (var z = -MeshSafetyRingRadius; z <= MeshSafetyRingRadius; z++)
        {
            if (x * x + z * z <= MeshSafetyRingRadius * MeshSafetyRingRadius)
                safetyColumns.Add(new Vector2D<int>(x, z));
        }

        safetyColumns.Sort((a, b) =>
            (a.X * a.X + a.Y * a.Y).CompareTo(b.X * b.X + b.Y * b.Y));
        s_safetyColumnOffsets = [.. safetyColumns];
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
            return _dirtyChunks.Count + _lightingUpdates.Count + profile.Outstanding;
        }
    }

    public int TotalChunks => _renderers.Count;
    public int ChunksInFrustum { get; private set; }
    public int ChunksOccluded { get; private set; }
    public int ChunksRendered { get; private set; }
    public int TranslucentMeshes { get; private set; }

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
                if (!_renderers.ContainsKey(new Vector3D<int>(
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
        var dirty = _dirtyChunks.Select(static item => item.Pos).ToHashSet();
        var text = new StringBuilder(256 + (radius * 2 + 1) * (radius * 2 + 1) * 48);

        text.Append("center\t").Append(centerX).Append('\t').Append(centerZ).AppendLine();
        text.Append("viewDistance\t").Append(_lastRenderDistance).AppendLine();
        text.Append("meshReadyRadius\t").Append(MeshReadyRadius).AppendLine();
        text.Append("totalRenderers\t").Append(_renderers.Count).AppendLine();
        text.Append("pendingWork\t").Append(PendingMeshWork).AppendLine();
        text.AppendLine("chunkX\tchunkZ\tdistance2\tloaded\tmeshes\tvisible\tpending\tdirty\tforeground\tcritical\tbackground");

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

            for (var y = 0; y < ChuckFormat.WorldHeight; y += SubChunkRenderer.Size)
            {
                var pos = new Vector3D<int>(chunkX * SubChunkRenderer.Size, y,
                    chunkZ * SubChunkRenderer.Size);
                if (_renderers.ContainsKey(pos)) meshes++;
                if (visible.Contains(pos)) visibleMeshes++;
                if (_chunkVersions.TryGetValue(pos, out var version) && version.State.Pending != -1) pending++;
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
                .Append(foreground).Append('\t').Append(critical).Append('\t').Append(background)
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

        _renderers.TryGetValue(cameraChunkPos, out var cameraState);

        var meshReadyCenterX = cameraChunkPos.X / SubChunkRenderer.Size;
        var meshReadyCenterZ = cameraChunkPos.Z / SubChunkRenderer.Size;
        _meshReadyRadius = GetContiguousMeshRadius(
            meshReadyCenterX,
            meshReadyCenterZ,
            MeshSafetyRingRadius);

        if (cameraState == null)
        {
            var y = Math.Clamp(cameraChunkPos.Y, 0, 112);
            _renderers.TryGetValue(new Vector3D<int>(cameraChunkPos.X, y, cameraChunkPos.Z), out cameraState);
        }

        float renderDistWorld = renderParams.RenderDistance * SubChunkRenderer.Size;

        using (Profiler.Begin("FindVisible"))
        {
            ChunksInFrustum = _occlusionCuller.FindVisible(
                this,
                _renderers.Values.Select(static state => state.Renderer),
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
            foreach (var state in _renderers.Values)
            {
                var renderer = state.Renderer;
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

        foreach (var state in _renderers.Values)
        {
            if (!IsChunkInMeshRetentionDistance(state.Renderer.Position, _lastViewPos))
            {
                _renderersToRemove.Add(state.Renderer);
            }
        }

        foreach (var renderer in _renderersToRemove)
        {
            UpdateAdjacency(renderer, false);
            _renderers.Remove(renderer.Position);
            renderer.Dispose();

            _chunkVersions.Remove(renderer.Position);
        }

        _renderersToRemove.Clear();

        DispatchPendingMeshUpdates(camera);
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
                if (!_chunkVersions.TryGetValue(mesh.Pos, out var version))
                {
                    version = ChunkMeshVersion.Get();
                    _chunkVersions[mesh.Pos] = version;
                }

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

                if (_renderers.TryGetValue(mesh.Pos, out var state))
                {
                    state.Renderer.UploadMeshData(mesh.Solid, mesh.Translucent);
                    state.IsLit = mesh.IsLit;
                    state.Renderer.VisibilityData = mesh.VisibilityData;
                }
                else
                {
                    var renderer = new SubChunkRenderer(mesh.Pos);
                    renderer.UploadMeshData(mesh.Solid, mesh.Translucent);
                    renderer.VisibilityData = mesh.VisibilityData;
                    _renderers[mesh.Pos] = new SubChunkState(mesh.IsLit, renderer);
                    UpdateAdjacency(renderer, true);
                }

                _requestedPriorities.Remove(mesh.Pos);
                if (_world is ClientWorld clientWorld)
                    clientWorld.NetworkHandler.NotifyMeshUploaded(mesh.Pos);
            }
            else
            {
                // Finished after the chunk fell out of render distance — UploadMeshData (which
                // would normally return these to the pool) never runs for it.
                mesh.Dispose();
                _requestedPriorities.Remove(mesh.Pos);
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

        SubChunkRenderer? Get(Vector3D<int> p) => _renderers.TryGetValue(p, out var s) ? s.Renderer : null;

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
                    if (_renderers.TryGetValue(pos, out var state))
                    {
                        if (state.Renderer.LastVisibleFrame != frame)
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
            if (!_renderers.ContainsKey(new Vector3D<int>(
                    chunkX * SubChunkRenderer.Size, y, chunkZ * SubChunkRenderer.Size)))
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Issues as many <see cref="ChunkMeshGenerator.MeshChunk" /> calls as fit in
    ///     <see cref="MeshDispatchBudgetMs" />, instead of the fixed one-dirty-plus-one-lighting
    ///     cap this used to have. That fixed cap throttled how fast a burst of newly-loaded chunks
    ///     could drain regardless of how fast the mesh workers or the snapshot copy underneath them
    ///     could go; a wall-clock budget lets it drain as fast as those actually allow, and still
    ///     bounds the frame-thread cost of dispatching regardless of how large the backlog gets.
    /// </summary>
    private void DispatchPendingMeshUpdates(ICuller? camera)
    {
        _dirtyChunks.RemoveAll(c => !IsChunkInMeshRetentionDistance(c.Pos, _lastViewPos));
        _lightingUpdates.RemoveAll(c => !IsChunkInMeshRetentionDistance(c.Pos, _lastViewPos));

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed.TotalMilliseconds < MeshDispatchBudgetMs)
        {
            var dispatchedDirty = TryDispatchBestDirtyMeshUpdate(camera);
            var dispatchedLighting = TryDispatchBestLightingMeshUpdate();

            if (!dispatchedDirty && !dispatchedLighting)
            {
                break;
            }
        }
    }

    private bool TryDispatchBestDirtyMeshUpdate(ICuller? camera)
    {
        var criticalIndex = -1;
        var foregroundIndex = -1;
        var backgroundIndex = -1;
        var criticalRank = (Tier: int.MaxValue, DistanceSquared: double.MaxValue, EnqueuedAt: long.MaxValue);
        var foregroundRank = criticalRank;
        var backgroundRank = criticalRank;

        for (var i = 0; i < _dirtyChunks.Count; i++)
        {
            var info = _dirtyChunks[i];
            var aabb = new Box(
                info.Pos.X, info.Pos.Y, info.Pos.Z,
                info.Pos.X + SubChunkRenderer.Size,
                info.Pos.Y + SubChunkRenderer.Size,
                info.Pos.Z + SubChunkRenderer.Size
            );

            // Expand only the scheduling frustum. Drawing still tests the exact mesh bounds.
            var prefetched = camera?.IsBoundingBoxInFrustum(aabb.Expand(
                MeshPrefetchMargin, MeshPrefetchMargin, MeshPrefetchMargin)) ?? false;
            var rank = GetMeshSchedulingRank(
                info.Pos,
                _lastViewPos,
                _predictedViewPos,
                info.Priority,
                prefetched,
                !IsChunkInRenderDistance(info.Pos, _lastViewPos),
                info.EnqueuedAt,
                _schedulerTick);

            switch (EffectiveWorkerPriority(info))
            {
                case MeshWorkPriority.Critical when rank.CompareTo(criticalRank) < 0:
                    criticalIndex = i;
                    criticalRank = rank;
                    break;
                case MeshWorkPriority.Foreground when rank.CompareTo(foregroundRank) < 0:
                    foregroundIndex = i;
                    foregroundRank = rank;
                    break;
                case MeshWorkPriority.Background when rank.CompareTo(backgroundRank) < 0:
                    backgroundIndex = i;
                    backgroundRank = rank;
                    break;
            }
        }

        if (criticalIndex == -1 && foregroundIndex == -1 && backgroundIndex == -1)
        {
            return false;
        }

        var selectedPriority = _dispatchFairness.Select(
            criticalIndex != -1,
            foregroundIndex != -1,
            backgroundIndex != -1);
        var bestIndex = selectedPriority switch
        {
            MeshWorkPriority.Critical => criticalIndex,
            MeshWorkPriority.Foreground => foregroundIndex,
            _ => backgroundIndex
        };
        var closest = _dirtyChunks[bestIndex];
        _meshGenerator.MeshChunk(
            _world,
            closest.Pos,
            closest.Version,
            _options.AlternateBlocksEnabled,
            selectedPriority);
        _dirtyChunks.RemoveAt(bestIndex);
        return true;
    }

    private MeshWorkPriority EffectiveWorkerPriority(ChunkToMeshInfo info) =>
        info.Priority != MeshWorkPriority.Background
            ? info.Priority
            : IsInMeshSafetyRing(info.Pos, _lastViewPos)
                ? MeshWorkPriority.Foreground
                : MeshWorkPriority.Background;

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
    {
        var centerX = (int)Math.Floor(viewPosition.X / SubChunkRenderer.Size);
        var centerZ = (int)Math.Floor(viewPosition.Z / SubChunkRenderer.Size);
        var chunkX = position.X / SubChunkRenderer.Size;
        var chunkZ = position.Z / SubChunkRenderer.Size;
        var deltaX = chunkX - centerX;
        var deltaZ = chunkZ - centerZ;
        return deltaX * deltaX + deltaZ * deltaZ <= MeshSafetyRingRadius * MeshSafetyRingRadius;
    }

    private bool TryDispatchBestLightingMeshUpdate()
    {
        var bestIndex = -1;
        var bestDist = double.MaxValue;
        for (var i = 0; i < _lightingUpdates.Count; i++)
        {
            var dist = Vector3D.DistanceSquared(ToDoubleVec(_lightingUpdates[i].Pos), _lastViewPos);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIndex = i;
            }
        }

        if (bestIndex == -1)
        {
            return false;
        }

        var update = _lightingUpdates[bestIndex];
        _meshGenerator.MeshChunk(_world, update.Pos, update.Version, _options.AlternateBlocksEnabled,
            update.Priority);
        _lightingUpdates.RemoveAt(bestIndex);
        return true;
    }

    public void UpdateAllRenderers()
    {
        foreach (var state in _renderers.Values)
        {
            if (IsChunkInMeshRetentionDistance(state.Renderer.Position, _lastViewPos) && state.IsLit)
            {
                if (!_chunkVersions.TryGetValue(state.Renderer.Position, out var version))
                {
                    version = ChunkMeshVersion.Get();
                    _chunkVersions[state.Renderer.Position] = version;
                }

                version.MarkDirty();

                var snapshot = version.SnapshotIfNeeded();
                if (snapshot.HasValue)
                {
                    _lightingUpdates.Add(new ChunkToMeshInfo(
                        state.Renderer.Position,
                        snapshot.Value,
                        MeshWorkPriority.Background));
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
        var foregroundPending = _requestedPriorities.Count(static entry =>
            entry.Value == MeshWorkPriority.Foreground);
        var safetyDiscoveryBudget = GetMeshSafetyDiscoveryCapacity(
            foregroundPending,
            _meshGenerator.MaxConcurrentTasks);
        var safetyAdmitted = 0;

        // Do not derive this traversal from the render-distance 3D spiral. At an aerial camera the
        // bottom sections are seven vertical steps from the clamped discovery center, and more
        // than PRIORITY_PASS_LIMIT unrelated spiral entries can sort ahead of them. Walking the
        // small safety set explicitly guarantees all 29 * 8 sections remain discoverable.
        foreach (var columnOffset in s_safetyColumnOffsets)
        {
            if (safetyAdmitted >= safetyDiscoveryBudget) break;
            for (var verticalDistance = 0;
                 verticalDistance < ChuckFormat.WorldHeight / SubChunkRenderer.Size;
                 verticalDistance++)
            {
                if (TrySafetySection(currentChunk.Y - verticalDistance) &&
                    safetyAdmitted >= safetyDiscoveryBudget) break;
                if (verticalDistance != 0 && TrySafetySection(currentChunk.Y + verticalDistance) &&
                    safetyAdmitted >= safetyDiscoveryBudget) break;
            }

            if (safetyAdmitted >= safetyDiscoveryBudget) break;

            continue;

            bool TrySafetySection(int sectionY)
            {
                if (sectionY < 0 || sectionY >= ChuckFormat.WorldHeight / SubChunkRenderer.Size)
                    return false;
                var chunkPos = new Vector3D<int>(
                    currentChunk.X + columnOffset.X,
                    sectionY,
                    currentChunk.Z + columnOffset.Y) * SubChunkRenderer.Size;
                if (_renderers.ContainsKey(chunkPos)) return false;
                RecoverOrphanedMesh(chunkPos);
                if (!PrioritizeSafetyMesh(chunkPos)) return false;
                safetyAdmitted++;
                return true;
            }
        }

        // Discovery is a producer and the mesh workers/uploads are the consumers. Letting the
        // producer run 32 sections every frame regardless of consumer progress grows a distance-32
        // world into a ten-thousand-entry dirty list, making the scheduler's priority scan itself
        // a frame stall. Keep only a small multiple of worker count buffered; urgent notifications
        // and startup prerequisites above are never rejected by this background limit.
        var pendingMeshWork = _dirtyChunks.Count + _lightingUpdates.Count + _meshGenerator.Profile.Outstanding;
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

            if (_renderers.ContainsKey(chunkPos))
                continue;

            RecoverOrphanedMesh(chunkPos);
            if (_chunkVersions.ContainsKey(chunkPos)) continue;

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
                    if (!_renderers.ContainsKey(chunkPos))
                    {
                        RecoverOrphanedMesh(chunkPos);
                        if (!_chunkVersions.ContainsKey(chunkPos) && MarkDirty(chunkPos))
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
            foreach (var version in _chunkVersions)
            {
                if (!IsChunkInMeshRetentionDistance(version.Key, _lastViewPos))
                {
                    _chunkVersionsToRemove.Add(version.Key);
                }
            }

            foreach (var pos in _chunkVersionsToRemove)
            {
                _chunkVersions[pos].Release();
                _chunkVersions.Remove(pos);
                _requestedPriorities.Remove(pos);
            }

            _chunkVersionsToRemove.Clear();
        }

        // The terrain-loading screen ticks the world renderer but does not necessarily open and
        // finish a world render pass. EndFrame is therefore not a reliable pump for startup mesh
        // work. Drain it here until the playable area is complete; normal gameplay keeps the
        // post-pass path so mesh replacement remains outside command recording.
        if (_world is ClientWorld loadingWorld && !loadingWorld.NetworkHandler.Preload.IsReady)
        {
            DispatchPendingMeshUpdates(null);
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

        foreach (var pos in new List<Vector3D<int>>(_chunkVersions.Keys))
            MarkDirty(pos, true);
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

        hasRenderer = _renderers.ContainsKey(pos);

        if (_chunkVersions.TryGetValue(pos, out var version))
        {
            state = version.State;
            return true;
        }

        state = default;
        return false;
    }

    public bool MarkDirty(Vector3D<int> chunkPos, bool priority = false)
    {
        if (!IsChunkInMeshRetentionDistance(chunkPos, _lastViewPos))
            return false;

        // Full chunk arrival uses the same dirty notification as player edits. Only updates to
        // an existing mesh are critical. Startup meshes are foreground work: they beat ordinary
        // streaming without making a newly placed block wait behind the whole safety ring.
        var requestedPriority = ClassifyRequestedMeshPriority(
            priority,
            _renderers.ContainsKey(chunkPos),
            _world is ClientWorld clientWorld && clientWorld.NetworkHandler.Preload.RequiresMesh(chunkPos),
            IsInMeshSafetyRing(chunkPos, _lastViewPos));

        // The snapshot needs one cell of neighbor padding, but it already reads a missing column
        // through ChunkSource's empty-chunk fallback. Requiring the whole neighbor ring here made
        // a fully received, interactive chunk invisible until every adjacent streaming placeholder
        // arrived. When a real neighbor is decoded, its expanded dirty range rebuilds this shared
        // boundary with the newly available faces and lighting.
        if (!HasRenderableSourceChunk(_world, chunkPos))
            return false;

        if (!_chunkVersions.TryGetValue(chunkPos, out var version))
        {
            version = ChunkMeshVersion.Get();
            _chunkVersions[chunkPos] = version;
        }

        version.MarkDirty();
        RememberPriority(chunkPos, requestedPriority);

        var snapshot = version.SnapshotIfNeeded();
        if (snapshot.HasValue)
        {
            for (var i = 0; i < _dirtyChunks.Count; i++)
            {
                if (_dirtyChunks[i].Pos == chunkPos)
                {
                    _dirtyChunks[i] = new ChunkToMeshInfo(
                        chunkPos,
                        snapshot.Value,
                        MaxPriority(requestedPriority, _dirtyChunks[i].Priority),
                        _dirtyChunks[i].EnqueuedAt);
                    return true;
                }
            }

            _dirtyChunks.Add(new ChunkToMeshInfo(chunkPos, snapshot.Value, requestedPriority, _schedulerTick));
            return true;
        }

        if (requestedPriority != MeshWorkPriority.Background)
        {
            // SnapshotIfNeeded also reports pending while the request is still in our local
            // list. Promoting only the worker queue silently loses priority in that interval.
            for (var i = 0; i < _dirtyChunks.Count; i++)
            {
                if (_dirtyChunks[i].Pos == chunkPos)
                {
                    var pending = _dirtyChunks[i];
                    _dirtyChunks[i] = new ChunkToMeshInfo(
                        pending.Pos,
                        pending.Version,
                        MaxPriority(pending.Priority, requestedPriority),
                        pending.EnqueuedAt);
                    return false;
                }
            }

            _meshGenerator.Promote(chunkPos, requestedPriority);
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
        if (!_renderers.ContainsKey(chunkPos) && !requiredForStartup) return;

        // A neighboring full-chunk arrival can touch this boundary repeatedly while the streaming
        // wave crosses it. Do not invalidate a build already using a coherent snapshot: doing so
        // every arrival makes each result stale before upload and can permanently starve nearby
        // holes. At worst that snapshot contains an internal face against the formerly absent
        // neighbor; it is hidden by the neighbor and a later ordinary update can clean it up.
        if (_chunkVersions.TryGetValue(chunkPos, out var version) && version.State.Pending != -1)
            return;

        MarkDirty(chunkPos);
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
        _requestedPriorities.GetValueOrDefault(chunkPos, MeshWorkPriority.Background);

    private void RememberPriority(Vector3D<int> chunkPos, MeshWorkPriority priority)
    {
        if (priority == MeshWorkPriority.Background) return;
        _requestedPriorities[chunkPos] = MaxPriority(RequestedPriority(chunkPos), priority);
    }

    /// <summary>
    ///     Drops version state that claims a missing renderer has work pending when no local,
    ///     worker, or result queue actually owns that work. Discovery can then schedule it again.
    /// </summary>
    private void RecoverOrphanedMesh(Vector3D<int> chunkPos)
    {
        if (!_chunkVersions.TryGetValue(chunkPos, out var version)) return;
        if (_dirtyChunks.Any(entry => entry.Pos == chunkPos) ||
            _lightingUpdates.Any(entry => entry.Pos == chunkPos) ||
            _meshGenerator.HasOutstanding(chunkPos))
            return;

        version.AbandonPendingMesh();
        version.Release();
        _chunkVersions.Remove(chunkPos);
        _requestedPriorities.Remove(chunkPos);
    }

    private void PrioritizeMesh(Vector3D<int> chunkPos)
    {
        if (_renderers.ContainsKey(chunkPos))
            return;

        // Do not consume the one-shot orphan recovery while this is still a network placeholder.
        // The startup loop calls us again every frame; once the full chunk blob marks it Loaded,
        // the retry can create a real snapshot instead of being lost on MarkDirty's source guard.
        if (!HasRenderableSourceChunk(_world, chunkPos))
            return;

        if (_chunkVersions.ContainsKey(chunkPos))
        {
            RememberPriority(chunkPos, MeshWorkPriority.Foreground);

            for (var i = 0; i < _dirtyChunks.Count; i++)
            {
                if (_dirtyChunks[i].Pos == chunkPos)
                {
                    if (_dirtyChunks[i].Priority < MeshWorkPriority.Foreground)
                    {
                        _dirtyChunks[i] = new ChunkToMeshInfo(
                            _dirtyChunks[i].Pos,
                            _dirtyChunks[i].Version,
                            MeshWorkPriority.Foreground,
                            _dirtyChunks[i].EnqueuedAt);
                    }

                    return;
                }
            }

            var version = _chunkVersions[chunkPos];
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
            MarkDirty(chunkPos, true);

            return;
        }

        MarkDirty(chunkPos, true);
    }

    /// <summary>
    ///     Admits or promotes one missing safety-ring mesh without changing the content epoch of
    ///     work already in flight. Calling MarkDirty repeatedly here makes every worker result
    ///     stale before it can upload, which is a self-sustaining invisible-hole loop.
    /// </summary>
    private bool PrioritizeSafetyMesh(Vector3D<int> chunkPos)
    {
        if (_renderers.ContainsKey(chunkPos) || !HasRenderableSourceChunk(_world, chunkPos))
            return false;

        var previousPriority = RequestedPriority(chunkPos);
        if (_chunkVersions.TryGetValue(chunkPos, out var version))
        {
            RememberPriority(chunkPos, MeshWorkPriority.Foreground);

            for (var i = 0; i < _dirtyChunks.Count; i++)
            {
                if (_dirtyChunks[i].Pos != chunkPos) continue;
                var pending = _dirtyChunks[i];
                if (pending.Priority < MeshWorkPriority.Foreground)
                {
                    _dirtyChunks[i] = new ChunkToMeshInfo(
                        pending.Pos,
                        pending.Version,
                        MeshWorkPriority.Foreground,
                        pending.EnqueuedAt);
                }

                return previousPriority < MeshWorkPriority.Foreground;
            }

            if (version.State.Pending != -1)
            {
                _meshGenerator.Promote(chunkPos, MeshWorkPriority.Foreground);
                return previousPriority < MeshWorkPriority.Foreground;
            }
        }

        MarkDirty(chunkPos);
        return previousPriority < MeshWorkPriority.Foreground &&
               RequestedPriority(chunkPos) >= MeshWorkPriority.Foreground;
    }

    private void LogBlockingStartupMeshes(ClientWorld clientWorld)
    {
        foreach (var pos in clientWorld.NetworkHandler.Preload.RequiredMeshSections())
        {
            var state = _chunkVersions.TryGetValue(pos, out var version)
                ? version.State.ToString()
                : "none";
            _logger.LogInformation(
                "Blocking startup mesh {Pos}: source={Source}, version={Version}, dirty={Dirty}, " +
                "renderer={Renderer}, priority={Priority}",
                pos,
                HasRenderableSourceChunk(_world, pos),
                state,
                _dirtyChunks.Any(entry => entry.Pos == pos),
                _renderers.ContainsKey(pos),
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

        foreach (var state in _renderers.Values)
        {
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

            AddSize(state.Renderer.SolidMeshSizeBytes);
            AddSize(state.Renderer.TranslucentMeshSizeBytes);
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

        foreach (var state in _renderers.Values)
        {
            state.Renderer.Dispose();
        }

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

        _renderers.Clear();

        _translucentRenderers.Clear();
        _renderersToRemove.Clear();
        _requestedPriorities.Clear();

        foreach (var version in _chunkVersions.Values)
        {
            version.Release();
        }

        _chunkVersions.Clear();
    }

    private class SubChunkState(bool isLit, SubChunkRenderer renderer)
    {
        public bool IsLit { get; set; } = isLit;
        public SubChunkRenderer Renderer { get; } = renderer;
    }

    private struct ChunkToMeshInfo(
        Vector3D<int> pos,
        long version,
        MeshWorkPriority priority,
        long enqueuedAt = 0)
    {
        public readonly Vector3D<int> Pos = pos;
        public readonly long Version = version;
        public readonly MeshWorkPriority Priority = priority;
        public readonly long EnqueuedAt = enqueuedAt;
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
