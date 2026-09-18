using System.Collections.ObjectModel;
using System.Diagnostics;

namespace OmniBlock.Worlds.Lod;

public enum TerrainLodParentWorkKind
{
    Coverage,
    Refinement
}

public enum TerrainLodParentAdmissionResult
{
    Accepted,
    Coalesced,
    RejectedAtCapacity
}

public sealed record TerrainLodParentConstructionResult(
    TerrainLodColumnTile Tile,
    TerrainLodParentWorkKind WorkKind,
    double DistanceChunks,
    long QueueSequence);

public sealed record TerrainLodParentLevelSnapshot(
    int SpatialLevel,
    int Queued,
    int Running,
    int Ready,
    int Failed,
    double OldestCoverageAgeMs,
    double OldestRefinementAgeMs);

public sealed record TerrainLodParentConstructionSnapshot(
    int Capacity,
    int CompletedCapacity,
    int Owned,
    int Queued,
    int Running,
    int Ready,
    int Failed,
    long AcceptedSubmissions,
    long CoalescedSubmissions,
    long RejectedCapacitySubmissions,
    long CompletedConstructions,
    long StaleConstructionsDiscarded,
    long FailedConstructions,
    long RetriedConstructions,
    long RetainedInputColumns,
    long ReadyColumns,
    ReadOnlyCollection<TerrainLodParentLevelSnapshot> Levels);

internal sealed record TerrainLodParentBuildInput(
    TerrainLodTileKey Key,
    TerrainLodColumnTile[] Children,
    int HorizontalSampleLevel,
    TerrainLodParentWorkKind WorkKind,
    double DistanceChunks,
    long QueueSequence);

/// <summary>
///     Bounded CPU owner for cross-chunk parent construction. A tile key owns at most one request;
///     a newer ordered child set replaces queued, running, ready, or failed work for that key.
///     Coverage and refinement remain separate scheduling lanes, with coarse tiles leading inside
///     each lane. A completed candidate is published only if its captured generation is current.
/// </summary>
public sealed class TerrainLodParentConstructionService : IDisposable
{
    private const int CoverageBurstLimit = 8;

    private readonly object _gate = new();
    private readonly int _capacity;
    private readonly int _completedCapacity;
    private readonly Func<TerrainLodParentBuildInput, TerrainLodColumnTile> _build;
    private readonly Dictionary<TerrainLodTileKey, WorkItem> _items = [];
    private readonly Thread _worker;
    private bool _disposed;
    private long _nextQueueSequence;
    private int _workCoverageSinceRefinement;
    private int _resultCoverageSinceRefinement;
    private long _acceptedSubmissions;
    private long _coalescedSubmissions;
    private long _rejectedCapacitySubmissions;
    private long _completedConstructions;
    private long _staleConstructionsDiscarded;
    private long _failedConstructions;
    private long _retriedConstructions;

    public TerrainLodParentConstructionService(
        int capacity = 64,
        int completedCapacity = 16)
        : this(capacity, completedCapacity, static input => TerrainLodColumnTile.BuildParent(
            input.Key, input.Children, input.HorizontalSampleLevel)) { }

