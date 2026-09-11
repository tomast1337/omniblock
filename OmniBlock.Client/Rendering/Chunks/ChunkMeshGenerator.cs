using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Textures;
using OmniBlock.Util;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;
using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks;

internal struct MeshBuildResult : IDisposable
{
    public PooledList<ChunkVertex> Solid;
    public PooledList<ChunkVertex> Translucent;
    public SectionLightModel? SolidLighting;
    public SectionLightModel? TranslucentLighting;
    public bool IsLit;
    public ChunkVisibilityStore VisibilityData;
    public Vector3D<int> Pos;
    public long Version;
    public long SectionId;
    public MeshWorkPriority Priority;
    public long RequestedAt;
    public long FinishedAt;
    public MeshLifecycleRequest? Trace;
    public bool Cancelled;
    public MeshCancellationReason CancellationReason;

    public readonly void Dispose()
    {
        Solid?.Dispose();
        Translucent?.Dispose();
    }
}

internal class ChunkMeshGenerator : IDisposable
{
    private const float ColorScale = 0.0039215686F;
    private const float TopShadow = 1.0F;
    private const float BottomShadow = 0.5F;
    private const float EastShadow = 0.8F;
    private const float WestShadow = 0.8F;
    private const float NorthShadow = 0.6F;
    private const float SouthShadow = 0.6F;

    /// <summary>
    ///     Grass draws a second, biome-tinted overlay quad over its side texture — a shape a single
    ///     merged quad can't represent — so any block using this texture is excluded from the greedy
    ///     path entirely and falls back to the ordinary per-block draw.
    /// </summary>
    private static readonly int s_grassSideTextureId = Atlases.Terrain.IndexOf("omniblock:grass_block_side");

    private readonly ConcurrentQueue<MeshBuildResult> _backgroundResults = new();
    private readonly ConcurrentQueue<MeshBuildResult> _criticalResults = new();
    private readonly ConcurrentQueue<MeshBuildResult> _foregroundResults = new();

    private readonly ILogger<ChunkMeshGenerator> _logger = Log.Instance.For<ChunkMeshGenerator>();
    private readonly ConcurrentDictionary<Vector3D<int>, MeshBuildCancellation> _outstanding = new();
    private readonly ChunkMeshProfiler _profile = new();
    private readonly MeshPriorityFairness _resultFairness = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly PriorityWorkScheduler<Vector3D<int>, MeshBuildRequest> _work = new();
    private readonly Task[] _workers;
    private readonly MeshLifecycleDiagnostics? _lifecycle;

    public ChunkMeshGenerator(ushort maxConcurrentTasks = 0, MeshLifecycleDiagnostics? lifecycle = null)
    {
        _lifecycle = lifecycle;
        MaxConcurrentTasks = maxConcurrentTasks == 0 ? (ushort)1 : maxConcurrentTasks;
        _workers = new Task[MaxConcurrentTasks];
        for (var i = 0; i < _workers.Length; i++)
            _workers[i] = Task.Run(WorkerLoop);
    }

    public ushort MaxConcurrentTasks { get; }

    public ChunkMeshProfileSnapshot Profile => _profile.Snapshot(
        _work.Count, _outstanding.Count,
        _criticalResults.Count, _foregroundResults.Count, _backgroundResults.Count,
        MaxConcurrentTasks);

    public void Dispose()
    {
        foreach (var control in _outstanding.Values)
            control.Cancel(MeshCancellationReason.RendererDisposed, MeshWorkPriority.Critical);
        _shutdown.Cancel();
        try
        {
            Task.WaitAll(_workers);
        }
        catch (AggregateException ex) when (ex.InnerExceptions.All(static e => e is TaskCanceledException or OperationCanceledException))
        {
        }

        foreach (var pending in _work.Drain())
        {
            _lifecycle?.Cancel(pending.Trace, MeshCancellationReason.RendererDisposed);
            CompleteOutstanding(pending.Pos);
            pending.Cache.Dispose();
        }
        _shutdown.Dispose();
        _work.Dispose();
        foreach (var queue in new[] { _criticalResults, _foregroundResults, _backgroundResults })
            while (queue.TryDequeue(out var result))
            {
                _lifecycle?.Cancel(result.Trace, MeshCancellationReason.RendererDisposed);
                result.Dispose();
            }
        foreach (var control in _outstanding.Values) control.Dispose();
        _outstanding.Clear();
    }

