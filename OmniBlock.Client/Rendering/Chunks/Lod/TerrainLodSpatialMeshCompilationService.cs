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
    RejectedAtCapacity
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
    long StaleResults);

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
            Name = "TerrainLOD-SpatialMesh"
        };
        _worker.Start();
    }

    public TerrainLodSpatialMeshAdmissionResult Submit(
        TerrainLodColumnTile tile,
        IBlockRuntimeView blocks,
        int verticalSliceBudget,
        TerrainLodSpatialMeshWorkKind workKind,
        double distanceChunks,
        int? caveCullBelowY = null)
    {
        ArgumentNullException.ThrowIfNull(tile);
        ArgumentNullException.ThrowIfNull(blocks);
        if (verticalSliceBudget <= 0)
            throw new ArgumentOutOfRangeException(nameof(verticalSliceBudget));
        if (!double.IsFinite(distanceChunks) || distanceChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceChunks));
        var input = new Input(
            tile, blocks, verticalSliceBudget, workKind, distanceChunks,
            caveCullBelowY, 0);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_items.TryGetValue(tile.Key, out var existing))
            {
                if (existing.Input.Tile.CanonicalHash == tile.CanonicalHash &&
                    existing.Input.VerticalSliceBudget == verticalSliceBudget &&
                    existing.Input.CaveCullBelowY == caveCullBelowY)
                    return TerrainLodSpatialMeshAdmissionResult.Coalesced;
                existing.Input = input with { Sequence = _sequence++ };
                existing.Generation++;
                existing.Result = null;
                existing.Failure = null;
                if (existing.State != State.Running) existing.State = State.Queued;
                _coalesced++;
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
                _staleResults);
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
                    item.State = State.Running;
                    break;
                }
            }

            TerrainLodSpatialMeshData? mesh = null;
            Exception? failure = null;
            var started = Stopwatch.GetTimestamp();
            try
            {
                mesh = TerrainLodSpatialMeshBuilder.Build(
                    input.Tile, input.Blocks, input.VerticalSliceBudget,
                    emitTileBoundaryFaces: false,
                    caveCullBelowY: input.CaveCullBelowY);
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
                    _staleResults++;
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
    }

    private readonly record struct Input(
        TerrainLodColumnTile Tile,
        IBlockRuntimeView Blocks,
        int VerticalSliceBudget,
        TerrainLodSpatialMeshWorkKind WorkKind,
        double DistanceChunks,
        int? CaveCullBelowY,
        long Sequence);
}
