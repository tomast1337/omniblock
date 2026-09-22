using System.Diagnostics;
using OmniBlock.Blocks;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal sealed record TerrainLodSpatialSeamCompilationResult(
    TerrainLodSpatialSeamMeshData? Mesh,
    double CompilationMs,
    Exception? Failure);

internal readonly record struct TerrainLodSpatialSeamCompilationSnapshot(
    int Owned,
    int Queued,
    int Running,
    int Ready,
    long Coalesced,
    long RejectedAtCapacity,
    long Cancelled,
    long OverBudget,
    long StaleResults,
    double OldestQueuedMs);

/// <summary>
///     Bounded off-thread seam compiler. Work is coalesced by geometric seam identity while source
///     hashes detect tile refreshes; selection changes cancel queued and completed obsolete seams.
/// </summary>
internal sealed class TerrainLodSpatialSeamCompilationService : IDisposable
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly int _completedCapacity;
    private readonly Dictionary<TerrainLodSpatialSeamSegment, WorkItem> _items = [];
    private readonly Thread[] _workers;
    private bool _disposed;
    private long _sequence;
    private long _coalesced;
    private long _rejected;
    private long _cancelled;
    private long _overBudget;
    private long _stale;

    public TerrainLodSpatialSeamCompilationService(
        int capacity = 64,
        int completedCapacity = 16,
        int workerCount = 2)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (completedCapacity <= 0 || completedCapacity > capacity)
            throw new ArgumentOutOfRangeException(nameof(completedCapacity));
        if (workerCount <= 0 || workerCount > capacity)
            throw new ArgumentOutOfRangeException(nameof(workerCount));
        _capacity = capacity;
        _completedCapacity = completedCapacity;
        _workers = new Thread[workerCount];
        for (var index = 0; index < _workers.Length; index++)
        {
            _workers[index] = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"TerrainLOD-SpatialSeam-{index}",
                Priority = ThreadPriority.BelowNormal
            };
            _workers[index].Start();
        }
    }

    public bool Submit(
        TerrainLodSpatialSeamSegment segment,
        TerrainLodColumnTile owner,
        TerrainLodColumnTile? neighbor,
        IBlockRuntimeView blocks,
        double distanceChunks,
        int? caveCullBelowY = null,
        long maximumResultBytes = TerrainLodScaleBudget.MaximumUploadBytesPerFrame)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(blocks);
        if (!double.IsFinite(distanceChunks) || distanceChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceChunks));
        if (maximumResultBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumResultBytes));
        var identity = SourceIdentity(owner, neighbor, caveCullBelowY);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (maximumResultBytes == 0)
            {
                _overBudget++;
                return false;
            }
            if (_items.TryGetValue(segment, out var existing))
            {
                if (existing.Input.SourceIdentity == identity) return false;
                existing.CancelCurrentGeneration();
                existing.Input = new Input(
                    segment, owner, neighbor, blocks, identity, distanceChunks,
                    caveCullBelowY, maximumResultBytes, _sequence++, Stopwatch.GetTimestamp());
                existing.Generation++;
                existing.Mesh = null;
                existing.Failure = null;
                if (existing.State != State.Running) existing.State = State.Queued;
                _coalesced++;
                _cancelled++;
                Monitor.PulseAll(_gate);
                return false;
            }
            if (_items.Count >= _capacity)
            {
                _rejected++;
                return false;
            }
            _items.Add(segment, new WorkItem(new Input(
                segment, owner, neighbor, blocks, identity, distanceChunks,
                caveCullBelowY, maximumResultBytes, _sequence++, Stopwatch.GetTimestamp())));
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public void Retain(IReadOnlySet<TerrainLodSpatialSeamSegment> desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        lock (_gate)
        {
            foreach (var key in _items.Keys.Where(key => !desired.Contains(key)).ToArray())
            {
                _items[key].CancelCurrentGeneration(createReplacement: false);
                _items.Remove(key);
                _cancelled++;
            }
            Monitor.PulseAll(_gate);
        }
    }

    public bool TryTakeCompleted(out TerrainLodSpatialSeamCompilationResult? result)
    {
        lock (_gate)
        {
            var ready = _items
                .Where(static pair => pair.Value.State is State.Ready or State.Failed)
                .OrderBy(static pair => pair.Key.IsExterior)
                .ThenBy(static pair => pair.Value.Input.DistanceChunks)
                .ThenBy(static pair => pair.Value.Input.Sequence)
                .FirstOrDefault();
            if (ready.Value is null)
            {
                result = null;
                return false;
            }
            result = new TerrainLodSpatialSeamCompilationResult(
                ready.Value.Mesh, ready.Value.CompilationMs, ready.Value.Failure);
            _items.Remove(ready.Key);
            ready.Value.DisposeCancellation();
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public TerrainLodSpatialSeamCompilationSnapshot Snapshot()
    {
        lock (_gate)
            return new TerrainLodSpatialSeamCompilationSnapshot(
                _items.Count,
                _items.Values.Count(static item => item.State == State.Queued),
                _items.Values.Count(static item => item.State == State.Running),
                _items.Values.Count(static item => item.State == State.Ready),
                _coalesced, _rejected, _cancelled, _overBudget, _stale,
                OldestQueuedMsLocked());
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var item in _items.Values)
                item.CancelCurrentGeneration(createReplacement: false);
            _items.Clear();
            Monitor.PulseAll(_gate);
        }
        foreach (var worker in _workers)
            if (Thread.CurrentThread != worker) worker.Join();
    }

    private void WorkerLoop()
    {
        while (true)
        {
            WorkItem item;
            Input input;
            long generation;
            CancellationToken cancellationToken;
            lock (_gate)
            {
                while (true)
                {
                    if (_disposed) return;
                    if (_items.Values.Count(static value =>
                            value.State is State.Ready or State.Running) >= _completedCapacity)
                    {
                        Monitor.Wait(_gate);
                        continue;
                    }
                    item = _items.Values
                        .Where(static value => value.State == State.Queued)
                        .OrderBy(static value => value.Input.Segment.IsExterior)
                        .ThenBy(static value => value.Input.DistanceChunks)
                        .ThenBy(static value => value.Input.Sequence)
                        .FirstOrDefault()!;
                    if (item is null)
                    {
                        Monitor.Wait(_gate);
                        continue;
                    }
                    input = item.Input;
                    generation = item.Generation;
                    cancellationToken = item.CancellationToken;
                    item.State = State.Running;
                    break;
                }
            }

            TerrainLodSpatialSeamMeshData? mesh = null;
            Exception? failure = null;
            var cancelled = false;
            var overBudget = false;
            var started = Stopwatch.GetTimestamp();
            try
            {
                mesh = TerrainLodSpatialSeamMeshBuilder.Build(
                    input.Segment, input.Owner, input.Neighbor, input.Blocks,
                    input.CaveCullBelowY,
                    cancellationToken,
                    input.MaximumResultBytes);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
            }
            catch (TerrainLodSpatialMeshBudgetExceededException)
            {
                overBudget = true;
            }
            catch (Exception error)
            {
                failure = error;
            }
            var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            lock (_gate)
            {
                if (_disposed) return;
                if (!_items.TryGetValue(input.Segment, out var current) ||
                    !ReferenceEquals(current, item))
                {
                    if (!cancelled) _stale++;
                    continue;
                }
                if (item.Generation != generation)
                {
                    item.State = State.Queued;
                    if (!cancelled) _stale++;
                    Monitor.PulseAll(_gate);
                    continue;
                }
                if (cancelled || overBudget)
                {
                    _items.Remove(input.Segment);
                    item.DisposeCancellation();
                    if (overBudget) _overBudget++;
                    Monitor.PulseAll(_gate);
                    continue;
                }
                item.Mesh = mesh;
                item.Failure = failure;
                item.CompilationMs = elapsed;
                item.State = failure is null ? State.Ready : State.Failed;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private static string SourceIdentity(
        TerrainLodColumnTile owner,
        TerrainLodColumnTile? neighbor,
        int? caveCullBelowY) =>
        $"{owner.CanonicalHash}:{neighbor?.CanonicalHash ?? "air"}:" +
        $"{caveCullBelowY?.ToString() ?? "none"}";

    private enum State { Queued, Running, Ready, Failed }

    private sealed class WorkItem(Input input)
    {
        public Input Input = input;
        public long Generation;
        public State State = State.Queued;
        public TerrainLodSpatialSeamMeshData? Mesh;
        public Exception? Failure;
        public double CompilationMs;
        private CancellationTokenSource _cancellation = new();

        public CancellationToken CancellationToken => _cancellation.Token;

        public void CancelCurrentGeneration(bool createReplacement = true)
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            if (createReplacement) _cancellation = new CancellationTokenSource();
        }

        public void DisposeCancellation() => _cancellation.Dispose();
    }

    private readonly record struct Input(
        TerrainLodSpatialSeamSegment Segment,
        TerrainLodColumnTile Owner,
        TerrainLodColumnTile? Neighbor,
        IBlockRuntimeView Blocks,
        string SourceIdentity,
        double DistanceChunks,
        int? CaveCullBelowY,
        long MaximumResultBytes,
        long Sequence,
        long QueuedTimestamp);

    private double OldestQueuedMsLocked()
    {
        var oldest = _items.Values
            .Where(static item => item.State == State.Queued)
            .Select(static item => item.Input.QueuedTimestamp)
            .DefaultIfEmpty(0)
            .Min();
        return oldest == 0 ? 0 : Stopwatch.GetElapsedTime(oldest).TotalMilliseconds;
    }
}