    public void ResetProfile() => _profile.Reset();

    public bool TryDequeueMesh(out MeshBuildResult result)
    {
        var priority = _resultFairness.Select(
            !_criticalResults.IsEmpty,
            !_foregroundResults.IsEmpty,
            !_backgroundResults.IsEmpty);
        if (ResultQueueFor(priority).TryDequeue(out result))
        {
            CompleteOutstanding(result.Pos);
            return true;
        }

        // Producers may enqueue between the availability snapshot and the selected dequeue.
        // Falling through all lanes keeps that harmless race from looking like an empty result set.
        var found = _criticalResults.TryDequeue(out result)
                    || _foregroundResults.TryDequeue(out result)
                    || _backgroundResults.TryDequeue(out result);
        if (found) CompleteOutstanding(result.Pos);
        return found;
    }

    /// <summary>Takes one completed result from an exact lane for the renderer's critical reserve.</summary>
    public bool TryDequeueMesh(MeshWorkPriority priority, out MeshBuildResult result)
    {
        if (!ResultQueueFor(priority).TryDequeue(out result)) return false;
        CompleteOutstanding(result.Pos);
        return true;
    }

    private void CompleteOutstanding(Vector3D<int> pos)
    {
        if (_outstanding.TryRemove(pos, out var control)) control.Dispose();
    }

    public bool HasOutstanding(Vector3D<int> pos) => _outstanding.ContainsKey(pos);

    public bool HasOutstandingAtPriority(Vector3D<int> pos, MeshWorkPriority priority) =>
        _outstanding.TryGetValue(pos, out var control) && control.Priority >= priority;

    public bool CancelObsolete(
        Vector3D<int> pos,
        MeshWorkPriority replacementPriority,
        MeshCancellationReason reason = MeshCancellationReason.Superseded)
    {
        if (!_outstanding.TryGetValue(pos, out var control)) return false;
        var cancelled = control.Cancel(reason, replacementPriority);
        _work.Promote(pos, replacementPriority);
        return cancelled || control.IsCancellationRequested;
    }

    //TODO: Make a chunk mesh config struct for alternateBlocks and other flags
    public void MeshChunk(
        World world,
        Vector3D<int> pos,
        long version,
        bool alternateBlocks,
        MeshWorkPriority priority = MeshWorkPriority.Background,
        MeshLifecycleRequest? trace = null,
        long sectionId = 0)
    {
        var requestedAt = Stopwatch.GetTimestamp();
        _lifecycle?.Move(trace, MeshLifecycleStage.Snapshotting, priority);
        // 1 block of padding on every side of the 16-block sub-chunk (18x18x18 total) — exactly
        // what face culling and AO need to look at a block's immediate neighbours.
        WorldRegionSnapshot cache;
        try
        {
            cache = new(
                world,
                pos.X - 1, pos.Y - 1, pos.Z - 1,
                pos.X + SubChunkRenderer.Size, pos.Y + SubChunkRenderer.Size, pos.Z + SubChunkRenderer.Size
            );
        }
        catch
        {
            _lifecycle?.Cancel(trace, MeshCancellationReason.SnapshotFailed);
            throw;
        }
        _profile.RecordSnapshot(Stopwatch.GetTimestamp() - requestedAt);

        var control = new MeshBuildCancellation(priority);
        var request = new MeshBuildRequest(pos, version, cache, alternateBlocks, requestedAt,
            Stopwatch.GetTimestamp(), trace, sectionId, control);
        if (!_outstanding.TryAdd(pos, control))
        {
            _lifecycle?.Cancel(trace, MeshCancellationReason.DuplicateRequest);
            control.Dispose();
            cache.Dispose();
            return;
        }

        // Record before publishing to workers, otherwise Building could race ahead of WorkerQueued.
        _lifecycle?.Move(trace, MeshLifecycleStage.WorkerQueued);
        if (!_work.Enqueue(pos, request, priority))
        {
            _lifecycle?.Cancel(trace, MeshCancellationReason.DuplicateRequest);
            _outstanding.TryRemove(pos, out _);
            control.Dispose();
            cache.Dispose();
        }
    }

