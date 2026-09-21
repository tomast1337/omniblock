using System.Diagnostics;
using OmniBlock.Blocks;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal enum TerrainLodSpatialMeshWorkKind
{
    Coverage,
    Refinement
}

internal enum TerrainLodSpatialMeshAdmissionResult
{
    Accepted,
    Coalesced,
    RejectedAtCapacity,
    RejectedOverBudget
}

internal sealed record TerrainLodSpatialMeshCompilationResult(
    TerrainLodSpatialMeshData? Mesh,
    TerrainLodSpatialMeshWorkKind WorkKind,
    double DistanceChunks,
    double CompilationMs,
    Exception? Failure);

internal readonly record struct TerrainLodSpatialMeshCompilationSnapshot(
    int Capacity,
    int CompletedCapacity,
    int Owned,
    int Queued,
    int Running,
    int Ready,
    int Failed,
    long Coalesced,
    long RejectedAtCapacity,
    long Cancelled,
    long OverBudget,
    long StaleResults,
    double OldestQueuedMs);

/// <summary>
///     Bounded, key-coalescing worker between immutable spatial column tiles and GPU staging.
///     Completed CPU meshes remain owned here until the render thread has upload budget.
/// </summary>
internal sealed class TerrainLodSpatialMeshCompilationService : IDisposable
{
    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly int _completedCapacity;
    private readonly Dictionary<TerrainLodTileKey, WorkItem> _items = [];
    private readonly Thread _worker;
    private bool _disposed;
    private long _sequence;
    private long _coalesced;
    private long _rejectedAtCapacity;
    private long _cancelled;
    private long _overBudget;
    private long _staleResults;

