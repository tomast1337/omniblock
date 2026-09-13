using System.Diagnostics;

namespace OmniBlock.Server.Worlds;

/// <summary>Stages measured independently before background world generation is introduced.</summary>
public enum WorldGenerationStage
{
    StorageDecode,
    Terrain,
    Decoration,
    LightInitialization,
    LightPropagation,
    Activation,
    EncodeSave,
    Unload
}

/// <summary>A bounded timing/allocation distribution for one generation stage.</summary>
public readonly record struct WorldGenerationStageSnapshot(
    long Count,
    long Failures,
    double AverageMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    long AverageAllocatedBytes,
    long P95AllocatedBytes,
    long MaxAllocatedBytes);

/// <summary>One immutable observation of world-generation pressure.</summary>
public sealed record WorldGenerationSnapshot(
    IReadOnlyDictionary<WorldGenerationStage, WorldGenerationStageSnapshot> Stages,
    int Queued,
    int InFlight,
    int Ready,
    int QueuePeak,
    int RetainedChunks,
    long RetainedPayloadBytes,
    long Failures);

/// <summary>
///     Per-world measurements for terrain production and publication. It retains only a bounded
///     sample window, so leaving it enabled cannot grow memory with explored world size.
/// </summary>
public sealed class WorldGenerationTelemetry
{
    internal const int SampleCapacity = 512;

    private readonly StageDistribution[] _stages =
        Enum.GetValues<WorldGenerationStage>().Select(_ => new StageDistribution()).ToArray();
    private int _inFlight;
    private int _queuePeak;
    private int _queued;
    private int _ready;
    private int _retainedChunks;
    private long _retainedPayloadBytes;

    public T Measure<T>(WorldGenerationStage stage, Func<T> action)
    {
        var started = Stopwatch.GetTimestamp();
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var failed = true;
        try
        {
            var result = action();
            failed = false;
            return result;
        }
        finally
        {
            Record(stage, Stopwatch.GetTimestamp() - started,
                GC.GetAllocatedBytesForCurrentThread() - allocatedBefore, failed);
        }
    }

    public void Measure(WorldGenerationStage stage, Action action) =>
        Measure(stage, () =>
        {
            action();
            return true;
        });

    internal void SetQueueDepths(int queued, int inFlight, int ready)
    {
        Volatile.Write(ref _queued, Math.Max(0, queued));
        Volatile.Write(ref _inFlight, Math.Max(0, inFlight));
        Volatile.Write(ref _ready, Math.Max(0, ready));
        var total = Math.Max(0, queued) + Math.Max(0, inFlight) + Math.Max(0, ready);
        var peak = Volatile.Read(ref _queuePeak);
        while (total > peak)
        {
            var observed = Interlocked.CompareExchange(ref _queuePeak, total, peak);
            if (observed == peak) break;
            peak = observed;
        }
    }

    internal void SetResidency(int chunks, long payloadBytes)
    {
        Volatile.Write(ref _retainedChunks, Math.Max(0, chunks));
        Volatile.Write(ref _retainedPayloadBytes, Math.Max(0, payloadBytes));
    }

    public WorldGenerationSnapshot Snapshot()
    {
        var snapshots = new Dictionary<WorldGenerationStage, WorldGenerationStageSnapshot>();
        long failures = 0;
        foreach (var stage in Enum.GetValues<WorldGenerationStage>())
        {
            var snapshot = _stages[(int)stage].Snapshot();
            snapshots[stage] = snapshot;
            failures += snapshot.Failures;
        }

        return new WorldGenerationSnapshot(
            snapshots,
            Volatile.Read(ref _queued),
            Volatile.Read(ref _inFlight),
            Volatile.Read(ref _ready),
            Volatile.Read(ref _queuePeak),
            Volatile.Read(ref _retainedChunks),
            Volatile.Read(ref _retainedPayloadBytes),
            failures);
    }

    private void Record(WorldGenerationStage stage, long elapsedTicks, long allocatedBytes, bool failed) =>
        _stages[(int)stage].Record(elapsedTicks, allocatedBytes, failed);

    private sealed class StageDistribution
    {
        private readonly object _gate = new();
        private readonly long[] _allocatedBytes = new long[SampleCapacity];
        private readonly long[] _elapsedTicks = new long[SampleCapacity];
        private long _count;
        private long _failures;
        private long _maxAllocatedBytes;
        private long _maxElapsedTicks;
        private long _totalAllocatedBytes;
        private long _totalElapsedTicks;

        public void Record(long elapsedTicks, long allocatedBytes, bool failed)
        {
            lock (_gate)
            {
                var index = (int)(_count % SampleCapacity);
                _elapsedTicks[index] = Math.Max(0, elapsedTicks);
                _allocatedBytes[index] = Math.Max(0, allocatedBytes);
                _count++;
                if (failed) _failures++;
                _totalElapsedTicks += Math.Max(0, elapsedTicks);
                _totalAllocatedBytes += Math.Max(0, allocatedBytes);
                _maxElapsedTicks = Math.Max(_maxElapsedTicks, elapsedTicks);
                _maxAllocatedBytes = Math.Max(_maxAllocatedBytes, allocatedBytes);
            }
        }

        public WorldGenerationStageSnapshot Snapshot()
        {
            lock (_gate)
            {
                var sampleCount = (int)Math.Min(_count, SampleCapacity);
                var ticks = _elapsedTicks[..sampleCount].ToArray();
                var bytes = _allocatedBytes[..sampleCount].ToArray();
                Array.Sort(ticks);
                Array.Sort(bytes);
                return new WorldGenerationStageSnapshot(
                    _count,
                    _failures,
                    _count == 0 ? 0 : TicksToMilliseconds(_totalElapsedTicks) / _count,
                    TicksToMilliseconds(Percentile(ticks, 0.50)),
                    TicksToMilliseconds(Percentile(ticks, 0.95)),
                    TicksToMilliseconds(Percentile(ticks, 0.99)),
                    TicksToMilliseconds(_maxElapsedTicks),
                    _count == 0 ? 0 : _totalAllocatedBytes / _count,
                    Percentile(bytes, 0.95),
                    _maxAllocatedBytes);
            }
        }

        private static long Percentile(long[] sorted, double percentile)
        {
            if (sorted.Length == 0) return 0;
            var index = (int)Math.Ceiling(sorted.Length * percentile) - 1;
            return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
        }

        private static double TicksToMilliseconds(long ticks) =>
            ticks * 1000.0 / Stopwatch.Frequency;
    }
}
