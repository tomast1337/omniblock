using System.Diagnostics;

namespace OmniBlock.Client.Rendering.Chunks;

internal readonly record struct ChunkMeshProfileSnapshot(
    int Workers,
    int Queued,
    int UrgentResults,
    int BackgroundResults,
    long Meshes,
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
    private long _generationTicks;
    private long _geometryTicks;
    private long _meshes;
    private long _queueWaitTicks;
    private long _snapshotCount;
    private long _snapshotTicks;
    private long _uploadCount;
    private long _uploadTicks;
    private long _visibilityTicks;
    private long _finishedToUploadTicks;
    private long _requestToUploadTicks;

    public void RecordSnapshot(long ticks)
    {
        Interlocked.Add(ref _snapshotTicks, ticks);
        Interlocked.Increment(ref _snapshotCount);
    }

    public void RecordQueueWait(long ticks) => Interlocked.Add(ref _queueWaitTicks, ticks);
    public void RecordClassification(long ticks) => Interlocked.Add(ref _classificationTicks, ticks);
    public void RecordGeometry(long ticks) => Interlocked.Add(ref _geometryTicks, ticks);
    public void RecordVisibility(long ticks) => Interlocked.Add(ref _visibilityTicks, ticks);

    public void RecordGeneration(long ticks)
    {
        Interlocked.Add(ref _generationTicks, ticks);
        Interlocked.Increment(ref _meshes);
    }

    public void RecordUpload(long ticks, long finishedToUploadTicks, long requestToUploadTicks)
    {
        Interlocked.Add(ref _uploadTicks, ticks);
        Interlocked.Add(ref _finishedToUploadTicks, finishedToUploadTicks);
        Interlocked.Add(ref _requestToUploadTicks, requestToUploadTicks);
        Interlocked.Increment(ref _uploadCount);
    }

    public ChunkMeshProfileSnapshot Snapshot(int queued, int urgentResults, int backgroundResults, int workers)
    {
        var meshes = Interlocked.Read(ref _meshes);
        var snapshots = Interlocked.Read(ref _snapshotCount);
        var uploads = Interlocked.Read(ref _uploadCount);
        return new ChunkMeshProfileSnapshot(
            workers, queued, urgentResults, backgroundResults, meshes,
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