    public TerrainLodSpatialMeshCompilationService(
        int capacity = 32,
        int completedCapacity = 8)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (completedCapacity <= 0 || completedCapacity > capacity)
            throw new ArgumentOutOfRangeException(nameof(completedCapacity));
        _capacity = capacity;
        _completedCapacity = completedCapacity;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "TerrainLOD-SpatialMesh",
            Priority = ThreadPriority.BelowNormal
        };
        _worker.Start();
    }

    public TerrainLodSpatialMeshAdmissionResult Submit(
        TerrainLodColumnTile tile,
        IBlockRuntimeView blocks,
        int verticalSliceBudget,
        TerrainLodSpatialMeshWorkKind workKind,
        double distanceChunks,
        int? caveCullBelowY = null,
        long maximumResultBytes = TerrainLodScaleBudget.MaximumUploadBytesPerFrame)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(blocks);
        if (verticalSliceBudget <= 0)
            throw new ArgumentOutOfRangeException(nameof(verticalSliceBudget));
        if (!double.IsFinite(distanceChunks) || distanceChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceChunks));
        if (maximumResultBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumResultBytes));
        var input = new Input(
            tile, blocks, verticalSliceBudget, workKind, distanceChunks,
            caveCullBelowY, maximumResultBytes, 0, Stopwatch.GetTimestamp());
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (maximumResultBytes == 0)
            {
                _overBudget++;
                return TerrainLodSpatialMeshAdmissionResult.RejectedOverBudget;
            }
            if (_items.TryGetValue(tile.Key, out var existing))
            {
                if (existing.Input.Tile.CanonicalHash == tile.CanonicalHash &&
                    existing.Input.VerticalSliceBudget == verticalSliceBudget &&
                    existing.Input.CaveCullBelowY == caveCullBelowY)
                    return TerrainLodSpatialMeshAdmissionResult.Coalesced;
                existing.CancelCurrentGeneration();
                existing.Input = input with { Sequence = _sequence++ };
                existing.Generation++;
                existing.Result = null;
                existing.Failure = null;
                if (existing.State != State.Running) existing.State = State.Queued;
                _coalesced++;
                _cancelled++;
                Monitor.PulseAll(_gate);
                return TerrainLodSpatialMeshAdmissionResult.Coalesced;
            }
            if (_items.Count >= _capacity)
            {
                _rejectedAtCapacity++;
                return TerrainLodSpatialMeshAdmissionResult.RejectedAtCapacity;
            }
            _items.Add(tile.Key, new WorkItem(input with { Sequence = _sequence++ }));
            Monitor.PulseAll(_gate);
            return TerrainLodSpatialMeshAdmissionResult.Accepted;
        }
    }

    public void Retain(IReadOnlySet<TerrainLodTileKey> desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        lock (_gate)
        {
            foreach (var key in _items.Keys.Where(key => !desired.Contains(key)).ToArray())
            {
                var item = _items[key];
                item.CancelCurrentGeneration(createReplacement: false);
                _items.Remove(key);
                _cancelled++;
            }
            Monitor.PulseAll(_gate);
        }
    }

    public bool TryTakeCompleted(out TerrainLodSpatialMeshCompilationResult? result)
    {
        lock (_gate)
        {
            var ready = _items
                .Where(static pair => pair.Value.State is State.Ready or State.Failed)
                .OrderBy(static pair => pair.Value.Input.WorkKind)
                .ThenByDescending(static pair => pair.Key.Level)
                .ThenBy(static pair => pair.Value.Input.DistanceChunks)
                .ThenBy(static pair => pair.Value.Input.Sequence)
                .FirstOrDefault();
            if (ready.Value is null)
            {
                result = null;
                return false;
            }
            result = new TerrainLodSpatialMeshCompilationResult(
                ready.Value.Result,
                ready.Value.Input.WorkKind,
                ready.Value.Input.DistanceChunks,
                ready.Value.CompilationMs,
                ready.Value.Failure);
            _items.Remove(ready.Key);
            ready.Value.DisposeCancellation();
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public TerrainLodSpatialMeshCompilationSnapshot Snapshot()
    {
        lock (_gate)
            return new TerrainLodSpatialMeshCompilationSnapshot(
                _capacity,
                _completedCapacity,
                _items.Count,
                _items.Values.Count(static item => item.State == State.Queued),
                _items.Values.Count(static item => item.State == State.Running),
                _items.Values.Count(static item => item.State == State.Ready),
                _items.Values.Count(static item => item.State == State.Failed),
                _coalesced,
                _rejectedAtCapacity,
                _cancelled,
                _overBudget,
                _staleResults,
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
        if (Thread.CurrentThread != _worker) _worker.Join();
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
                    if (_items.Values.Count(static value => value.State == State.Ready) >=
                        _completedCapacity)
                    {
                        Monitor.Wait(_gate);
                        continue;
                    }
                    item = _items.Values
                        .Where(static value => value.State == State.Queued)
                        .OrderBy(static value => value.Input.WorkKind)
                        .ThenByDescending(static value => value.Input.Tile.Key.Level)
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

            TerrainLodSpatialMeshData? mesh = null;
            Exception? failure = null;
            var cancelled = false;
            var overBudget = false;
            var started = Stopwatch.GetTimestamp();
            try
            {
                mesh = TerrainLodSpatialMeshBuilder.Build(
                    input.Tile, input.Blocks, input.VerticalSliceBudget,
                    emitTileBoundaryFaces: false,
                    caveCullBelowY: input.CaveCullBelowY,
                    cancellationToken: cancellationToken,
                    maximumResultBytes: input.MaximumResultBytes);
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
                if (!_items.TryGetValue(input.Tile.Key, out var current) ||
                    !ReferenceEquals(current, item)) continue;
                if (item.Generation != generation)
                {
                    item.State = State.Queued;
                    if (!cancelled) _staleResults++;
                    Monitor.PulseAll(_gate);
                    continue;
                }
                if (cancelled || overBudget)
                {
                    _items.Remove(input.Tile.Key);
                    item.DisposeCancellation();
                    if (overBudget) _overBudget++;
                    Monitor.PulseAll(_gate);
                    continue;
                }
                item.Result = mesh;
                item.Failure = failure;
                item.CompilationMs = elapsed;
                item.State = failure is null ? State.Ready : State.Failed;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private enum State { Queued, Running, Ready, Failed }

    private sealed class WorkItem(Input input)
    {
        public Input Input = input;
        public long Generation;
        public State State = State.Queued;
        public TerrainLodSpatialMeshData? Result;
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
        TerrainLodColumnTile Tile,
        IBlockRuntimeView Blocks,
        int VerticalSliceBudget,
        TerrainLodSpatialMeshWorkKind WorkKind,
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
