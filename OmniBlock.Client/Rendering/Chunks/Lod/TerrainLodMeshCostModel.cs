using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal enum TerrainLodMeshWorkKind
{
    Coverage,
    Refinement
}

internal readonly record struct TerrainLodMeshCostEstimate(
    long WorkCells,
    double CompilationMs,
    long ResultBytes,
    double UploadMs);

internal readonly record struct TerrainLodMeshCostSnapshot(
    long CompilationSamples,
    long UploadSamples,
    double CompilationMsPerKCell,
    double ResultBytesPerKCell,
    double UploadBaseMs,
    double UploadMsPerMiB);

/// <summary>Online admission model for CPU LOD compilation and render-thread installation.</summary>
internal sealed class TerrainLodMeshCostModel
{
    internal const double ColdCompilationMsPerKCell = 0.20;
    internal const double ColdResultBytesPerKCell = 96 * 1024;
    internal const double ColdUploadBaseMs = 0.04;
    internal const double ColdUploadMsPerMiB = 0.20;
    private const double Alpha = 0.125;
    private const double CellsPerUnit = 1024.0;
    private const double MiB = 1024.0 * 1024.0;

    private readonly object _gate = new();
    private long _compilationSamples;
    private long _uploadSamples;
    private double _compilationMsPerKCell = ColdCompilationMsPerKCell;
    private double _resultBytesPerKCell = ColdResultBytesPerKCell;
    private double _uploadBaseMs = ColdUploadBaseMs;
    private double _uploadMsPerMiB = ColdUploadMsPerMiB;

    public TerrainLodMeshCostEstimate Estimate(
        TerrainLodHierarchy hierarchy,
        int minimumLevel,
        int maximumLevel)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        var maximum = Math.Min(maximumLevel, hierarchy.Levels.Count - 1);
        var minimum = Math.Clamp(minimumLevel, 0, maximum);
        long cells = 0;
        for (var level = minimum; level <= maximum; level++)
            cells += hierarchy.Levels[level].CellCount;
        cells = Math.Max(1, cells);
        var units = cells / CellsPerUnit;
        lock (_gate)
        {
            var bytes = checked((long)Math.Ceiling(Math.Max(1, units * _resultBytesPerKCell)));
            return new TerrainLodMeshCostEstimate(
                cells,
                Math.Max(0.01, units * _compilationMsPerKCell),
                bytes,
                EstimateUploadMsLocked(bytes));
        }
    }

    public double EstimateUploadMs(long bytes)
    {
        lock (_gate) return EstimateUploadMsLocked(bytes);
    }

    public void RecordCompilation(double elapsedMs, long workCells, long resultBytes)
    {
        if (!double.IsFinite(elapsedMs) || elapsedMs < 0 || workCells <= 0 || resultBytes < 0)
            return;
        var units = workCells / CellsPerUnit;
        lock (_gate)
        {
            _compilationMsPerKCell = Blend(
                _compilationSamples, _compilationMsPerKCell, elapsedMs / units);
            _resultBytesPerKCell = Blend(
                _compilationSamples, _resultBytesPerKCell, resultBytes / units);
            _compilationSamples++;
        }
    }

    public void RecordUpload(double elapsedMs, long bytes)
    {
        if (!double.IsFinite(elapsedMs) || elapsedMs < 0 || bytes < 0) return;
        lock (_gate)
        {
            var observedBase = Math.Min(elapsedMs, 0.25);
            var observedPerMiB = Math.Max(0, elapsedMs - _uploadBaseMs) /
                                 Math.Max(1.0 / 16.0, bytes / MiB);
            _uploadBaseMs = Blend(_uploadSamples, _uploadBaseMs, observedBase);
            _uploadMsPerMiB = Blend(_uploadSamples, _uploadMsPerMiB, observedPerMiB);
            _uploadSamples++;
        }
    }

    public TerrainLodMeshCostSnapshot Snapshot()
    {
        lock (_gate)
            return new TerrainLodMeshCostSnapshot(
                _compilationSamples,
                _uploadSamples,
                _compilationMsPerKCell,
                _resultBytesPerKCell,
                _uploadBaseMs,
                _uploadMsPerMiB);
    }

    private double EstimateUploadMsLocked(long bytes) =>
        Math.Max(0.01, _uploadBaseMs + bytes / MiB * _uploadMsPerMiB);

    private static double Blend(long samples, double current, double observed) =>
        samples == 0 ? observed : current + Alpha * (observed - current);
}

/// <summary>Coverage leads, while a bounded burst guarantees refinement progress.</summary>
internal sealed class TerrainLodWorkFairness
{
    internal const int CoverageBurstLimit = 8;
    private int _coverageSinceRefinement;

    public TerrainLodMeshWorkKind Peek(bool hasCoverage, bool hasRefinement)
    {
        if (hasRefinement && (!hasCoverage || _coverageSinceRefinement >= CoverageBurstLimit))
            return TerrainLodMeshWorkKind.Refinement;
        return TerrainLodMeshWorkKind.Coverage;
    }

    public void Commit(TerrainLodMeshWorkKind kind)
    {
        if (kind == TerrainLodMeshWorkKind.Refinement)
            _coverageSinceRefinement = 0;
        else
            _coverageSinceRefinement++;
    }
}
