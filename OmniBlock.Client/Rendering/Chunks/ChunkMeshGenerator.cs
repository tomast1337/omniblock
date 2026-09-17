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
    public MeshPageBuildResult[] Pages;
    public SectionMeshRebuildPlan RebuildPlan;
    public bool IsLit;
    public ChunkVisibilityStore VisibilityData;
    public Vector3D<int> Pos;
    public long Version;
    public long SectionId;
    public MeshWorkPriority Priority;
    public long RequestedAt;
    public long FinishedAt;
    public MeshLifecycleRequest? Trace;
    public SectionDirtyReason DirtyReasons;
    public bool Cancelled;
    public MeshCancellationReason CancellationReason;
    public double BuildMs;
    public long RetainedBytes;
    public long UploadBytes;

    public readonly void Dispose()
    {
        if (Pages == null) return;
        foreach (var page in Pages) page?.Dispose();
    }
}

internal sealed class MeshPageBuildResult(int page)
{
    public int Page { get; } = page;
    public PooledList<ChunkVertex>? Solid { get; set; }
    public PooledList<ChunkVertex>? Translucent { get; set; }
    public SectionLightModel? SolidLighting { get; set; }
    public SectionLightModel? TranslucentLighting { get; set; }
    public ChunkDirectionalRanges SolidRanges { get; set; }
    public ChunkDirectionalRanges TranslucentRanges { get; set; }

    public void Dispose()
    {
        Solid?.Dispose();
        Translucent?.Dispose();
        Solid = null;
        Translucent = null;
    }
}

internal class ChunkMeshGenerator : IDisposable
{
    internal const long CompletedResultBudgetBytes = 64L * 1024 * 1024;
    internal const long CriticalCompletedResultBudgetBytes = 96L * 1024 * 1024;
    // Roughly one to two ordinary frame intervals of queued worker time per worker. This keeps all
    // workers fed without allowing a cheap count estimate to hide hundreds of expensive jobs.
    internal const double BuildAdmissionMsPerWorker = 24.0;
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
    private readonly ChunkMeshCostModel _costModel = new();
    private readonly MeshPriorityFairness _resultFairness = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly PriorityWorkScheduler<Vector3D<int>, MeshBuildRequest> _work = new();
    private readonly Task[] _workers;
    private readonly MeshLifecycleDiagnostics? _lifecycle;
    private long _completedResultBytes;
    private long _inFlightEstimatedBuildMicros;
    private long _inFlightEstimatedResultBytes;
    private long _buildAdmissionDeferrals;
    private long _uploadAdmissionDeferrals;
    private long _oversizedUploadAdmissions;

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