    public bool Promote(Vector3D<int> pos, MeshWorkPriority priority) => _work.Promote(pos, priority);

    public void Reprioritize(Vector3D<double> viewPosition, Vector3D<double> predictedViewPosition)
    {
        _work.ReorderValuesWithinPriorities((left, right) =>
        {
            var deadline = (left.Trace?.DeadlineFrame ?? int.MaxValue)
                .CompareTo(right.Trace?.DeadlineFrame ?? int.MaxValue);
            if (deadline != 0) return deadline;
            var leftDistance = DistanceToEither(left.Pos, viewPosition, predictedViewPosition);
            var rightDistance = DistanceToEither(right.Pos, viewPosition, predictedViewPosition);
            return leftDistance.CompareTo(rightDistance);
        });
    }

    private static double DistanceToEither(
        Vector3D<int> position,
        Vector3D<double> viewPosition,
        Vector3D<double> predictedViewPosition)
    {
        var meshPosition = new Vector3D<double>(position.X, position.Y, position.Z);
        return Math.Min(
            Vector3D.DistanceSquared(meshPosition, viewPosition),
            Vector3D.DistanceSquared(meshPosition, predictedViewPosition));
    }

    private async Task WorkerLoop()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                var (request, priority) = await _work.TakeAsync(_shutdown.Token);
                _profile.RecordQueueWait(Stopwatch.GetTimestamp() - request.EnqueuedAt);
                var buildStarted = false;
                try
                {
                    request.Control.Token.ThrowIfCancellationRequested();
                    buildStarted = true;
                    _lifecycle?.Move(request.Trace, MeshLifecycleStage.Building, priority);
                    var mesh = GenerateMesh(
                        request.Pos, request.Version, request.Cache, request.AlternateBlocks,
                        request.Control.Token);
                    request.Control.Token.ThrowIfCancellationRequested();
                    mesh.Priority = request.Control.Priority;
                    mesh.RequestedAt = request.RequestedAt;
                    mesh.FinishedAt = Stopwatch.GetTimestamp();
                    mesh.Trace = request.Trace;
                    mesh.SectionId = request.SectionId;
                    _lifecycle?.Move(request.Trace, MeshLifecycleStage.AwaitingUpload);
                    ResultQueueFor(mesh.Priority).Enqueue(mesh);
                }
                catch (OperationCanceledException) when (request.Control.IsCancellationRequested)
                {
                    var reason = request.Control.Reason;
                    _lifecycle?.ObserveCancellation(request.Trace, buildStarted, reason);
                    ResultQueueFor(request.Control.Priority).Enqueue(new MeshBuildResult
                    {
                        Pos = request.Pos,
                        Version = request.Version,
                        SectionId = request.SectionId,
                        Priority = request.Control.Priority,
                        RequestedAt = request.RequestedAt,
                        FinishedAt = Stopwatch.GetTimestamp(),
                        Trace = request.Trace,
                        Cancelled = true,
                        CancellationReason = reason
                    });
                }
                catch (Exception ex)
                {
                    _lifecycle?.Cancel(request.Trace, MeshCancellationReason.BuildFailed);
                    CompleteOutstanding(request.Pos);
                    _logger.LogError(ex, "Error generating chunk mesh at {Pos}", request.Pos);
                }
                finally
                {
                    request.Cache.Dispose();
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private ConcurrentQueue<MeshBuildResult> ResultQueueFor(MeshWorkPriority priority) => priority switch
    {
        MeshWorkPriority.Critical => _criticalResults,
        MeshWorkPriority.Foreground => _foregroundResults,
        _ => _backgroundResults
    };

    private MeshBuildResult GenerateMesh(
        Vector3D<int> pos, long version, WorldRegionSnapshot cache, bool alternateBlocks,
        CancellationToken cancellationToken)
    {
        var generationStart = Stopwatch.GetTimestamp();
        var minX = pos.X;
        var minY = pos.Y;
        var minZ = pos.Z;
        var maxX = pos.X + SubChunkRenderer.Size;
        var maxY = pos.Y + SubChunkRenderer.Size;
        var maxZ = pos.Z + SubChunkRenderer.Size;

        var result = new MeshBuildResult
        {
            Pos = pos,
            Version = version
        };

        try
        {
        // Full 1x1x1 Standard blocks (minus grass, minus anything using texture variance) are
        // pulled out of the per-block loop below and merged into larger quads instead — see
        // EmitGreedyMesh. Precomputed once so the sweep and the loop's skip check agree on
        // exactly which cells were handled the fast way.
        Block?[] greedyEligible = new Block[SubChunkRenderer.Size * SubChunkRenderer.Size * SubChunkRenderer.Size];
        var classificationStart = Stopwatch.GetTimestamp();
        for (var y = minY; y < maxY; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var z = minZ; z < maxZ; z++)
            {
                for (var x = minX; x < maxX; x++)
                {
                    if (TryGetGreedyEligibleBlock(cache, x, y, z, alternateBlocks, out var eligible))
                    {
                        greedyEligible[LocalIndex(x - minX, y - minY, z - minZ)] = eligible;
                    }
                }
            }
        }

        _profile.RecordClassification(Stopwatch.GetTimestamp() - classificationStart);

        var geometryStart = Stopwatch.GetTimestamp();
        for (var pass = 0; pass < 2; pass++)
        {
            var hasNextPass = false;
            using var mesh = new ChunkMeshBuilder();
            mesh.Begin(-pos.X, -pos.Y, -pos.Z);
            var ctx = new BlockRenderContext(cache, cache.ContentBlocks, mesh, cache);

            if (pass == 0)
            {
                EmitGreedyMesh(cache, ctx, mesh, greedyEligible, minX, minY, minZ, cancellationToken);
            }

            for (var y = minY; y < maxY; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var z = minZ; z < maxZ; z++)
                {
                    for (var x = minX; x < maxX; x++)
                    {
                        var id = cache.GetBlockId(x, y, z);
                        if (id <= 0) continue;

                        var b = cache.ContentBlocks.GetByProtocolId(id);
                        var blockPass = b.RenderLayer;

                        if (blockPass != pass)
                        {
                            hasNextPass = true;
                        }
                        else if (pass != 0 || greedyEligible[LocalIndex(x - minX, y - minY, z - minZ)] is null)
                        {
                            BlockRenderer.RenderBlockByRenderType(cache, cache.ContentBlocks, cache, b, new BlockPos(x, y, z), mesh, doVariance: alternateBlocks);
                        }
                    }
                }
            }

            var verts = mesh.Finish(out var lights);
            try
            {
                if (verts.Count > 0)
                {
                    if (pass == 0)
                    {
                        result.Solid = verts;
                        result.SolidLighting = SectionLightModel.Create(pos, verts.Span, lights.Span);
                    }
                    else
                    {
                        result.Translucent = verts;
                        result.TranslucentLighting = SectionLightModel.Create(pos, verts.Span, lights.Span);
                    }
                }
                else
                {
                    verts.Dispose();
                }
            }
            finally
            {
                lights.Dispose();
            }

            if (!hasNextPass) break;
        }

        _profile.RecordGeometry(Stopwatch.GetTimestamp() - geometryStart);

        result.IsLit = cache.IsLit;
        cancellationToken.ThrowIfCancellationRequested();
        var visibilityStart = Stopwatch.GetTimestamp();
        result.VisibilityData = ChunkVisibilityComputer.Compute(cache, pos.X, pos.Y, pos.Z);
        cancellationToken.ThrowIfCancellationRequested();
        _profile.RecordVisibility(Stopwatch.GetTimestamp() - visibilityStart);
        _profile.RecordGeneration(Stopwatch.GetTimestamp() - generationStart);
        return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    public void RecordUpload(long elapsedTicks, long finishedToUploadTicks, long requestToUploadTicks) =>
        _profile.RecordUpload(elapsedTicks, finishedToUploadTicks, requestToUploadTicks);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int LocalIndex(int lx, int ly, int lz) => (lx * SubChunkRenderer.Size + lz) * SubChunkRenderer.Size + ly;

    /// <summary>
    ///     Whether the block at this cell can take the greedy-meshing fast path: a full 1x1x1
    ///     <see cref="BlockRendererType.Standard" /> block, not grass, and — when texture variance is
    ///     active for this mesh — not using it. Any of those disqualify it because they make two
    ///     otherwise-identical adjacent faces render differently (a partial bounding box changes the
    ///     quad's shape; variance and grass both change which texture/orientation a face gets), which
    ///     a single merged quad can't reproduce.
    /// </summary>
    private static bool TryGetGreedyEligibleBlock(WorldRegionSnapshot cache, int x, int y, int z, bool alternateBlocks, out Block? block)
    {
        var id = cache.GetBlockId(x, y, z);
        if (id <= 0)
        {
            block = null;
            return false;
        }

        var candidate = cache.ContentBlocks.GetByProtocolId(id);
        if (candidate.RenderType != BlockRendererType.Standard || candidate.RenderLayer != 0)
        {
            block = null;
            return false;
        }

        candidate.UpdateBoundingBox(cache, x, y, z);
        var bb = candidate.BoundingBox;
        if (bb.MinX != 0.0 || bb.MinY != 0.0 || bb.MinZ != 0.0 || bb.MaxX != 1.0 || bb.MaxY != 1.0 || bb.MaxZ != 1.0)
        {
            block = null;
            return false;
        }

        if (alternateBlocks &&
            (candidate.TopVariance != TextureVariance.None ||
             candidate.BottomVariance != TextureVariance.None ||
             candidate.SideVariance != TextureVariance.None))
        {
            block = null;
            return false;
        }

        if (candidate.GetTextureId(cache, x, y, z, Side.North) == s_grassSideTextureId)
        {
            block = null;
            return false;
        }

        block = candidate;
        return true;
    }

    private static void EmitGreedyMesh(
        WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess,
        Block?[] eligible, int minX, int minY, int minZ, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EmitGreedyTop(cache, ctx, tess, eligible, minX, minY, minZ);
        cancellationToken.ThrowIfCancellationRequested();
        EmitGreedyBottom(cache, ctx, tess, eligible, minX, minY, minZ);
        cancellationToken.ThrowIfCancellationRequested();
        EmitGreedyEast(cache, ctx, tess, eligible, minX, minY, minZ);
        cancellationToken.ThrowIfCancellationRequested();
        EmitGreedyWest(cache, ctx, tess, eligible, minX, minY, minZ);
        cancellationToken.ThrowIfCancellationRequested();
        EmitGreedyNorth(cache, ctx, tess, eligible, minX, minY, minZ);
        cancellationToken.ThrowIfCancellationRequested();
        EmitGreedySouth(cache, ctx, tess, eligible, minX, minY, minZ);
    }

    private static void EmitGreedyTop(WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        var size = SubChunkRenderer.Size;
        var grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (var depth = 0; depth < size; depth++)
        {
            var y = minY + depth;
            Array.Clear(grid);

            for (var v = 0; v < size; v++)
            {
                var z = minZ + v;
                for (var u = 0; u < size; u++)
                {
                    var x = minX + u;
                    if (eligible[LocalIndex(u, depth, v)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x, y + 1, z, Side.Up)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeTopFaceLight(block, new BlockPos(x, y, z));
                    var textureId = block.GetTextureId(cache, x, y, z, Side.Up);
                    var layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    var tint = block.GetColorMultiplier(cache, x, y, z);

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float x0 = minX + u0, x1 = x0 + w;
                float z0 = minZ + v0i, z1 = z0 + h;
                float yFace = y + 1;

                QuadCorner tl = new(x1, yFace, z1, w, h, key.L0);
                QuadCorner bl = new(x1, yFace, z0, w, 0, key.L1);
                QuadCorner br = new(x0, yFace, z0, 0, 0, key.L2);
                QuadCorner tr = new(x0, yFace, z1, 0, h, key.L3);

                var flipped = key.L0.FlipWeight + key.L2.FlipWeight > key.L1.FlipWeight + key.L3.FlipWeight;
                EmitMergedQuad(tess, key, flipped, TopShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedyBottom(WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        var size = SubChunkRenderer.Size;
        var grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (var depth = 0; depth < size; depth++)
        {
            var y = minY + depth;
            Array.Clear(grid);

            for (var v = 0; v < size; v++)
            {
                var z = minZ + v;
                for (var u = 0; u < size; u++)
                {
                    var x = minX + u;
                    if (eligible[LocalIndex(u, depth, v)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x, y - 1, z, Side.Down)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeBottomFaceLight(block, new BlockPos(x, y, z));
                    var textureId = block.GetTextureId(cache, x, y, z, Side.Down);
                    var layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    var tint = block.TextureId != 3 ? block.GetColorMultiplier(cache, x, y, z) : 0xFFFFFF;

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float x0 = minX + u0, x1 = x0 + w;
                float z0 = minZ + v0i, z1 = z0 + h;
                float yFace = y;

                QuadCorner tl = new(x0, yFace, z1, 0, h, key.L0);
                QuadCorner bl = new(x0, yFace, z0, 0, 0, key.L1);
                QuadCorner br = new(x1, yFace, z0, w, 0, key.L2);
                QuadCorner tr = new(x1, yFace, z1, w, h, key.L3);

                var flipped = key.L0.FlipWeight + key.L2.FlipWeight > key.L1.FlipWeight + key.L3.FlipWeight;
                EmitMergedQuad(tess, key, flipped, BottomShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedyEast(WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        var size = SubChunkRenderer.Size;
        var grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (var depth = 0; depth < size; depth++)
        {
            var z = minZ + depth;
            Array.Clear(grid);

            for (var v = 0; v < size; v++)
            {
                var y = minY + v;
                for (var u = 0; u < size; u++)
                {
                    var x = minX + u;
                    if (eligible[LocalIndex(u, v, depth)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x, y, z - 1, Side.North)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeEastFaceLight(block, new BlockPos(x, y, z));
                    var textureId = block.GetTextureId(cache, x, y, z, Side.North);
                    if (textureId == s_grassSideTextureId) continue;
                    var layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    var tint = block.TextureId != 3 ? block.GetColorMultiplier(cache, x, y, z) : 0xFFFFFF;

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float x0 = minX + u0, x1 = x0 + w;
                float y0 = minY + v0i, y1 = y0 + h;
                float zFace = z;

                // DrawEastFace's TopLeft/BottomLeft/BottomRight/TopRight land on v1/v2/v3/v0 (see
                // DrawBlock's EAST FACE branch: AssignVertexColors(v1, v2, v3, v0, ...)) — one step
                // rotated from the raw quadrant order the FaceMergeKey stores its four corners in.
                QuadCorner tl = new(x1, y1, zFace, 0, 0, key.L1);
                QuadCorner bl = new(x1, y0, zFace, 0, h, key.L2);
                QuadCorner br = new(x0, y0, zFace, w, h, key.L3);
                QuadCorner tr = new(x0, y1, zFace, w, 0, key.L0);

                var flipped = key.L1.FlipWeight + key.L3.FlipWeight > key.L2.FlipWeight + key.L0.FlipWeight;
                EmitMergedQuad(tess, key, flipped, EastShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedyWest(WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        var size = SubChunkRenderer.Size;
        var grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (var depth = 0; depth < size; depth++)
        {
            var z = minZ + depth;
            Array.Clear(grid);

            for (var v = 0; v < size; v++)
            {
                var y = minY + v;
                for (var u = 0; u < size; u++)
                {
                    var x = minX + u;
                    if (eligible[LocalIndex(u, v, depth)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x, y, z + 1, Side.South)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeWestFaceLight(block, new BlockPos(x, y, z));
                    var textureId = block.GetTextureId(cache, x, y, z, Side.South);
                    if (textureId == s_grassSideTextureId) continue;
                    var layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    var tint = block.TextureId != 3 ? block.GetColorMultiplier(cache, x, y, z) : 0xFFFFFF;

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float x0 = minX + u0, x1 = x0 + w;
                float y0 = minY + v0i, y1 = y0 + h;
                float zFace = z + 1;

                QuadCorner tl = new(x0, y1, zFace, 0, 0, key.L0);
                QuadCorner bl = new(x0, y0, zFace, 0, h, key.L1);
                QuadCorner br = new(x1, y0, zFace, w, h, key.L2);
                QuadCorner tr = new(x1, y1, zFace, w, 0, key.L3);

                var flipped = key.L0.FlipWeight + key.L2.FlipWeight > key.L1.FlipWeight + key.L3.FlipWeight;
                EmitMergedQuad(tess, key, flipped, WestShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedyNorth(WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        var size = SubChunkRenderer.Size;
        var grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (var depth = 0; depth < size; depth++)
        {
            var x = minX + depth;
            Array.Clear(grid);

            for (var v = 0; v < size; v++)
            {
                var y = minY + v;
                for (var u = 0; u < size; u++)
                {
                    var z = minZ + u;
                    if (eligible[LocalIndex(depth, v, u)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x - 1, y, z, Side.West)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeNorthFaceLight(block, new BlockPos(x, y, z));
                    var textureId = block.GetTextureId(cache, x, y, z, Side.West);
                    if (textureId == s_grassSideTextureId) continue;
                    var layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    var tint = block.TextureId != 3 ? block.GetColorMultiplier(cache, x, y, z) : 0xFFFFFF;

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float z0 = minZ + u0, z1 = z0 + w;
                float y0 = minY + v0i, y1 = y0 + h;
                float xFace = x;

                // Same one-step rotation as EmitGreedyEast — DrawNorthFace's corners land on
                // v1/v2/v3/v0 (DrawBlock's NORTH FACE: AssignVertexColors(v1, v2, v3, v0, ...)).
                QuadCorner tl = new(xFace, y1, z0, 0, 0, key.L1);
                QuadCorner bl = new(xFace, y0, z0, 0, h, key.L2);
                QuadCorner br = new(xFace, y0, z1, w, h, key.L3);
                QuadCorner tr = new(xFace, y1, z1, w, 0, key.L0);

                var flipped = key.L1.FlipWeight + key.L3.FlipWeight > key.L2.FlipWeight + key.L0.FlipWeight;
                EmitMergedQuad(tess, key, flipped, NorthShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedySouth(WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess, Block?[] eligible, int minX, int minY, int minZ)
    {
        var size = SubChunkRenderer.Size;
        var grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (var depth = 0; depth < size; depth++)
        {
            var x = minX + depth;
            Array.Clear(grid);

            for (var v = 0; v < size; v++)
            {
                var y = minY + v;
                for (var u = 0; u < size; u++)
                {
                    var z = minZ + u;
                    if (eligible[LocalIndex(depth, v, u)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x + 1, y, z, Side.East)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeSouthFaceLight(block, new BlockPos(x, y, z));
                    var textureId = block.GetTextureId(cache, x, y, z, Side.East);
                    if (textureId == s_grassSideTextureId) continue;
                    var layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    var tint = block.TextureId != 3 ? block.GetColorMultiplier(cache, x, y, z) : 0xFFFFFF;

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float z0 = minZ + u0, z1 = z0 + w;
                float y0 = minY + v0i, y1 = y0 + h;
                float xFace = x + 1;

                // DrawSouthFace's corners land on v3/v0/v1/v2 (DrawBlock's SOUTH FACE:
                // AssignVertexColors(v3, v0, v1, v2, ...)) — a different rotation from East/North.
                QuadCorner tl = new(xFace, y1, z1, 0, 0, key.L3);
                QuadCorner bl = new(xFace, y0, z1, 0, h, key.L0);
                QuadCorner br = new(xFace, y0, z0, w, h, key.L1);
                QuadCorner tr = new(xFace, y1, z0, w, 0, key.L2);

                var flipped = key.L3.FlipWeight + key.L1.FlipWeight > key.L0.FlipWeight + key.L2.FlipWeight;
                EmitMergedQuad(tess, key, flipped, SouthShadow, tl, bl, br, tr);
            }
        }
    }

    /// <summary>
    ///     Standard 2D greedy-rectangle merge over one depth layer: from each unconsumed cell, grow a
    ///     rectangle as wide as the matching run to its right, then as tall as that whole width stays
    ///     matching going down. Consumed cells are nulled out of <paramref name="grid" /> in place, so
    ///     no separate visited mask is needed.
    /// </summary>
    private static void GreedyMergeLayer(FaceMergeKey?[] grid, int size, List<(int U, int V, int W, int H, FaceMergeKey Key)> rects)
    {
        for (var v = 0; v < size; v++)
        {
            for (var u = 0; u < size; u++)
            {
                var idx = v * size + u;
                if (grid[idx] is not { } key) continue;

                var w = 1;
                while (u + w < size && grid[v * size + u + w] is { } wk && wk.Equals(key)) w++;

                var h = 1;
                var canExpand = true;
                while (canExpand && v + h < size)
                {
                    for (var du = 0; du < w; du++)
                    {
                        if (grid[(v + h) * size + u + du] is not { } hk || !hk.Equals(key))
                        {
                            canExpand = false;
                            break;
                        }
                    }

                    if (canExpand) h++;
                }

                for (var dv = 0; dv < h; dv++)
                {
                    for (var du = 0; du < w; du++)
                    {
                        grid[(v + dv) * size + u + du] = null;
                    }
                }

                rects.Add((u, v, w, h, key));
            }
        }
    }

    /// <summary>
    ///     Emits one quad in the canonical top-left/bottom-left/bottom-right/top-right winding, or
    ///     that same cycle started one vertex later when <paramref name="flipped" /> — which is all
    ///     "flipped" ever means in the per-block renderer this mirrors: which diagonal the two
    ///     triangles split along, not a different set of corners.
    /// </summary>
    private static void EmitMergedQuad(IBlockVertexSink tess, in FaceMergeKey key, bool flipped, float shade, QuadCorner tl, QuadCorner bl, QuadCorner br, QuadCorner tr)
    {
        var r = ((key.TintColor >> 16) & 255) * ColorScale * shade;
        var g = ((key.TintColor >> 8) & 255) * ColorScale * shade;
        var b = (key.TintColor & 255) * ColorScale * shade;

        tess.setArrayLayer(key.ArrayLayer);

        Span<QuadCorner> corners = [tl, bl, br, tr];
        var start = flipped ? 1 : 0;
        for (var i = 0; i < 4; i++)
        {
            var c = corners[(start + i) % 4];
            tess.setColorOpaque_F(r, g, b);
            tess.setLight(c.Light.Sky, c.Light.Block);
            tess.addVertexWithUV(c.X, c.Y, c.Z, c.U, c.V);
        }
    }

    private readonly record struct MeshBuildRequest(
        Vector3D<int> Pos,
        long Version,
        WorldRegionSnapshot Cache,
        bool AlternateBlocks,
        long RequestedAt,
        long EnqueuedAt,
        MeshLifecycleRequest? Trace,
        long SectionId,
        MeshBuildCancellation Control);

    /// <summary>One corner of a quad about to be emitted: world position, tiled UV, and its light.</summary>
    private readonly record struct QuadCorner(float X, float Y, float Z, float U, float V, CornerLight Light);

    /// <summary>
    ///     What two faces must agree on, bit for bit, to be merged: the same texture, the same tint,
    ///     and identical lighting at all four corners. Requiring the whole tuple rather than just the
    ///     shared edge is conservative — it merges less than a fully general algorithm would across a
    ///     lighting gradient — but it means a merged quad's corners are exactly the value every
    ///     contributing cell already agreed on, with no interpolation to get wrong.
    /// </summary>
    private readonly record struct FaceMergeKey(int ArrayLayer, int TintColor, CornerLight L0, CornerLight L1, CornerLight L2, CornerLight L3);
}
