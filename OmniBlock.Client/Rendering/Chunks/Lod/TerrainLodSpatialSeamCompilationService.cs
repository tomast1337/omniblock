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
    long StaleResults);

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
    private readonly Thread _worker;
    private bool _disposed;
    private long _sequence;
    private long _coalesced;
    private long _rejected;
    private long _cancelled;
    private long _stale;

    public TerrainLodSpatialSeamCompilationService(int capacity = 64, int completedCapacity = 16)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (completedCapacity <= 0 || completedCapacity > capacity)
            throw new ArgumentOutOfRangeException(nameof(completedCapacity));
        _capacity = capacity;
        _completedCapacity = completedCapacity;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "TerrainLOD-SpatialSeam"
        };
        _worker.Start();
    }

    public bool Submit(
        TerrainLodSpatialSeamSegment segment,
        TerrainLodColumnTile owner,
        TerrainLodColumnTile? neighbor,
        IBlockRuntimeView blocks,
        double distanceChunks)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(blocks);
        if (!double.IsFinite(distanceChunks) || distanceChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceChunks));
        var identity = SourceIdentity(owner, neighbor);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_items.TryGetValue(segment, out var existing))
            {
                if (existing.Input.SourceIdentity == identity) return false;
                existing.Input = new Input(
                    segment, owner, neighbor, blocks, identity, distanceChunks, _sequence++);
                existing.Generation++;
                existing.Mesh = null;
                existing.Failure = null;
                if (existing.State != State.Running) existing.State = State.Queued;
                _coalesced++;
                Monitor.PulseAll(_gate);
                return false;
            }
            if (_items.Count >= _capacity)
            {
                _rejected++;
                return false;
            }
            _items.Add(segment, new WorkItem(new Input(
                segment, owner, neighbor, blocks, identity, distanceChunks, _sequence++)));
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
                _coalesced, _rejected, _cancelled, _stale);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _items.Clear();
            Monitor.PulseAll(_gate);
        }
        if (Thread.CurrentThread != _worker) _worker.Join();
    }

    private void WorkerLoop()
    {
        while (true)
        {
            WorkItem item;
            Input input;
            long generation;
            lock (_gate)
            {
                while (true)
                {
                    if (_disposed) return;
                    if (_items.Values.Count(static value => value.State == State.Ready) >=
                        _completedCapacity)
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
                    item.State = State.Running;
                    break;
                }
            }

            TerrainLodSpatialSeamMeshData? mesh = null;
            Exception? failure = null;
            var started = Stopwatch.GetTimestamp();
            try
            {
                mesh = TerrainLodSpatialSeamMeshBuilder.Build(
                    input.Segment, input.Owner, input.Neighbor, input.Blocks);
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
                    _stale++;
                    continue;
                }
                if (item.Generation != generation)
                {
                    item.State = State.Queued;
                    _stale++;
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
        TerrainLodColumnTile? neighbor) =>
        $"{owner.CanonicalHash}:{neighbor?.CanonicalHash ?? "air"}";

    private enum State { Queued, Running, Ready, Failed }

    private sealed class WorkItem(Input input)
    {
        public Input Input = input;
        public long Generation;
        public State State = State.Queued;
        public TerrainLodSpatialSeamMeshData? Mesh;
        public Exception? Failure;
        public double CompilationMs;
    }

    private readonly record struct Input(
        TerrainLodSpatialSeamSegment Segment,
        TerrainLodColumnTile Owner,
        TerrainLodColumnTile? Neighbor,
        IBlockRuntimeView Blocks,
        string SourceIdentity,
        double DistanceChunks,
        long Sequence);
}
