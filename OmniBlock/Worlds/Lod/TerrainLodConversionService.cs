namespace OmniBlock.Worlds.Lod;

public enum TerrainLodAdmissionResult
{
    Accepted,
    Coalesced,
    RejectedStaleRevision,
    RejectedAtCapacity
}

public sealed record TerrainLodConversionResult(
    int Dimension,
    int ChunkX,
    int ChunkZ,
    long TerrainRevision,
    TerrainLodHierarchy Hierarchy,
    TerrainLodLightingSnapshot? Lighting = null,
    bool RequiresPersistence = true);

internal readonly record struct TerrainLodConversionOutput(
    TerrainLodHierarchy Hierarchy,
    TerrainLodLightingSnapshot? Lighting = null,
    bool RequiresPersistence = true);

public sealed record TerrainLodConversionSnapshot(
    int Dimension,
    int Capacity,
    int OwnedChunks,
    int Queued,
    int Running,
    int Ready,
    int Failed,
    long AcceptedSubmissions,
    long CoalescedSubmissions,
    long RejectedStaleSubmissions,
    long RejectedCapacitySubmissions,
    long CompletedConversions,
    long StaleBuildsDiscarded,
    long FailedConversions,
    long RetriedConversions,
    long CurrentRetainedSourceBytes,
    long PeakRetainedSourceBytes,
    long CurrentReadyCells,
    long PeakReadyCells);

/// <summary>
///     Bounded per-dimension owner for CPU LOD conversion. A chunk coordinate owns at most one
///     latest source snapshot; edits replace queued work and cause running stale output to be
///     discarded before publication. Ready output and retryable failures remain bounded by the
///     same coordinate capacity.
/// </summary>
public sealed class TerrainLodConversionService : IDisposable
{
    private readonly object _gate = new();
    private readonly int _dimension;
    private readonly int _capacity;
    private readonly Func<TerrainLodSourceSnapshot, TerrainLodConversionOutput> _convert;
    private readonly Dictionary<ChunkKey, WorkItem> _items = [];
    private readonly Thread _worker;
    private bool _disposed;
    private long _nextQueueSequence;
    private long _acceptedSubmissions;
    private long _coalescedSubmissions;
    private long _rejectedStaleSubmissions;
    private long _rejectedCapacitySubmissions;
    private long _completedConversions;
    private long _staleBuildsDiscarded;
    private long _failedConversions;
    private long _retriedConversions;
    private long _peakRetainedSourceBytes;
    private long _peakReadyCells;
    private TerrainLodConversionSnapshot _publishedSnapshot = null!;

    public TerrainLodConversionService(
        int dimension,
        TerrainLodMaterialCatalog materials,
        TerrainLodReductionStrategy strategy = TerrainLodReductionStrategy.SurfacePreserving,
        int capacity = 64)
        : this(dimension, capacity, CreateConverter(materials, strategy)) { }

    internal TerrainLodConversionService(
        int dimension,
        int capacity,
        Func<TerrainLodSourceSnapshot, TerrainLodHierarchy> convert)
        : this(dimension, capacity, source => new TerrainLodConversionOutput(
            convert(source), source.Lighting))
    {
        ArgumentNullException.ThrowIfNull(convert);
    }

