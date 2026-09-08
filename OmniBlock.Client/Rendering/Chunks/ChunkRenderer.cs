using System.Diagnostics;
using System.Runtime.InteropServices;
using OmniBlock.Client.Options;
using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.WebGPU;
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

    //TODO: MAKE THIS CONFIGURABLE
    private const double MeshUploadBudgetMs = 1.5;

    //TODO: MAKE THIS CONFIGURABLE
    private const double MeshDispatchBudgetMs = 1.5;

    /// <summary>Bytes of <see cref="ChunkUniforms" />, as chunk.wgsl declares the block.</summary>
    private const uint ChunkUniformSize = 336;

    private static readonly Vector3D<int>[] s_spiralOffsets;
    private readonly Dictionary<Vector3D<int>, ChunkMeshVersion> _chunkVersions = [];
    private readonly List<Vector3D<int>> _chunkVersionsToRemove = [];
    private readonly List<ChunkToMeshInfo> _dirtyChunks = [];
    private readonly List<ChunkToMeshInfo> _lightingUpdates = [];
    private readonly HashSet<Vector3D<int>> _priorityChunks = [];
    private readonly ChunkMeshGenerator _meshGenerator;
    private readonly List<SubChunkRenderer> _occludedRenderersBuffer = [];
    private readonly ChunkOcclusionCuller _occlusionCuller = new();
    private readonly GameOptions _options;
    private readonly Dictionary<Vector3D<int>, SubChunkState> _renderers = [];
    private readonly List<SubChunkRenderer> _renderersToRemove = [];
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
    private Matrix4X4<float> _modelView;
    private Matrix4X4<float> _projection;

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
    }

    public ChunkRenderer(World world, GameOptions options)
    {
        _options = options;

        // Left uncapped, every dispatched chunk becomes its own unbounded Task.Run; once the
        // dispatch side stopped throttling itself to ~2 chunks/frame, an uncapped mesh generator
        // could flood the ThreadPool with concurrent GenerateMesh calls (each visiting 32K+
        // blocks plus a flood-fill) and starve the frame thread. Same reservation the chunk
        // loader's worker pool leaves for the tick thread.
        _meshGenerator = new ChunkMeshGenerator((ushort)Math.Max(1, Environment.ProcessorCount - 2));
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

    public int TotalChunks => _renderers.Count;
    public int ChunksInFrustum { get; private set; }
    public int ChunksOccluded { get; private set; }
    public int ChunksRendered { get; private set; }
    public int TranslucentMeshes { get; private set; }

    public void Visit(SubChunkRenderer renderer) => _visibleRenderers.Add(renderer);

    /// <summary>
    ///     Chooses which sub-chunks the frame draws and takes the view matrices the draw will
    ///     transform by off the matrix stacks.
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

        _modelView = GLManager.ModelView.Top;
        _projection = GLManager.Projection.Top;

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

        if (cameraState == null)
        {
            var y = Math.Clamp(cameraChunkPos.Y, 0, 112);
            _renderers.TryGetValue(new Vector3D<int>(cameraChunkPos.X, y, cameraChunkPos.Z), out cameraState);
        }

        float renderDistWorld = renderParams.RenderDistance * SubChunkRenderer.Size;

        using (Profiler.Begin("FindVisible"))
        {
            _occlusionCuller.FindVisible(
                this,
                cameraState?.Renderer,
                renderParams.ViewPos,
                renderParams.Camera,
                renderDistWorld,
                UseOcclusionCulling,
                _frameIndex
            );
        }

        AddNearbySections(cameraChunkPos, _frameIndex, renderParams.Camera);

        var frustumCount = 0;
        var visitedVisibleCount = _visibleRenderers.Count;

        foreach (var state in _renderers.Values)
        {
            if (renderParams.Camera.IsBoundingBoxInFrustum(state.Renderer.BoundingBox))
            {
                frustumCount++;
            }
        }

        ChunksInFrustum = frustumCount;
        ChunksOccluded = frustumCount - visitedVisibleCount;
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
            if (!IsChunkInRenderDistance(state.Renderer.Position, _lastViewPos))
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
                    RenderWireframeWebGpu(pass, WgpuWireframePipelineFor(GLManager.State.Current), array);
                }
                else
                {
                    RenderSolidWebGpu(pass, WgpuPipelineFor(GLManager.State.Current), array);
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
                RenderTranslucentWebGpu(pass, WgpuPipelineFor(GLManager.State.Current), array,
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

            if (IsChunkInRenderDistance(mesh.Pos, viewPos))
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
                        var priority = mesh.Priority || _priorityChunks.Contains(mesh.Pos);
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

                _priorityChunks.Remove(mesh.Pos);
            }
            else
            {
                // Finished after the chunk fell out of render distance — UploadMeshData (which
                // would normally return these to the pool) never runs for it.
                mesh.Dispose();
                _priorityChunks.Remove(mesh.Pos);
            }
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

    private void AddNearbySections(Vector3D<int> cameraChunkPos, int frame, ICuller camera)
    {
        var size = SubChunkRenderer.Size;
        for (var x = -size; x <= size; x += size)
        {
            for (var y = -size; y <= size; y += size)
            {
                for (var z = -size; z <= size; z += size)
                {
                    var pos = cameraChunkPos + new Vector3D<int>(x, y, z);
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
    ///     Issues as many <see cref="ChunkMeshGenerator.MeshChunk" /> calls as fit in
    ///     <see cref="MeshDispatchBudgetMs" />, instead of the fixed one-dirty-plus-one-lighting
    ///     cap this used to have. That fixed cap throttled how fast a burst of newly-loaded chunks
    ///     could drain regardless of how fast the mesh workers or the snapshot copy underneath them
    ///     could go; a wall-clock budget lets it drain as fast as those actually allow, and still
    ///     bounds the frame-thread cost of dispatching regardless of how large the backlog gets.
    /// </summary>
    private void DispatchPendingMeshUpdates(ICuller camera)
    {
        _dirtyChunks.RemoveAll(c => !IsChunkInRenderDistance(c.Pos, _lastViewPos));
        _lightingUpdates.RemoveAll(c => !IsChunkInRenderDistance(c.Pos, _lastViewPos));

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

    private bool TryDispatchBestDirtyMeshUpdate(ICuller camera)
    {
        var bestIndex = -1;
        var bestPriority = false;
        var bestDist = double.MaxValue;
        for (var i = 0; i < _dirtyChunks.Count; i++)
        {
            var info = _dirtyChunks[i];
            var aabb = new Box(
                info.Pos.X, info.Pos.Y, info.Pos.Z,
                info.Pos.X + SubChunkRenderer.Size,
                info.Pos.Y + SubChunkRenderer.Size,
                info.Pos.Z + SubChunkRenderer.Size
            );

            var dist = Vector3D.DistanceSquared(ToDoubleVec(info.Pos), _lastViewPos);
            if (camera.IsBoundingBoxInFrustum(aabb) &&
                (info.Priority && !bestPriority || info.Priority == bestPriority && dist < bestDist))
            {
                bestDist = dist;
                bestIndex = i;
                bestPriority = info.Priority;
            }
        }

        if (bestIndex == -1)
        {
            return false;
        }

        var closest = _dirtyChunks[bestIndex];
        _meshGenerator.MeshChunk(_world, closest.Pos, closest.Version, _options.AlternateBlocksEnabled, closest.Priority);
        _dirtyChunks.RemoveAt(bestIndex);
        return true;
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
        _meshGenerator.MeshChunk(_world, update.Pos, update.Version, _options.AlternateBlocksEnabled, update.Priority);
        _lightingUpdates.RemoveAt(bestIndex);
        return true;
    }

    public void UpdateAllRenderers()
    {
        foreach (var state in _renderers.Values)
        {
            if (IsChunkInRenderDistance(state.Renderer.Position, _lastViewPos) && state.IsLit)
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
                    _lightingUpdates.Add(new ChunkToMeshInfo(state.Renderer.Position, snapshot.Value, false));
                }
            }
        }
    }

    public void Tick(Vector3D<double> viewPos)
    {
        using var _chunkTick = Profiler.Begin("ChunkTick");

        _lastViewPos = viewPos;

        Vector3D<int> currentChunk = new(
            (int)Math.Floor(viewPos.X / SubChunkRenderer.Size),
            (int)Math.Floor(viewPos.Y / SubChunkRenderer.Size),
            (int)Math.Floor(viewPos.Z / SubChunkRenderer.Size)
        );

        var radiusSq = _lastRenderDistance * _lastRenderDistance;
        var enqueuedCount = 0;
        var priorityPassClean = true;

        //TODO: MAKE THESE CONFIGURABLE
        const int MAX_CHUNKS_PER_FRAME = 32;
        const int PRIORITY_PASS_LIMIT = 1024;
        const int BACKGROUND_PASS_LIMIT = 2048;

        for (var i = 0; i < PRIORITY_PASS_LIMIT && i < s_spiralOffsets.Length; i++)
        {
            var offset = s_spiralOffsets[i];
            var distSq = offset.X * offset.X + offset.Y * offset.Y + offset.Z * offset.Z;

            if (distSq > radiusSq)
                break;

            var chunkPos = (currentChunk + offset) * SubChunkRenderer.Size;

            if (chunkPos.Y < 0 || chunkPos.Y >= ChuckFormat.WorldHeight)
                continue;

            if (_renderers.ContainsKey(chunkPos) || _chunkVersions.ContainsKey(chunkPos))
                continue;

            if (MarkDirty(chunkPos))
            {
                enqueuedCount++;
                priorityPassClean = false;
            }
            else
            {
                priorityPassClean = false;
            }

            if (enqueuedCount >= MAX_CHUNKS_PER_FRAME)
                break;
        }

        if (priorityPassClean && enqueuedCount < MAX_CHUNKS_PER_FRAME)
        {
            for (var i = 0; i < BACKGROUND_PASS_LIMIT; i++)
            {
                var offset = s_spiralOffsets[_currentIndex];
                var distSq = offset.X * offset.X + offset.Y * offset.Y + offset.Z * offset.Z;

                if (distSq <= radiusSq)
                {
                    var chunkPos = (currentChunk + offset) * SubChunkRenderer.Size;
                    if (!_renderers.ContainsKey(chunkPos) && !_chunkVersions.ContainsKey(chunkPos))
                    {
                        if (MarkDirty(chunkPos))
                        {
                            enqueuedCount++;
                        }
                    }
                }

                _currentIndex = (_currentIndex + 1) % s_spiralOffsets.Length;

                if (enqueuedCount >= MAX_CHUNKS_PER_FRAME)
                    break;
            }
        }

        using (Profiler.Begin("RemoveVersions"))
        {
            foreach (var version in _chunkVersions)
            {
                if (!IsChunkInRenderDistance(version.Key, _lastViewPos))
                {
                    _chunkVersionsToRemove.Add(version.Key);
                }
            }

            foreach (var pos in _chunkVersionsToRemove)
            {
                _chunkVersions[pos].Release();
                _chunkVersions.Remove(pos);
                _priorityChunks.Remove(pos);
            }

            _chunkVersionsToRemove.Clear();
        }
    }

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
        if (!_world.BlockHost.IsRegionLoaded(chunkPos.X - 1, chunkPos.Y - 1, chunkPos.Z - 1, chunkPos.X + SubChunkRenderer.Size + 1, chunkPos.Y + SubChunkRenderer.Size + 1, chunkPos.Z + SubChunkRenderer.Size + 1) |
            !IsChunkInRenderDistance(chunkPos, _lastViewPos))
            return false;

        if (!_chunkVersions.TryGetValue(chunkPos, out var version))
        {
            version = ChunkMeshVersion.Get();
            _chunkVersions[chunkPos] = version;
        }

        version.MarkDirty();
        if (priority) _priorityChunks.Add(chunkPos);

        var snapshot = version.SnapshotIfNeeded();
        if (snapshot.HasValue)
        {
            for (var i = 0; i < _dirtyChunks.Count; i++)
            {
                if (_dirtyChunks[i].Pos == chunkPos)
                {
                    _dirtyChunks[i] = new ChunkToMeshInfo(chunkPos, snapshot.Value, priority || _dirtyChunks[i].Priority);
                    return true;
                }
            }

            _dirtyChunks.Add(new ChunkToMeshInfo(chunkPos, snapshot.Value, priority));
            return true;
        }

        if (priority)
        {
            _meshGenerator.Promote(chunkPos);
        }

        return false;
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

        if (GLManager.DrawTargetOrNull is not WebGpuDrawTarget target) return false;
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
        var fog = GLManager.Fog;
        var light = GLManager.WorldLight;

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
        _priorityChunks.Clear();

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

    private struct ChunkToMeshInfo(Vector3D<int> pos, long version, bool priority)
    {
        public readonly Vector3D<int> Pos = pos;
        public readonly long Version = version;
        public readonly bool Priority = priority;
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