    internal TerrainLodParentConstructionService(
        int capacity,
        int completedCapacity,
        Func<TerrainLodParentBuildInput, TerrainLodColumnTile> build)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (completedCapacity <= 0 || completedCapacity > capacity)
            throw new ArgumentOutOfRangeException(nameof(completedCapacity));
        _capacity = capacity;
        _completedCapacity = completedCapacity;
        _build = build ?? throw new ArgumentNullException(nameof(build));
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "TerrainLOD-Parents"
        };
        _worker.Start();
    }

    public TerrainLodParentAdmissionResult Submit(
        TerrainLodTileKey key,
        IReadOnlyList<TerrainLodColumnTile> children,
        int horizontalSampleLevel,
        TerrainLodParentWorkKind workKind,
        double distanceChunks)
    {
        var input = CreateInput(
            key, children, horizontalSampleLevel, workKind, distanceChunks);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_items.TryGetValue(key, out var existing))
            {
                existing.Input = input with { QueueSequence = NextQueueSequenceLocked() };
                existing.Generation++;
                existing.Error = null;
                existing.Result = null;
                _coalescedSubmissions++;
                if (existing.State != WorkState.Running) existing.State = WorkState.Queued;
                Monitor.PulseAll(_gate);
                return TerrainLodParentAdmissionResult.Coalesced;
            }

            if (_items.Count >= _capacity)
            {
                _rejectedCapacitySubmissions++;
                return TerrainLodParentAdmissionResult.RejectedAtCapacity;
            }

            input = input with { QueueSequence = NextQueueSequenceLocked() };
            _items.Add(key, new WorkItem(input));
            _acceptedSubmissions++;
            Monitor.PulseAll(_gate);
            return TerrainLodParentAdmissionResult.Accepted;
        }
    }

    public bool Retry(TerrainLodTileKey key)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_items.TryGetValue(key, out var item) || item.State != WorkState.Failed)
                return false;
            item.Input = item.Input with { QueueSequence = NextQueueSequenceLocked() };
            item.State = WorkState.Queued;
            item.Error = null;
            _retriedConstructions++;
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public bool Discard(TerrainLodTileKey key)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_items.Remove(key)) return false;
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public bool TryTakeCompleted(out TerrainLodParentConstructionResult? result)
    {
        lock (_gate)
        {
            var ready = SelectReadyLocked();
            if (ready.Value is null)
            {
                result = null;
                return false;
            }
            result = ready.Value.Result;
            _items.Remove(ready.Key);
            CommitFairness(ref _resultCoverageSinceRefinement, result!.WorkKind);
            Monitor.PulseAll(_gate);
            return true;
        }
    }

    public bool TryGetFailure(TerrainLodTileKey key, out string? error)
    {
        lock (_gate)
        {
            if (_items.TryGetValue(key, out var item) && item.State == WorkState.Failed)
            {
                error = item.Error;
                return true;
            }
            error = null;
            return false;
        }
    }

    public TerrainLodParentConstructionSnapshot Snapshot()
    {
        lock (_gate)
        {
            var now = Stopwatch.GetTimestamp();
            var levels = _items.Values
                .GroupBy(static item => item.Input.Key.Level)
                .OrderBy(static group => group.Key)
                .Select(group => new TerrainLodParentLevelSnapshot(
                    group.Key,
                    group.Count(static item => item.State == WorkState.Queued),
                    group.Count(static item => item.State == WorkState.Running),
                    group.Count(static item => item.State == WorkState.Ready),
                    group.Count(static item => item.State == WorkState.Failed),
                    OldestAgeMs(group, TerrainLodParentWorkKind.Coverage, now),
                    OldestAgeMs(group, TerrainLodParentWorkKind.Refinement, now)))
                .ToArray();
            return new TerrainLodParentConstructionSnapshot(
                _capacity,
                _completedCapacity,
                _items.Count,
                _items.Values.Count(static item => item.State == WorkState.Queued),
                _items.Values.Count(static item => item.State == WorkState.Running),
                _items.Values.Count(static item => item.State == WorkState.Ready),
                _items.Values.Count(static item => item.State == WorkState.Failed),
                _acceptedSubmissions,
                _coalescedSubmissions,
                _rejectedCapacitySubmissions,
                _completedConstructions,
                _staleConstructionsDiscarded,
                _failedConstructions,
                _retriedConstructions,
                _items.Values.Sum(static item => InputColumns(item.Input)),
                _items.Values
                    .Where(static item => item.State == WorkState.Ready)
                    .Sum(static item => (long)item.Result!.Tile.Width * item.Result.Tile.Width),
                Array.AsReadOnly(levels));
        }
    }

    private void WorkerLoop()
    {
        while (true)
        {
            WorkItem item;
            TerrainLodParentBuildInput input;
            long generation;
            lock (_gate)
            {
                while (!_disposed &&
                       (!HasQueuedLocked() || ReadyCountLocked() >= _completedCapacity))
                    Monitor.Wait(_gate);
                if (_disposed) return;
                item = SelectQueuedLocked();
                input = item.Input;
                generation = item.Generation;
                item.State = WorkState.Running;
            }

            TerrainLodColumnTile? tile = null;
            Exception? failure = null;
            try
            {
                tile = _build(input);
                if (tile.Key != input.Key)
                    throw new InvalidOperationException(
                        $"Parent builder returned {tile.Key}; expected {input.Key}.");
                if (tile.HorizontalSampleLevel != input.HorizontalSampleLevel)
                    throw new InvalidOperationException(
                        $"Parent builder returned horizontal sample level " +
                        $"{tile.HorizontalSampleLevel}; expected {input.HorizontalSampleLevel}.");
                if (!tile.InputHashes.SequenceEqual(
                        input.Children.Select(static child => child.CanonicalHash)))
                    throw new InvalidOperationException(
                        $"Parent builder returned stale child identities for {input.Key}.");
            }
            catch (Exception error)
            {
                failure = error;
            }

            lock (_gate)
            {
                if (!_items.TryGetValue(input.Key, out var current) ||
                    !ReferenceEquals(current, item))
                    continue;
                if (item.Generation != generation)
                {
                    _staleConstructionsDiscarded++;
                    item.State = WorkState.Queued;
                    Monitor.PulseAll(_gate);
                }
                else if (failure is not null)
                {
                    item.State = WorkState.Failed;
                    item.Error = failure.GetBaseException().Message;
                    _failedConstructions++;
                }
                else
                {
                    item.State = WorkState.Ready;
                    item.Result = new TerrainLodParentConstructionResult(
                        tile!, input.WorkKind, input.DistanceChunks, input.QueueSequence);
                    _completedConstructions++;
                }
            }
        }
    }

    private TerrainLodParentBuildInput CreateInput(
        TerrainLodTileKey key,
        IReadOnlyList<TerrainLodColumnTile> children,
        int horizontalSampleLevel,
        TerrainLodParentWorkKind workKind,
        double distanceChunks)
    {
        if (key.Level == 0)
            throw new ArgumentException("Parent construction requires a non-leaf tile key.",
                nameof(key));
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count != 4)
            throw new ArgumentException("Parent construction requires four children.",
                nameof(children));
        if (horizontalSampleLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(horizontalSampleLevel));
        if (!Enum.IsDefined(workKind)) throw new ArgumentOutOfRangeException(nameof(workKind));
        if (!double.IsFinite(distanceChunks) || distanceChunks < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceChunks));
        var ownedChildren = new TerrainLodColumnTile[4];
        for (var index = 0; index < ownedChildren.Length; index++)
        {
            var child = children[index] ?? throw new ArgumentException(
                $"Parent child {index} is null.", nameof(children));
            if (child.Key != key.Child(index))
                throw new ArgumentException(
                    $"Parent child {index} is {child.Key}; expected {key.Child(index)}.",
                    nameof(children));
            ownedChildren[index] = child;
        }
        return new TerrainLodParentBuildInput(
            key,
            ownedChildren,
            horizontalSampleLevel,
            workKind,
            distanceChunks,
            QueueSequence: -1);
    }

    private WorkItem SelectQueuedLocked()
    {
        var hasCoverage = _items.Values.Any(static item =>
            item.State == WorkState.Queued &&
            item.Input.WorkKind == TerrainLodParentWorkKind.Coverage);
        var hasRefinement = _items.Values.Any(static item =>
            item.State == WorkState.Queued &&
            item.Input.WorkKind == TerrainLodParentWorkKind.Refinement);
        var kind = PeekFairness(
            hasCoverage, hasRefinement, _workCoverageSinceRefinement);
        var selected = _items.Values
            .Where(item => item.State == WorkState.Queued && item.Input.WorkKind == kind)
            .OrderByDescending(static item => item.Input.Key.Level)
            .ThenBy(static item => item.Input.DistanceChunks)
            .ThenBy(static item => item.Input.QueueSequence)
            .ThenBy(static item => item.Input.Key.X)
            .ThenBy(static item => item.Input.Key.Z)
            .First();
        CommitFairness(ref _workCoverageSinceRefinement, kind);
        return selected;
    }

    private KeyValuePair<TerrainLodTileKey, WorkItem> SelectReadyLocked()
    {
        var hasCoverage = _items.Values.Any(static item =>
            item.State == WorkState.Ready &&
            item.Input.WorkKind == TerrainLodParentWorkKind.Coverage);
        var hasRefinement = _items.Values.Any(static item =>
            item.State == WorkState.Ready &&
            item.Input.WorkKind == TerrainLodParentWorkKind.Refinement);
        if (!hasCoverage && !hasRefinement) return default;
        var kind = PeekFairness(
            hasCoverage, hasRefinement, _resultCoverageSinceRefinement);
        return _items
            .Where(pair => pair.Value.State == WorkState.Ready &&
                           pair.Value.Input.WorkKind == kind)
            .OrderByDescending(static pair => pair.Key.Level)
            .ThenBy(static pair => pair.Value.Input.DistanceChunks)
            .ThenBy(static pair => pair.Value.Input.QueueSequence)
            .ThenBy(static pair => pair.Key.X)
            .ThenBy(static pair => pair.Key.Z)
            .First();
    }

    private static TerrainLodParentWorkKind PeekFairness(
        bool hasCoverage,
        bool hasRefinement,
        int coverageSinceRefinement)
    {
        if (hasRefinement && (!hasCoverage || coverageSinceRefinement >= CoverageBurstLimit))
            return TerrainLodParentWorkKind.Refinement;
        return TerrainLodParentWorkKind.Coverage;
    }

    private static void CommitFairness(
        ref int coverageSinceRefinement,
        TerrainLodParentWorkKind kind)
    {
        if (kind == TerrainLodParentWorkKind.Refinement) coverageSinceRefinement = 0;
        else coverageSinceRefinement++;
    }

    private long NextQueueSequenceLocked() => _nextQueueSequence++;
    private bool HasQueuedLocked() =>
        _items.Values.Any(static item => item.State == WorkState.Queued);
    private int ReadyCountLocked() =>
        _items.Values.Count(static item => item.State == WorkState.Ready);

    private static long InputColumns(TerrainLodParentBuildInput input) =>
        input.Children.Sum(static child => (long)child.Width * child.Width);

    private static double OldestAgeMs(
        IEnumerable<WorkItem> items,
        TerrainLodParentWorkKind kind,
        long now)
    {
        var oldest = items
            .Where(item => item.State == WorkState.Queued && item.Input.WorkKind == kind)
            .Select(static item => item.QueuedAtTimestamp)
            .DefaultIfEmpty(now)
            .Min();
        return Stopwatch.GetElapsedTime(oldest, now).TotalMilliseconds;
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
        if (_worker != Thread.CurrentThread) _worker.Join(TimeSpan.FromSeconds(5));
    }

    private enum WorkState
    {
        Queued,
        Running,
        Ready,
        Failed
    }

    private sealed class WorkItem(TerrainLodParentBuildInput input)
    {
        private TerrainLodParentBuildInput _input = input;

        public TerrainLodParentBuildInput Input
        {
            get => _input;
            set
            {
                _input = value;
                QueuedAtTimestamp = Stopwatch.GetTimestamp();
            }
        }

        public long Generation;
        public long QueuedAtTimestamp { get; private set; } = Stopwatch.GetTimestamp();
        public WorkState State = WorkState.Queued;
        public TerrainLodParentConstructionResult? Result;
        public string? Error;
    }
}
