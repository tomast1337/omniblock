namespace OmniBlock.Client.Rendering.Chunks;

internal readonly record struct ChunkMeshCostEstimate(
    double BuildMs,
    long ResultBytes,
    double UploadMs);

internal readonly record struct ChunkMeshCostSnapshot(
    long BuildSamples,
    long UploadSamples,
    double BuildMsPerPage,
    long ResultBytesPerPage,
    double UploadBaseMs,
    double UploadMsPerMiB);

/// <summary>
///     Small online cost model used only for admission. Actual elapsed-time limits remain the final
///     guard; these estimates prevent obviously expensive work from being admitted after a frame's
///     budget is already effectively spent.
/// </summary>
internal sealed class ChunkMeshCostModel
{
    internal const double ColdBuildMsPerPage = 0.75;
    internal const long ColdResultBytesPerPage = 256 * 1024;
    internal const double ColdUploadBaseMs = 0.04;
    internal const double ColdUploadMsPerMiB = 0.20;
    private const double Alpha = 0.125;
    private const double MiB = 1024.0 * 1024.0;

    private readonly object _gate = new();
    private long _buildSamples;
    private double _buildMsPerPage = ColdBuildMsPerPage;
    private double _resultBytesPerPage = ColdResultBytesPerPage;
    private long _uploadSamples;
    private double _uploadBaseMs = ColdUploadBaseMs;
    private double _uploadMsPerMiB = ColdUploadMsPerMiB;

    public ChunkMeshCostEstimate Estimate(SectionMeshRebuildPlan plan, long previousBytesPerPage = 0)
    {
        var pages = Math.Max(1, plan.PageBuildCount);
        lock (_gate)
        {
            var bytesPerPage = previousBytesPerPage > 0
                ? previousBytesPerPage
                : Math.Max(1.0, _resultBytesPerPage);
            var bytes = checked((long)Math.Ceiling(bytesPerPage * pages));
            return new ChunkMeshCostEstimate(
                Math.Max(0.01, _buildMsPerPage * pages),
                bytes,
                EstimateUploadMsLocked(bytes));
        }
    }

    public double EstimateUploadMs(long bytes)
    {
        lock (_gate) return EstimateUploadMsLocked(bytes);
    }

    public void RecordBuild(double elapsedMs, int pages, long resultBytes)
    {
        if (!double.IsFinite(elapsedMs) || elapsedMs < 0 || pages <= 0 || resultBytes < 0) return;
        lock (_gate)
        {
            _buildMsPerPage = Blend(_buildSamples, _buildMsPerPage, elapsedMs / pages);
            _resultBytesPerPage = Blend(_buildSamples, _resultBytesPerPage, (double)resultBytes / pages);
            _buildSamples++;
        }
    }

    public void RecordUpload(double elapsedMs, long bytes)
    {
        if (!double.IsFinite(elapsedMs) || elapsedMs < 0 || bytes < 0) return;
        lock (_gate)
        {
            // A small fixed component covers allocations and presentation publication. The
            // residual is normalized by at least 1/16 MiB so tiny/empty meshes cannot explode the
            // per-byte estimate.
            var observedBase = Math.Min(elapsedMs, 0.25);
            var observedPerMiB = Math.Max(0, elapsedMs - _uploadBaseMs) /
                                 Math.Max(1.0 / 16.0, bytes / MiB);
            _uploadBaseMs = Blend(_uploadSamples, _uploadBaseMs, observedBase);
            _uploadMsPerMiB = Blend(_uploadSamples, _uploadMsPerMiB, observedPerMiB);
            _uploadSamples++;
        }
    }

    public ChunkMeshCostSnapshot Snapshot()
    {
        lock (_gate)
            return new ChunkMeshCostSnapshot(
                _buildSamples,
                _uploadSamples,
                _buildMsPerPage,
                checked((long)Math.Ceiling(_resultBytesPerPage)),
                _uploadBaseMs,
                _uploadMsPerMiB);
    }

    public void Reset()
    {
        lock (_gate)
        {
            _buildSamples = 0;
            _buildMsPerPage = ColdBuildMsPerPage;
            _resultBytesPerPage = ColdResultBytesPerPage;
            _uploadSamples = 0;
            _uploadBaseMs = ColdUploadBaseMs;
            _uploadMsPerMiB = ColdUploadMsPerMiB;
        }
    }

    private double EstimateUploadMsLocked(long bytes) =>
        Math.Max(0.01, _uploadBaseMs + bytes / MiB * _uploadMsPerMiB);

    private static double Blend(long samples, double current, double observed) =>
        samples == 0 ? observed : current + Alpha * (observed - current);
}
