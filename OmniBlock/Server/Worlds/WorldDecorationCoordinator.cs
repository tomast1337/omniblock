using System.Globalization;
using OmniBlock.Util.Maths;

namespace OmniBlock.Server.Worlds;

public readonly record struct DecorationCoordinatorSnapshot(
    int Queued,
    int Running,
    int Completed,
    int Owners);

/// <summary>
///     Owns inactive decoration admission for one world and dimension. Decoration remains
///     serialized even when two requests cover different-but-overlapping dependency regions;
///     identical canonical target sets share one transaction across owners.
/// </summary>
public sealed class WorldDecorationCoordinator : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<IReadOnlyList<ChunkPos>, CancellationToken, InactiveGenerationBatch> _execute;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly PriorityQueue<Entry, (GenerationRequestPriority Priority, long Sequence)> _queue = new();
    private readonly Thread _worker;
    private bool _disposed;
    private long _sequence;

    public WorldDecorationCoordinator(
        Func<IReadOnlyList<ChunkPos>, CancellationToken, InactiveGenerationBatch> execute)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "WorldDecoration"
        };
        _worker.Start();
    }

    public DecorationRequest Request(
        IEnumerable<ChunkPos> targets,
        string owner,
        GenerationRequestPriority priority)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        var canonicalTargets = targets
            .Distinct()
            .OrderBy(static target => target.X)
            .ThenBy(static target => target.Z)
            .ToArray();
        if (canonicalTargets.Length == 0)
            throw new ArgumentException("At least one decoration target is required.", nameof(targets));
        var key = CreateKey(canonicalTargets);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var entry = _entries.GetValueOrDefault(key) ?? CreateEntry(key, canonicalTargets);
            if (!entry.Owners.TryAdd(owner, priority))
                throw new InvalidOperationException(
                    $"Owner '{owner}' already owns inactive decoration region '{key}'.");

            var previousPriority = entry.Priority;
            entry.RecalculatePriority();
            if (entry.State == WorkState.Queued && entry.Priority != previousPriority)
                Requeue(entry);
            Monitor.PulseAll(_gate);
            return new DecorationRequest(this, entry, owner, entry.Completion.Task);
        }
    }

    public DecorationCoordinatorSnapshot Snapshot()
    {
        lock (_gate)
            return new DecorationCoordinatorSnapshot(
                _entries.Values.Count(static entry => entry.State == WorkState.Queued),
                _entries.Values.Count(static entry => entry.State == WorkState.Running),
                _entries.Values.Count(static entry => entry.State == WorkState.Completed),
                _entries.Values.Sum(static entry => entry.Owners.Count));
    }

    private Entry CreateEntry(string key, ChunkPos[] targets)
    {
        var entry = new Entry(key, targets, _sequence++);
        _entries.Add(key, entry);
        Enqueue(entry);
        return entry;
    }

    private void Enqueue(Entry entry)
    {
        entry.State = WorkState.Queued;
        _queue.Enqueue(entry, (entry.Priority, entry.Sequence));
    }

    private void Requeue(Entry entry)
    {
        _queue.Remove(entry, out _, out _);
        Enqueue(entry);
    }

    private void Release(Entry entry, string owner)
    {
        lock (_gate)
        {
            if (!entry.Owners.Remove(owner)) return;
            if (entry.Owners.Count > 0)
            {
                var previousPriority = entry.Priority;
                entry.RecalculatePriority();
                if (entry.State == WorkState.Queued && entry.Priority != previousPriority)
                    Requeue(entry);
                return;
            }

            if (entry.State == WorkState.Queued) _queue.Remove(entry, out _, out _);
            if (_entries.GetValueOrDefault(entry.Key) == entry) _entries.Remove(entry.Key);
            entry.Cancellation.Cancel();
            entry.Completion.TrySetCanceled(entry.Cancellation.Token);
        }
    }

    private void WorkerLoop()
    {
        while (true)
        {
            Entry entry;
            lock (_gate)
            {
                while (!_disposed && _queue.Count == 0) Monitor.Wait(_gate);
                if (_disposed) return;
                entry = _queue.Dequeue();
                if (entry.Owners.Count == 0 || entry.Cancellation.IsCancellationRequested) continue;
                entry.State = WorkState.Running;
            }

            try
            {
                var result = _execute(entry.Targets, entry.Cancellation.Token);
                lock (_gate)
                {
                    if (_entries.GetValueOrDefault(entry.Key) != entry || entry.Owners.Count == 0)
                        continue;
                    entry.State = WorkState.Completed;
                    entry.Completion.TrySetResult(result);
                }
            }
            catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested)
            {
                entry.Completion.TrySetCanceled(entry.Cancellation.Token);
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    entry.State = WorkState.Completed;
                    entry.Completion.TrySetException(error);
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _entries.Values)
            {
                entry.Cancellation.Cancel();
                entry.Completion.TrySetCanceled(entry.Cancellation.Token);
            }
            _queue.Clear();
            _entries.Clear();
            Monitor.PulseAll(_gate);
        }

        if (_worker != Thread.CurrentThread) _worker.Join(TimeSpan.FromSeconds(5));
    }

    private static string CreateKey(IEnumerable<ChunkPos> targets) =>
        string.Join(';', targets.Select(static target =>
            string.Create(CultureInfo.InvariantCulture, $"{target.X},{target.Z}")));

    private sealed class Entry(string key, ChunkPos[] targets, long sequence)
    {
        public string Key { get; } = key;
        public IReadOnlyList<ChunkPos> Targets { get; } = Array.AsReadOnly(targets);
        public long Sequence { get; } = sequence;
        public Dictionary<string, GenerationRequestPriority> Owners { get; } =
            new(StringComparer.Ordinal);
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<InactiveGenerationBatch> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public GenerationRequestPriority Priority { get; private set; } =
            GenerationRequestPriority.Background;
        public WorkState State { get; set; }

        public void RecalculatePriority() => Priority = Owners.Values.Min();
    }

    private enum WorkState { Queued, Running, Completed }

    public sealed class DecorationRequest : IDisposable
    {
        private readonly WorldDecorationCoordinator _coordinator;
        private readonly object _entry;
        private readonly string _owner;
        private int _disposed;

        internal DecorationRequest(
            WorldDecorationCoordinator coordinator,
            object entry,
            string owner,
            Task<InactiveGenerationBatch> completion)
        {
            _coordinator = coordinator;
            _entry = entry;
            _owner = owner;
            Completion = completion;
        }

        public Task<InactiveGenerationBatch> Completion { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _coordinator.Release((Entry)_entry, _owner);
        }
    }
}
