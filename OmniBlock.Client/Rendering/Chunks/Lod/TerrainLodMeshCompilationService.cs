using System.Diagnostics;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal sealed record TerrainLodMeshCompilationRequest(
    TerrainLodConversionResult Conversion,
    int MinimumLevel,
    int MaximumLevel,
    WorldRegionSnapshot Visuals,
    bool HasSkyLight,
    TerrainLodMeshWorkKind WorkKind = TerrainLodMeshWorkKind.Coverage,
    int? CaveCullBelowY = null);

internal sealed record TerrainLodMeshCompilationResult(
    TerrainLodConversionResult Conversion,
    int MinimumLevel,
    TerrainLodBoundarySummary? Boundaries,
    TerrainLodMeshData[] Levels,
    TerrainLodMeshWorkKind WorkKind = TerrainLodMeshWorkKind.Coverage,
    long WorkCells = 0,
    double CompilationMs = 0,
    long RetainedBytes = 0,
    long UploadBytes = 0,
    Exception? Failure = null);

internal readonly record struct TerrainLodMeshCompilationSnapshot(
    int Owned,
    int CoverageQueued,
    int RefinementQueued,
    int CoverageCompleted,
    int RefinementCompleted,
    long CompletedResultBytes,
    long PredictedResultBytes,
    double PredictedCompilationMs,
    long AdmissionDeferrals,
    long UploadAdmissionDeferrals,
    long OversizedUploadAdmissions,
    TerrainLodMeshCostSnapshot Cost);

/// <summary>
///     Bounded CPU-only stage between hierarchy reduction/cache loading and render-thread GPU
///     installation. The request owns its immutable world snapshot until the worker finishes.
/// </summary>
internal sealed class TerrainLodMeshCompilationService : IDisposable
{
    internal const long CompletedResultBudgetBytes = 64L * 1024 * 1024;
    internal const long CoverageCompletedResultBudgetBytes = 96L * 1024 * 1024;
    internal const double CompilationAdmissionMs = 96;
    internal const double CoverageCompilationAdmissionMs = 128;
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly int _refinementCapacity;
    private readonly Queue<CompilationWork> _coverageQueued = [];
    private readonly Queue<CompilationWork> _refinementQueued = [];
    private readonly Queue<TerrainLodMeshCompilationResult> _coverageCompleted = [];
    private readonly Queue<TerrainLodMeshCompilationResult> _refinementCompleted = [];
    private readonly TerrainLodWorkFairness _workFairness = new();
    private readonly TerrainLodWorkFairness _resultFairness = new();
    private readonly TerrainLodMeshCostModel _costModel = new();
    private readonly Thread _worker;
    private int _owned;
    private long _completedResultBytes;
    private long _predictedResultBytes;
    private double _predictedCompilationMs;
    private long _admissionDeferrals;
    private long _uploadAdmissionDeferrals;
    private long _oversizedUploadAdmissions;
    private bool _disposed;