    public ChunkMeshCostSnapshot CostProfile => _costModel.Snapshot();
    public long CompletedResultBytes => Math.Max(0, Interlocked.Read(ref _completedResultBytes));
    public long InFlightEstimatedResultBytes => Math.Max(0, Interlocked.Read(ref _inFlightEstimatedResultBytes));
    public double InFlightEstimatedBuildMs =>
        Math.Max(0, Interlocked.Read(ref _inFlightEstimatedBuildMicros)) / 1000.0;
    public long BuildAdmissionDeferrals => Interlocked.Read(ref _buildAdmissionDeferrals);
    public long UploadAdmissionDeferrals => Interlocked.Read(ref _uploadAdmissionDeferrals);
    public long OversizedUploadAdmissions => Interlocked.Read(ref _oversizedUploadAdmissions);

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
            ReleaseInFlightEstimate(pending.Estimate);
            pending.Cache.Dispose();
        }
        _shutdown.Dispose();
        _work.Dispose();
        foreach (var queue in new[] { _criticalResults, _foregroundResults, _backgroundResults })
            while (queue.TryDequeue(out var result))
            {
                Interlocked.Add(ref _completedResultBytes, -result.RetainedBytes);
                _lifecycle?.Cancel(result.Trace, MeshCancellationReason.RendererDisposed);
                result.Dispose();
            }
        foreach (var control in _outstanding.Values) control.Dispose();
        _outstanding.Clear();
    }

    public void ResetProfile()
    {
        _profile.Reset();
        _costModel.Reset();
        Interlocked.Exchange(ref _buildAdmissionDeferrals, 0);
        Interlocked.Exchange(ref _uploadAdmissionDeferrals, 0);
        Interlocked.Exchange(ref _oversizedUploadAdmissions, 0);
    }

    public ChunkMeshCostEstimate EstimateCost(SectionMeshRebuildPlan plan, long previousBytesPerPage = 0) =>
        _costModel.Estimate(plan, previousBytesPerPage);

    public double EstimateUploadMs(long bytes) => _costModel.EstimateUploadMs(bytes);

    public bool CanAdmitBuild(in ChunkMeshCostEstimate estimate, MeshWorkPriority priority)
    {
        var predictedBuildMs = InFlightEstimatedBuildMs + estimate.BuildMs;
        var predictedBytes = CompletedResultBytes + InFlightEstimatedResultBytes + estimate.ResultBytes;
        var buildFits = priority == MeshWorkPriority.Critical ||
                        predictedBuildMs <= MaxConcurrentTasks * BuildAdmissionMsPerWorker;
        var bytesFit = predictedBytes <= (priority == MeshWorkPriority.Critical
            ? CriticalCompletedResultBudgetBytes
            : CompletedResultBudgetBytes);
        if (buildFits && bytesFit) return true;

        // One cold/oversized job must still establish observations and make progress. Once any
        // non-critical worker/result cost is retained, subsequent work waits for capacity.
        if (InFlightEstimatedBuildMs <= 0 && InFlightEstimatedResultBytes <= 0 && CompletedResultBytes <= 0)
            return true;
        Interlocked.Increment(ref _buildAdmissionDeferrals);
        return false;
    }

    public void NoteUploadAdmissionDeferred() => Interlocked.Increment(ref _uploadAdmissionDeferrals);
    public void NoteOversizedUploadAdmission() => Interlocked.Increment(ref _oversizedUploadAdmissions);

    public bool TryPeekMesh(out MeshBuildResult result, out MeshWorkPriority priority)
    {
        var hasCritical = !_criticalResults.IsEmpty;
        var hasForeground = !_foregroundResults.IsEmpty;
        var hasBackground = !_backgroundResults.IsEmpty;
        if (!hasCritical && !hasForeground && !hasBackground)
        {
            result = default;
            priority = default;
            return false;
        }

        priority = _resultFairness.Peek(hasCritical, hasForeground, hasBackground);
        return ResultQueueFor(priority).TryPeek(out result);
    }

    public bool TryPeekMesh(MeshWorkPriority priority, out MeshBuildResult result) =>
        ResultQueueFor(priority).TryPeek(out result);

    public bool TryDequeueMesh(out MeshBuildResult result)
    {
        result = default;
        if (!TryPeekMesh(out _, out var priority) ||
            !TryDequeueMesh(priority, advanceFairness: true, out result)) return false;
        return true;
    }

    /// <summary>Takes one completed result from an exact lane for the renderer's critical reserve.</summary>
    public bool TryDequeueMesh(MeshWorkPriority priority, out MeshBuildResult result)
        => TryDequeueMesh(priority, advanceFairness: false, out result);

    public bool TryDequeueMesh(
        MeshWorkPriority priority,
        bool advanceFairness,
        out MeshBuildResult result)
    {
        if (!ResultQueueFor(priority).TryDequeue(out result)) return false;
        if (advanceFairness) _resultFairness.Commit(priority);
        Interlocked.Add(ref _completedResultBytes, -result.RetainedBytes);
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
        long sectionId = 0,
        SectionMeshRebuildPlan rebuildPlan = default,
        ChunkMeshCostEstimate estimate = default)
    {
        if (rebuildPlan.PageMask == 0) rebuildPlan = SectionMeshRebuildPlan.Full;
        if (estimate.BuildMs <= 0 || estimate.ResultBytes <= 0)
            estimate = _costModel.Estimate(rebuildPlan);
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
            Stopwatch.GetTimestamp(), trace, sectionId, rebuildPlan, control, estimate);
        if (!_outstanding.TryAdd(pos, control))
        {
            _lifecycle?.Cancel(trace, MeshCancellationReason.DuplicateRequest);
            control.Dispose();
            cache.Dispose();
            return;
        }

        // Record before publishing to workers, otherwise Building could race ahead of WorkerQueued.
        _lifecycle?.Move(trace, MeshLifecycleStage.WorkerQueued);
        Interlocked.Add(ref _inFlightEstimatedBuildMicros, ToMicros(estimate.BuildMs));
        Interlocked.Add(ref _inFlightEstimatedResultBytes, estimate.ResultBytes);
        if (!_work.Enqueue(pos, request, priority))
        {
            ReleaseInFlightEstimate(estimate);
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
                        request.RebuildPlan, request.Control.Token);
                    request.Control.Token.ThrowIfCancellationRequested();
                    mesh.Priority = request.Control.Priority;
                    mesh.RequestedAt = request.RequestedAt;
                    mesh.FinishedAt = Stopwatch.GetTimestamp();
                    mesh.Trace = request.Trace;
                    mesh.DirtyReasons = request.Trace?.Reasons ?? SectionDirtyReason.None;
                    mesh.SectionId = request.SectionId;
                    _costModel.RecordBuild(
                        mesh.BuildMs,
                        request.RebuildPlan.PageBuildCount,
                        mesh.RetainedBytes);
                    _lifecycle?.Move(request.Trace, MeshLifecycleStage.AwaitingUpload);
                    Interlocked.Add(ref _completedResultBytes, mesh.RetainedBytes);
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
                    ReleaseInFlightEstimate(request.Estimate);
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
        SectionMeshRebuildPlan rebuildPlan, CancellationToken cancellationToken)
    {
        var generationStart = Stopwatch.GetTimestamp();
        var result = new MeshBuildResult
        {
            Pos = pos,
            Version = version,
            RebuildPlan = rebuildPlan,
            Pages = new MeshPageBuildResult[rebuildPlan.PageBuildCount]
        };

        try
        {
            var classificationTicks = 0L;
            var geometryTicks = 0L;
            var resultIndex = 0;
            for (var page = 0; page < SectionMeshRebuildPlan.PageCount; page++)
            {
                if (!rebuildPlan.Includes(page)) continue;
                result.Pages[resultIndex++] = GeneratePage(
                    pos, page, cache, alternateBlocks, cancellationToken,
                    ref classificationTicks, ref geometryTicks);
            }

            _profile.RecordClassification(classificationTicks);
            _profile.RecordGeometry(geometryTicks);

            result.IsLit = cache.IsLit;
            cancellationToken.ThrowIfCancellationRequested();
            var visibilityStart = Stopwatch.GetTimestamp();
            result.VisibilityData = ChunkVisibilityComputer.Compute(cache, pos.X, pos.Y, pos.Z);
            cancellationToken.ThrowIfCancellationRequested();
            _profile.RecordVisibility(Stopwatch.GetTimestamp() - visibilityStart);
            var generationTicks = Stopwatch.GetTimestamp() - generationStart;
            result.BuildMs = generationTicks * 1000.0 / Stopwatch.Frequency;
            (result.RetainedBytes, result.UploadBytes) = MeasureResultBytes(result.Pages);
            _profile.RecordGeneration(generationTicks, rebuildPlan);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static MeshPageBuildResult GeneratePage(
        Vector3D<int> pos,
        int page,
        WorldRegionSnapshot cache,
        bool alternateBlocks,
        CancellationToken cancellationToken,
        ref long classificationTicks,
        ref long geometryTicks)
    {
        var minX = pos.X;
        var minY = pos.Y + page * SectionMeshRebuildPlan.PageHeight;
        var minZ = pos.Z;
        var maxX = pos.X + SubChunkRenderer.Size;
        var maxY = minY + SectionMeshRebuildPlan.PageHeight;
        var maxZ = pos.Z + SubChunkRenderer.Size;
        var pageResult = new MeshPageBuildResult(page);

        // Full 1x1x1 Standard blocks (minus grass and texture variance) are classified only in
        // the selected page. The compact page-local eligibility table avoids restoring a
        // section-sized allocation on the partial-rebuild path.
        Block?[] greedyEligible =
            new Block[SubChunkRenderer.Size * SubChunkRenderer.Size * SectionMeshRebuildPlan.PageHeight];
        var classificationStart = Stopwatch.GetTimestamp();
        for (var y = minY; y < maxY; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var z = minZ; z < maxZ; z++)
            for (var x = minX; x < maxX; x++)
            {
                if (TryGetGreedyEligibleBlock(cache, x, y, z, alternateBlocks, out var eligible))
                    greedyEligible[PageLocalIndex(x - pos.X, y - minY, z - pos.Z)] = eligible;
            }
        }
        classificationTicks += Stopwatch.GetTimestamp() - classificationStart;

        var geometryStart = Stopwatch.GetTimestamp();
        try
        {
            for (var pass = 0; pass < 2; pass++)
            {
                var hasNextPass = false;
                using var mesh = new ChunkMeshBuilder();
                mesh.Begin(-pos.X, -pos.Y, -pos.Z);
                var ctx = new BlockRenderContext(cache, cache.ContentBlocks, mesh, cache);

                if (pass == 0)
                    EmitGreedyMesh(cache, ctx, mesh, greedyEligible, pos.X, pos.Y, pos.Z,
                        minY, maxY, cancellationToken);

                for (var y = minY; y < maxY; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (var z = minZ; z < maxZ; z++)
                    for (var x = minX; x < maxX; x++)
                    {
                        var id = cache.GetBlockId(x, y, z);
                        if (id <= 0) continue;

                        var block = cache.ContentBlocks.GetByProtocolId(id);
                        if (block.RenderLayer != pass)
                        {
                            hasNextPass = true;
                        }
                        else if (pass != 0 ||
                                 greedyEligible[PageLocalIndex(x - pos.X, y - minY, z - pos.Z)] is null)
                        {
                            BlockRenderer.RenderBlockByRenderType(
                                cache, cache.ContentBlocks, cache, block, new BlockPos(x, y, z), mesh,
                                doVariance: alternateBlocks);
                        }
                    }
                }

                var vertices = mesh.Finish(out var lights, out var ranges);
                try
                {
                    if (vertices.Count > 0)
                    {
                        if (pass == 0)
                        {
                            pageResult.Solid = vertices;
                            pageResult.SolidRanges = ranges;
                            pageResult.SolidLighting = SectionLightModel.Create(pos, vertices.Span, lights.Span);
                        }
                        else
                        {
                            pageResult.Translucent = vertices;
                            pageResult.TranslucentRanges = ranges;
                            pageResult.TranslucentLighting = SectionLightModel.Create(pos, vertices.Span, lights.Span);
                        }
                    }
                    else
                    {
                        vertices.Dispose();
                    }
                }
                finally
                {
                    lights.Dispose();
                }

                if (!hasNextPass) break;
            }

            return pageResult;
        }
        catch
        {
            pageResult.Dispose();
            throw;
        }
        finally
        {
            geometryTicks += Stopwatch.GetTimestamp() - geometryStart;
        }
    }

    public void RecordUpload(
        long elapsedTicks,
        long finishedToUploadTicks,
        long requestToUploadTicks,
        long uploadBytes)
    {
        _profile.RecordUpload(elapsedTicks, finishedToUploadTicks, requestToUploadTicks);
        _costModel.RecordUpload(elapsedTicks * 1000.0 / Stopwatch.Frequency, uploadBytes);
    }

    private static (long RetainedBytes, long UploadBytes) MeasureResultBytes(
        MeshPageBuildResult[] pages)
    {
        long retained = 0;
        long upload = 0;
        foreach (var page in pages)
        {
            if (page == null) continue;
            Add(page.Solid, page.SolidLighting);
            Add(page.Translucent, page.TranslucentLighting);
        }
        return (retained, upload);

        void Add(PooledList<ChunkVertex>? vertices, SectionLightModel? lighting)
        {
            if (vertices == null) return;
            // ArrayPool may retain a larger bucket than the logical vertex count. Admission is a
            // memory-safety mechanism, so account for the owned array rather than only bytes that
            // will be copied to the GPU.
            retained += (long)vertices.Buffer.Length * Unsafe.SizeOf<ChunkVertex>();
            upload += (long)vertices.Count * Unsafe.SizeOf<ChunkVertex>();
            if (lighting == null) return;
            retained += lighting.RetainedBytes;
            upload += lighting.InitialUploadBytes;
        }
    }

    private void ReleaseInFlightEstimate(in ChunkMeshCostEstimate estimate)
    {
        Interlocked.Add(ref _inFlightEstimatedBuildMicros, -ToMicros(estimate.BuildMs));
        Interlocked.Add(ref _inFlightEstimatedResultBytes, -estimate.ResultBytes);
    }

    private static long ToMicros(double milliseconds) =>
        checked((long)Math.Ceiling(Math.Max(0, milliseconds) * 1000.0));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int PageLocalIndex(int lx, int ly, int lz) =>
        (lx * SubChunkRenderer.Size + lz) * SectionMeshRebuildPlan.PageHeight + ly;

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
        Block?[] eligible, int minX, int sectionMinY, int minZ,
        int pageMinY, int pageMaxY, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EmitGreedyTop(cache, ctx, tess, eligible, minX, sectionMinY, minZ, pageMinY, pageMaxY);
        cancellationToken.ThrowIfCancellationRequested();
        EmitGreedyBottom(cache, ctx, tess, eligible, minX, sectionMinY, minZ, pageMinY, pageMaxY);
        cancellationToken.ThrowIfCancellationRequested();
        EmitGreedyEast(cache, ctx, tess, eligible, minX, sectionMinY, minZ, pageMinY, pageMaxY);
        cancellationToken.ThrowIfCancellationRequested();
        EmitGreedyWest(cache, ctx, tess, eligible, minX, sectionMinY, minZ, pageMinY, pageMaxY);
        cancellationToken.ThrowIfCancellationRequested();
        EmitGreedyNorth(cache, ctx, tess, eligible, minX, sectionMinY, minZ, pageMinY, pageMaxY);
        cancellationToken.ThrowIfCancellationRequested();
        EmitGreedySouth(cache, ctx, tess, eligible, minX, sectionMinY, minZ, pageMinY, pageMaxY);
    }

    private static void EmitGreedyTop(WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess, Block?[] eligible, int minX, int sectionMinY, int minZ, int pageMinY, int pageMaxY)
    {
        var size = SubChunkRenderer.Size;
        var grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (var y = pageMinY; y < pageMaxY; y++)
        {
            var localY = y - pageMinY;
            Array.Clear(grid);

            for (var v = 0; v < size; v++)
            {
                var z = minZ + v;
                for (var u = 0; u < size; u++)
                {
                    var x = minX + u;
                    if (eligible[PageLocalIndex(u, localY, v)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x, y + 1, z, Side.Up)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeTopFaceLight(block, new BlockPos(x, y, z));
                    var textureId = block.GetTextureId(cache, x, y, z, Side.Up);
                    var layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    var tint = block.GetColorMultiplier(cache, x, y, z);

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, size, rects);

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
                EmitMergedQuad(tess, Side.Up, key, flipped, TopShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedyBottom(WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess, Block?[] eligible, int minX, int sectionMinY, int minZ, int pageMinY, int pageMaxY)
    {
        var size = SubChunkRenderer.Size;
        var grid = new FaceMergeKey?[size * size];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (var y = pageMinY; y < pageMaxY; y++)
        {
            var localY = y - pageMinY;
            Array.Clear(grid);

            for (var v = 0; v < size; v++)
            {
                var z = minZ + v;
                for (var u = 0; u < size; u++)
                {
                    var x = minX + u;
                    if (eligible[PageLocalIndex(u, localY, v)] is not { } block) continue;
                    if (!block.IsSideVisible(cache, x, y - 1, z, Side.Down)) continue;

                    var (v0, v1, v2, v3, _) = ctx.ComputeBottomFaceLight(block, new BlockPos(x, y, z));
                    var textureId = block.GetTextureId(cache, x, y, z, Side.Down);
                    var layer = Atlases.Terrain.LayerOfGridIndex(textureId);
                    var tint = block.TextureId != 3 ? block.GetColorMultiplier(cache, x, y, z) : 0xFFFFFF;

                    grid[v * size + u] = new FaceMergeKey(layer, tint, v0, v1, v2, v3);
                }
            }

            rects.Clear();
            GreedyMergeLayer(grid, size, size, rects);

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
                EmitMergedQuad(tess, Side.Down, key, flipped, BottomShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedyEast(WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess, Block?[] eligible, int minX, int sectionMinY, int minZ, int pageMinY, int pageMaxY)
    {
        var size = SubChunkRenderer.Size;
        var pageHeight = pageMaxY - pageMinY;
        var grid = new FaceMergeKey?[size * pageHeight];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (var depth = 0; depth < size; depth++)
        {
            var z = minZ + depth;
            Array.Clear(grid);

            for (var v = 0; v < pageHeight; v++)
            {
                var y = pageMinY + v;
                var localY = y - pageMinY;
                for (var u = 0; u < size; u++)
                {
                    var x = minX + u;
                    if (eligible[PageLocalIndex(u, localY, depth)] is not { } block) continue;
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
            GreedyMergeLayer(grid, size, pageHeight, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float x0 = minX + u0, x1 = x0 + w;
                float y0 = pageMinY + v0i, y1 = y0 + h;
                float zFace = z;

                // DrawEastFace's TopLeft/BottomLeft/BottomRight/TopRight land on v1/v2/v3/v0 (see
                // DrawBlock's EAST FACE branch: AssignVertexColors(v1, v2, v3, v0, ...)) — one step
                // rotated from the raw quadrant order the FaceMergeKey stores its four corners in.
                QuadCorner tl = new(x1, y1, zFace, 0, 0, key.L1);
                QuadCorner bl = new(x1, y0, zFace, 0, h, key.L2);
                QuadCorner br = new(x0, y0, zFace, w, h, key.L3);
                QuadCorner tr = new(x0, y1, zFace, w, 0, key.L0);

                var flipped = key.L1.FlipWeight + key.L3.FlipWeight > key.L2.FlipWeight + key.L0.FlipWeight;
                EmitMergedQuad(tess, Side.North, key, flipped, EastShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedyWest(WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess, Block?[] eligible, int minX, int sectionMinY, int minZ, int pageMinY, int pageMaxY)
    {
        var size = SubChunkRenderer.Size;
        var pageHeight = pageMaxY - pageMinY;
        var grid = new FaceMergeKey?[size * pageHeight];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (var depth = 0; depth < size; depth++)
        {
            var z = minZ + depth;
            Array.Clear(grid);

            for (var v = 0; v < pageHeight; v++)
            {
                var y = pageMinY + v;
                var localY = y - pageMinY;
                for (var u = 0; u < size; u++)
                {
                    var x = minX + u;
                    if (eligible[PageLocalIndex(u, localY, depth)] is not { } block) continue;
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
            GreedyMergeLayer(grid, size, pageHeight, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float x0 = minX + u0, x1 = x0 + w;
                float y0 = pageMinY + v0i, y1 = y0 + h;
                float zFace = z + 1;

                QuadCorner tl = new(x0, y1, zFace, 0, 0, key.L0);
                QuadCorner bl = new(x0, y0, zFace, 0, h, key.L1);
                QuadCorner br = new(x1, y0, zFace, w, h, key.L2);
                QuadCorner tr = new(x1, y1, zFace, w, 0, key.L3);

                var flipped = key.L0.FlipWeight + key.L2.FlipWeight > key.L1.FlipWeight + key.L3.FlipWeight;
                EmitMergedQuad(tess, Side.South, key, flipped, WestShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedyNorth(WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess, Block?[] eligible, int minX, int sectionMinY, int minZ, int pageMinY, int pageMaxY)
    {
        var size = SubChunkRenderer.Size;
        var pageHeight = pageMaxY - pageMinY;
        var grid = new FaceMergeKey?[size * pageHeight];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (var depth = 0; depth < size; depth++)
        {
            var x = minX + depth;
            Array.Clear(grid);

            for (var v = 0; v < pageHeight; v++)
            {
                var y = pageMinY + v;
                var localY = y - pageMinY;
                for (var u = 0; u < size; u++)
                {
                    var z = minZ + u;
                    if (eligible[PageLocalIndex(depth, localY, u)] is not { } block) continue;
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
            GreedyMergeLayer(grid, size, pageHeight, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float z0 = minZ + u0, z1 = z0 + w;
                float y0 = pageMinY + v0i, y1 = y0 + h;
                float xFace = x;

                // Same one-step rotation as EmitGreedyEast — DrawNorthFace's corners land on
                // v1/v2/v3/v0 (DrawBlock's NORTH FACE: AssignVertexColors(v1, v2, v3, v0, ...)).
                QuadCorner tl = new(xFace, y1, z0, 0, 0, key.L1);
                QuadCorner bl = new(xFace, y0, z0, 0, h, key.L2);
                QuadCorner br = new(xFace, y0, z1, w, h, key.L3);
                QuadCorner tr = new(xFace, y1, z1, w, 0, key.L0);

                var flipped = key.L1.FlipWeight + key.L3.FlipWeight > key.L2.FlipWeight + key.L0.FlipWeight;
                EmitMergedQuad(tess, Side.West, key, flipped, NorthShadow, tl, bl, br, tr);
            }
        }
    }

    private static void EmitGreedySouth(WorldRegionSnapshot cache, BlockRenderContext ctx, IBlockVertexSink tess, Block?[] eligible, int minX, int sectionMinY, int minZ, int pageMinY, int pageMaxY)
    {
        var size = SubChunkRenderer.Size;
        var pageHeight = pageMaxY - pageMinY;
        var grid = new FaceMergeKey?[size * pageHeight];
        List<(int U, int V, int W, int H, FaceMergeKey Key)> rects = [];

        for (var depth = 0; depth < size; depth++)
        {
            var x = minX + depth;
            Array.Clear(grid);

            for (var v = 0; v < pageHeight; v++)
            {
                var y = pageMinY + v;
                var localY = y - pageMinY;
                for (var u = 0; u < size; u++)
                {
                    var z = minZ + u;
                    if (eligible[PageLocalIndex(depth, localY, u)] is not { } block) continue;
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
            GreedyMergeLayer(grid, size, pageHeight, rects);

            foreach (var (u0, v0i, w, h, key) in rects)
            {
                float z0 = minZ + u0, z1 = z0 + w;
                float y0 = pageMinY + v0i, y1 = y0 + h;
                float xFace = x + 1;

                // DrawSouthFace's corners land on v3/v0/v1/v2 (DrawBlock's SOUTH FACE:
                // AssignVertexColors(v3, v0, v1, v2, ...)) — a different rotation from East/North.
                QuadCorner tl = new(xFace, y1, z1, 0, 0, key.L3);
                QuadCorner bl = new(xFace, y0, z1, 0, h, key.L0);
                QuadCorner br = new(xFace, y0, z0, w, h, key.L1);
                QuadCorner tr = new(xFace, y1, z0, w, 0, key.L2);

                var flipped = key.L3.FlipWeight + key.L1.FlipWeight > key.L0.FlipWeight + key.L2.FlipWeight;
                EmitMergedQuad(tess, Side.East, key, flipped, SouthShadow, tl, bl, br, tr);
            }
        }
    }

    /// <summary>
    ///     Standard 2D greedy-rectangle merge over one depth layer: from each unconsumed cell, grow a
    ///     rectangle as wide as the matching run to its right, then as tall as that whole width stays
    ///     matching going down. Consumed cells are nulled out of <paramref name="grid" /> in place, so
    ///     no separate visited mask is needed.
    /// </summary>
    private static void GreedyMergeLayer(FaceMergeKey?[] grid, int width, int height, List<(int U, int V, int W, int H, FaceMergeKey Key)> rects)
    {
        for (var v = 0; v < height; v++)
        {
            for (var u = 0; u < width; u++)
            {
                var idx = v * width + u;
                if (grid[idx] is not { } key) continue;

                var w = 1;
                while (u + w < width && grid[v * width + u + w] is { } wk && wk.Equals(key)) w++;

                var h = 1;
                var canExpand = true;
                while (canExpand && v + h < height)
                {
                    for (var du = 0; du < w; du++)
                    {
                        if (grid[(v + h) * width + u + du] is not { } hk || !hk.Equals(key))
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
                        grid[(v + dv) * width + u + du] = null;
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
    private static void EmitMergedQuad(IBlockVertexSink tess, Side side, in FaceMergeKey key, bool flipped, float shade, QuadCorner tl, QuadCorner bl, QuadCorner br, QuadCorner tr)
    {
        var r = ((key.TintColor >> 16) & 255) * ColorScale * shade;
        var g = ((key.TintColor >> 8) & 255) * ColorScale * shade;
        var b = (key.TintColor & 255) * ColorScale * shade;

        tess.setArrayLayer(key.ArrayLayer);
        tess.setQuadDirection(side);

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
        SectionMeshRebuildPlan RebuildPlan,
        MeshBuildCancellation Control,
        ChunkMeshCostEstimate Estimate);

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