    internal TerrainLodConversionService(
        int dimension,
        int capacity,
        Func<TerrainLodSourceSnapshot, TerrainLodConversionOutput> convert)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _dimension = dimension;
        _capacity = capacity;
        _convert = convert ?? throw new ArgumentNullException(nameof(convert));
        PublishSnapshotLocked();
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = $"TerrainLOD-{dimension}"
        };
        _worker.Start();
    }

    public TerrainLodAdmissionResult Submit(TerrainLodSourceSnapshot source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = new ChunkKey(source.ChunkX, source.ChunkZ);
            if (_items.TryGetValue(key, out var existing))
            {
                if (source.TerrainRevision < existing.Source.TerrainRevision)
                {
                    _rejectedStaleSubmissions++;
                    PublishSnapshotLocked();
                    return TerrainLodAdmissionResult.RejectedStaleRevision;
                }

                existing.Source = source;
                existing.Generation++;
                existing.Error = null;
                existing.Result = null;
                _coalescedSubmissions++;
                if (existing.State is WorkState.Ready or WorkState.Failed)
                {
                    QueueLocked(existing);
                    Monitor.PulseAll(_gate);
                }
                UpdateMemoryPeaksLocked();
                PublishSnapshotLocked();
                return TerrainLodAdmissionResult.Coalesced;
            }

            if (_items.Count >= _capacity)
            {
                _rejectedCapacitySubmissions++;
                PublishSnapshotLocked();
                return TerrainLodAdmissionResult.RejectedAtCapacity;
            }

            var item = new WorkItem(source);
            QueueLocked(item);
            _items.Add(key, item);
            _acceptedSubmissions++;
            UpdateMemoryPeaksLocked();
            PublishSnapshotLocked();
            Monitor.PulseAll(_gate);
            return TerrainLodAdmissionResult.Accepted;
        }
    }

    public bool Retry(int chunkX, int chunkZ)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = new ChunkKey(chunkX, chunkZ);
            if (!_items.TryGetValue(key, out var item) || item.State != WorkState.Failed)
                return false;
            QueueLocked(item);
            item.Error = null;
            _retriedConversions++;
            PublishSnapshotLocked();
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public bool Discard(int chunkX, int chunkZ)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = new ChunkKey(chunkX, chunkZ);
            if (!_items.Remove(key)) return false;
            PublishSnapshotLocked();
            return true;
        }
    }

    public bool TryTakeCompleted(out TerrainLodConversionResult? result)
    {
        lock (_gate)
        {
            var ready = FindReadyLocked();
            if (ready.Value is null)
            {
                result = null;
                return false;
            }
            result = ready.Value.Result;
            _items.Remove(ready.Key);
            PublishSnapshotLocked();
            return true;
        }
    }

    /// <summary>
    ///     Borrows the next completed result without releasing its bounded ownership. A cache
    ///     writer acknowledges it only after durable publication, so transient I/O failures can
    ///     retry without repeating conversion.
    /// </summary>
    public bool TryPeekCompleted(out TerrainLodConversionResult? result)
    {
        lock (_gate)
        {
            var ready = FindReadyLocked();
            if (ready.Value is not null)
            {
                result = ready.Value.Result;
                return true;
            }
            result = null;
            return false;
        }
    }

    public bool AcknowledgeCompleted(int chunkX, int chunkZ, long terrainRevision)
    {
        lock (_gate)
        {
            var key = new ChunkKey(chunkX, chunkZ);
            if (!_items.TryGetValue(key, out var item) ||
                item.State != WorkState.Ready ||
                item.Result?.TerrainRevision != terrainRevision) return false;
            _items.Remove(key);
            PublishSnapshotLocked();
            return true;
        }
    }

    public bool TryGetFailure(int chunkX, int chunkZ, out string? error)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(new ChunkKey(chunkX, chunkZ), out var item) &&
                item.State == WorkState.Failed)
            {
                error = item.Error;
                return true;
            }
            error = null;
            return false;
        }
    }

    public TerrainLodConversionSnapshot Snapshot() => Volatile.Read(ref _publishedSnapshot);

    private void WorkerLoop()
    {
        while (true)
        {
            ChunkKey key;
            WorkItem item;
            TerrainLodSourceSnapshot source;
            long generation;
            lock (_gate)
            {
                while (true)
                {
                    while (!_disposed && !_items.Values.Any(static item => item.State == WorkState.Queued))
                        Monitor.Wait(_gate);
                    if (_disposed) return;
                    var queued = _items
                        .Where(static pair => pair.Value.State == WorkState.Queued)
                        .OrderBy(static pair => pair.Value.QueueSequence)
                        .First();
                    key = queued.Key;
                    item = queued.Value;
                    source = item.Source;
                    generation = item.Generation;
                    item.RunningSource = source;
                    item.State = WorkState.Running;
                    UpdateMemoryPeaksLocked();
                    PublishSnapshotLocked();
                    break;
                }
            }

            TerrainLodConversionOutput output = default;
            Exception? failure = null;
            try
            {
                output = _convert(source);
                if (output.Hierarchy is null)
                    throw new InvalidOperationException(
                        "Terrain LOD converter returned no hierarchy.");
            }
            catch (Exception error)
            {
                failure = error;
            }

            lock (_gate)
            {
                if (!_items.TryGetValue(key, out var current) || !ReferenceEquals(current, item))
                    continue;
                item.RunningSource = null;
                if (item.Generation != generation)
                {
                    _staleBuildsDiscarded++;
                    QueueLocked(item);
                    Monitor.PulseAll(_gate);
                }
                else if (failure is not null)
                {
                    item.State = WorkState.Failed;
                    item.Error = failure.GetBaseException().Message;
                    _failedConversions++;
                }
                else
                {
                    item.State = WorkState.Ready;
                    item.Result = new TerrainLodConversionResult(
                        _dimension,
                        key.X,
                        key.Z,
                        source.TerrainRevision,
                        output.Hierarchy,
                        output.Lighting,
                        output.RequiresPersistence);
                    _completedConversions++;
                }
                UpdateMemoryPeaksLocked();
                PublishSnapshotLocked();
            }
        }
    }

    private void UpdateMemoryPeaksLocked()
    {
        var sourceBytes = CurrentRetainedSourceBytesLocked();
        var readyCells = CurrentReadyCellsLocked();
        _peakRetainedSourceBytes = Math.Max(_peakRetainedSourceBytes, sourceBytes);
        _peakReadyCells = Math.Max(_peakReadyCells, readyCells);
    }

    private static Func<TerrainLodSourceSnapshot, TerrainLodHierarchy> CreateConverter(
        TerrainLodMaterialCatalog materials,
        TerrainLodReductionStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(materials);
        return source => TerrainLodReducer.Build(source, materials, strategy);
    }

    private void QueueLocked(WorkItem item)
    {
        item.State = WorkState.Queued;
        item.QueueSequence = _nextQueueSequence++;
    }

    private KeyValuePair<ChunkKey, WorkItem> FindReadyLocked() => _items
        .Where(static pair => pair.Value.State == WorkState.Ready && pair.Value.Result is not null)
        .OrderBy(static pair => pair.Key.X)
        .ThenBy(static pair => pair.Key.Z)
        .FirstOrDefault();

    private long CurrentRetainedSourceBytesLocked() => _items.Values.Sum(static item =>
        item.Source.EstimatedBytes +
        (item.RunningSource is not null && !ReferenceEquals(item.RunningSource, item.Source)
            ? item.RunningSource.EstimatedBytes
            : 0));

    private long CurrentReadyCellsLocked() => _items.Values.Sum(static item =>
        item.Result?.Hierarchy.Levels.Sum(static level => (long)level.CellCount) ?? 0);

    private void PublishSnapshotLocked()
    {
        var snapshot = new TerrainLodConversionSnapshot(
            _dimension,
            _capacity,
            _items.Count,
            _items.Values.Count(static item => item.State == WorkState.Queued),
            _items.Values.Count(static item => item.State == WorkState.Running),
            _items.Values.Count(static item => item.State == WorkState.Ready),
            _items.Values.Count(static item => item.State == WorkState.Failed),
            _acceptedSubmissions,
            _coalescedSubmissions,
            _rejectedStaleSubmissions,
            _rejectedCapacitySubmissions,
            _completedConversions,
            _staleBuildsDiscarded,
            _failedConversions,
            _retriedConversions,
            CurrentRetainedSourceBytesLocked(),
            _peakRetainedSourceBytes,
            CurrentReadyCellsLocked(),
            _peakReadyCells);
        Volatile.Write(ref _publishedSnapshot, snapshot);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _items.Clear();
            PublishSnapshotLocked();
            Monitor.PulseAll(_gate);
        }
        if (_worker != Thread.CurrentThread) _worker.Join(TimeSpan.FromSeconds(5));
    }

    private readonly record struct ChunkKey(int X, int Z);
    private enum WorkState { Queued, Running, Ready, Failed }

    private sealed class WorkItem(TerrainLodSourceSnapshot source)
    {
        public TerrainLodSourceSnapshot Source = source;
        public TerrainLodSourceSnapshot? RunningSource;
        public long Generation;
        public long QueueSequence;
        public WorkState State = WorkState.Queued;
        public TerrainLodConversionResult? Result;
        public string? Error;
    }
}