    public TerrainLodMeshCompilationService(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        var coverageReserve = capacity > 1 ? Math.Max(1, capacity / 4) : 0;
        _refinementCapacity = capacity - coverageReserve;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "TerrainLOD-Mesh"
        };
        _worker.Start();
    }

    public bool HasCapacity
    {
        get
        {
            lock (_gate) return !_disposed && _owned < _capacity;
        }
    }

    public bool TrySubmit(TerrainLodMeshCompilationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var estimate = _costModel.Estimate(
            request.Conversion.Hierarchy, request.MinimumLevel, request.MaximumLevel);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!CanAdmitLocked(request.WorkKind, estimate))
            {
                _admissionDeferrals++;
                return false;
            }
            _owned++;
            _predictedResultBytes += estimate.ResultBytes;
            _predictedCompilationMs += estimate.CompilationMs;
            QueueFor(request.WorkKind).Enqueue(new CompilationWork(request, estimate));
            Monitor.Pulse(_gate);
            return true;
        }
    }

    public bool CanSubmit(
        TerrainLodConversionResult conversion,
        int minimumLevel,
        int maximumLevel,
        TerrainLodMeshWorkKind workKind)
    {
        ArgumentNullException.ThrowIfNull(conversion);
        var estimate = _costModel.Estimate(conversion.Hierarchy, minimumLevel, maximumLevel);
        lock (_gate)
        {
            var admitted = !_disposed && CanAdmitLocked(workKind, estimate);
            if (!admitted) _admissionDeferrals++;
            return admitted;
        }
    }

    public bool TryPeekCompleted(
        out TerrainLodMeshCompilationResult? result,
        out TerrainLodMeshWorkKind workKind)
    {
        lock (_gate)
        {
            if (_coverageCompleted.Count == 0 && _refinementCompleted.Count == 0)
            {
                result = null;
                workKind = default;
                return false;
            }
            workKind = _resultFairness.Peek(
                _coverageCompleted.Count > 0, _refinementCompleted.Count > 0);
            return CompletedFor(workKind).TryPeek(out result);
        }
    }

    public bool TryTakeCompleted(out TerrainLodMeshCompilationResult? result)
    {
        if (!TryPeekCompleted(out _, out var workKind))
        {
            result = null;
            return false;
        }
        return TryTakeCompleted(workKind, advanceFairness: true, out result);
    }

    public bool TryTakeCompleted(
        TerrainLodMeshWorkKind workKind,
        bool advanceFairness,
        out TerrainLodMeshCompilationResult? result)
    {
        lock (_gate)
        {
            if (!CompletedFor(workKind).TryDequeue(out result) || result is null)
            {
                return false;
            }

            if (advanceFairness) _resultFairness.Commit(workKind);
            _owned--;
            _completedResultBytes -= result.RetainedBytes;
            return true;
        }
    }

    public double EstimateUploadMs(long bytes) => _costModel.EstimateUploadMs(bytes);

    public void RecordUpload(double elapsedMs, long bytes) =>
        _costModel.RecordUpload(elapsedMs, bytes);

    public void NoteUploadAdmissionDeferred()
    {
        lock (_gate) _uploadAdmissionDeferrals++;
    }

    public void NoteOversizedUploadAdmission()
    {
        lock (_gate) _oversizedUploadAdmissions++;
    }

    public TerrainLodMeshCompilationSnapshot Snapshot()
    {
        lock (_gate)
            return new TerrainLodMeshCompilationSnapshot(
                _owned,
                _coverageQueued.Count,
                _refinementQueued.Count,
                _coverageCompleted.Count,
                _refinementCompleted.Count,
                Math.Max(0, _completedResultBytes),
                Math.Max(0, _predictedResultBytes),
                Math.Max(0, _predictedCompilationMs),
                _admissionDeferrals,
                _uploadAdmissionDeferrals,
                _oversizedUploadAdmissions,
                _costModel.Snapshot());
    }

    private void WorkerLoop()
    {
        while (true)
        {
            CompilationWork work;
            lock (_gate)
            {
                while (!_disposed && _coverageQueued.Count == 0 && _refinementQueued.Count == 0)
                    Monitor.Wait(_gate);
                if (_disposed) return;
                var kind = _workFairness.Peek(
                    _coverageQueued.Count > 0, _refinementQueued.Count > 0);
                work = QueueFor(kind).Dequeue();
                _workFairness.Commit(kind);
            }

            var request = work.Request;
            TerrainLodMeshCompilationResult result;
            var started = Stopwatch.GetTimestamp();
            try
            {
                var hierarchy = request.Conversion.Hierarchy;
                var maximum = Math.Min(request.MaximumLevel, hierarchy.Levels.Count - 1);
                var minimum = Math.Clamp(request.MinimumLevel, 0, maximum);
                var boundaries = TerrainLodBoundarySummary.Capture(hierarchy, minimum, maximum);
                var levels = new TerrainLodMeshData[maximum - minimum + 1];
                for (var level = minimum; level <= maximum; level++)
                {
                    levels[level - minimum] = TerrainLodMeshBuilder.Build(
                        hierarchy,
                        level,
                        request.Visuals.ContentBlocks,
                        request.HasSkyLight,
                        request.Conversion.Lighting,
                        request.Visuals,
                        request.CaveCullBelowY);
                }
                var compilationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var retainedBytes = levels.Sum(static level => level.EstimatedBytes) +
                                    boundaries.EstimatedBytes;
                var uploadBytes = levels.Sum(static level => level.EstimatedBytes);
                result = new TerrainLodMeshCompilationResult(
                    request.Conversion, minimum, boundaries, levels,
                    request.WorkKind, work.Estimate.WorkCells, compilationMs,
                    retainedBytes, uploadBytes);
                _costModel.RecordCompilation(
                    compilationMs, work.Estimate.WorkCells, retainedBytes);
            }
            catch (Exception error)
            {
                result = new TerrainLodMeshCompilationResult(
                    request.Conversion, request.MinimumLevel, null, [],
                    request.WorkKind, work.Estimate.WorkCells,
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    Failure: error);
            }
            finally
            {
                request.Visuals.Dispose();
            }

            lock (_gate)
            {
                if (_disposed) return;
                _predictedResultBytes -= work.Estimate.ResultBytes;
                _predictedCompilationMs -= work.Estimate.CompilationMs;
                _completedResultBytes += result.RetainedBytes;
                CompletedFor(result.WorkKind).Enqueue(result);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            while (_coverageQueued.TryDequeue(out var work)) work.Request.Visuals.Dispose();
            while (_refinementQueued.TryDequeue(out var work)) work.Request.Visuals.Dispose();
            _coverageCompleted.Clear();
            _refinementCompleted.Clear();
            Monitor.PulseAll(_gate);
        }
        if (_worker != Thread.CurrentThread) _worker.Join(TimeSpan.FromSeconds(5));
    }

    private bool CanAdmitLocked(
        TerrainLodMeshWorkKind workKind,
        in TerrainLodMeshCostEstimate estimate)
    {
        if (_owned >= _capacity) return false;
        if (workKind == TerrainLodMeshWorkKind.Refinement && _owned >= _refinementCapacity)
            return false;
        var compilationLimit = workKind == TerrainLodMeshWorkKind.Coverage
            ? CoverageCompilationAdmissionMs
            : CompilationAdmissionMs;
        var byteLimit = workKind == TerrainLodMeshWorkKind.Coverage
            ? CoverageCompletedResultBudgetBytes
            : CompletedResultBudgetBytes;
        var timeFits = _predictedCompilationMs + estimate.CompilationMs <= compilationLimit;
        var bytesFit = _completedResultBytes + _predictedResultBytes + estimate.ResultBytes <= byteLimit;
        // A cold or unusually large request must be able to establish measurements and progress.
        return timeFits && bytesFit || _owned == 0;
    }

    private Queue<CompilationWork> QueueFor(TerrainLodMeshWorkKind workKind) =>
        workKind == TerrainLodMeshWorkKind.Coverage ? _coverageQueued : _refinementQueued;

    private Queue<TerrainLodMeshCompilationResult> CompletedFor(TerrainLodMeshWorkKind workKind) =>
        workKind == TerrainLodMeshWorkKind.Coverage ? _coverageCompleted : _refinementCompleted;

    private sealed record CompilationWork(
        TerrainLodMeshCompilationRequest Request,
        TerrainLodMeshCostEstimate Estimate);
}
