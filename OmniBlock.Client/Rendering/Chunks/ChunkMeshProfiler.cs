using System.Diagnostics;

namespace OmniBlock.Client.Rendering.Chunks;

internal readonly record struct ChunkMeshProfileSnapshot(
    int Workers,
    int Queued,
    int Outstanding,
    int CriticalResults,
    int ForegroundResults,
    int BackgroundResults,
    long Meshes,
    long Pages,
    long BlockCellsVisited,
    long FullSectionBuilds,
    long PartialSectionBuilds,
    double SnapshotMs,
    double QueueWaitMs,
    double ClassificationMs,
    double GeometryMs,
    double VisibilityMs,
    double GenerationMs,
    double UploadMs,
    double FinishedToUploadMs,
    double RequestToUploadMs);

/// <summary>
///     Aggregated timings for the parallel chunk-mesh pipeline.
/// </summary>
/// <remarks>
///     The common stack profiler intentionally owns only the registered main and server thread
///     contexts. Mesh workers are pooled, concurrent, and unregistered, so <c>Profiler.Begin</c>
///     is a no-op on them and per-worker stacks cannot be merged safely into one hierarchy.
///     This accumulator uses atomic totals to expose comparable per-mesh averages across every
///     worker. Main-thread render scopes remain in the common profiler.
/// </remarks>
internal sealed class ChunkMeshProfiler
{
    private long _classificationTicks;
    private long _finishedToUploadTicks;
    private long _generationTicks;
    private long _geometryTicks;
    private long _meshes;
    private long _pages;
    private long _blockCellsVisited;
    private long _fullSectionBuilds;
    private long _partialSectionBuilds;
    private long _queueWaitTicks;
    private long _requestToUploadTicks;
    private long _snapshotCount;
    private long _snapshotTicks;
    private long _uploadCount;
    private long _uploadTicks;
    private long _visibilityTicks;

    public void RecordSnapshot(long ticks)
    {
        Interlocked.Add(ref _snapshotTicks, ticks);
        Interlocked.Increment(ref _snapshotCount);
    }

    public void RecordQueueWait(long ticks) => Interlocked.Add(ref _queueWaitTicks, ticks);
    public void RecordClassification(long ticks) => Interlocked.Add(ref _classificationTicks, ticks);
    public void RecordGeometry(long ticks) => Interlocked.Add(ref _geometryTicks, ticks);
    public void RecordVisibility(long ticks) => Interlocked.Add(ref _visibilityTicks, ticks);

    public void RecordGeneration(long ticks, SectionMeshRebuildPlan rebuildPlan)
    {
        Interlocked.Add(ref _generationTicks, ticks);
        Interlocked.Add(ref _pages, rebuildPlan.PageBuildCount);
        Interlocked.Add(ref _blockCellsVisited, rebuildPlan.BlockVisitCount);
        if (rebuildPlan.IsFull)
            Interlocked.Increment(ref _fullSectionBuilds);
        else
            Interlocked.Increment(ref _partialSectionBuilds);
        Interlocked.Increment(ref _meshes);
    }

    public void RecordUpload(long ticks, long finishedToUploadTicks, long requestToUploadTicks)
    {
        Interlocked.Add(ref _uploadTicks, ticks);
        Interlocked.Add(ref _finishedToUploadTicks, finishedToUploadTicks);
        Interlocked.Add(ref _requestToUploadTicks, requestToUploadTicks);
        Interlocked.Increment(ref _uploadCount);
    }

    public ChunkMeshProfileSnapshot Snapshot(
        int queued,
        int outstanding,
        int criticalResults,
        int foregroundResults,
        int backgroundResults,
        int workers)
    {
        var meshes = Interlocked.Read(ref _meshes);
        var snapshots = Interlocked.Read(ref _snapshotCount);
        var uploads = Interlocked.Read(ref _uploadCount);
        return new ChunkMeshProfileSnapshot(
            workers, queued, outstanding, criticalResults, foregroundResults, backgroundResults, meshes,
            Interlocked.Read(ref _pages),
            Interlocked.Read(ref _blockCellsVisited),
            Interlocked.Read(ref _fullSectionBuilds),
            Interlocked.Read(ref _partialSectionBuilds),
            AverageMs(_snapshotTicks, snapshots),
            AverageMs(_queueWaitTicks, meshes),
            AverageMs(_classificationTicks, meshes),
            AverageMs(_geometryTicks, meshes),
            AverageMs(_visibilityTicks, meshes),
            AverageMs(_generationTicks, meshes),
            AverageMs(_uploadTicks, uploads),
            AverageMs(_finishedToUploadTicks, uploads),
            AverageMs(_requestToUploadTicks, uploads));
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _classificationTicks, 0);
        Interlocked.Exchange(ref _generationTicks, 0);
        Interlocked.Exchange(ref _geometryTicks, 0);
        Interlocked.Exchange(ref _meshes, 0);
        Interlocked.Exchange(ref _pages, 0);
        Interlocked.Exchange(ref _blockCellsVisited, 0);
        Interlocked.Exchange(ref _fullSectionBuilds, 0);
        Interlocked.Exchange(ref _partialSectionBuilds, 0);
        Interlocked.Exchange(ref _queueWaitTicks, 0);
        Interlocked.Exchange(ref _snapshotCount, 0);
        Interlocked.Exchange(ref _snapshotTicks, 0);
        Interlocked.Exchange(ref _uploadCount, 0);
        Interlocked.Exchange(ref _uploadTicks, 0);
        Interlocked.Exchange(ref _visibilityTicks, 0);
        Interlocked.Exchange(ref _finishedToUploadTicks, 0);
        Interlocked.Exchange(ref _requestToUploadTicks, 0);
    }

    private static double AverageMs(long ticks, long count) =>
        count == 0 ? 0 : ticks * 1000.0 / Stopwatch.Frequency / count;
}
