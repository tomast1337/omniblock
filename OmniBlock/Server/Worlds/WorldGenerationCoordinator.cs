namespace OmniBlock.Server.Worlds;

public enum GenerationDesiredStage
{
    Terrain,
    Decorated,
    Lit,
    Saved,
    Activated
}

public enum GenerationPriorityClass
{
    RelocationCritical,
    Gameplay,
    Background
}

/// <summary>
///     A stable generation ordering shared by player streaming and future background owners.
///     The class protects gameplay from maintenance work; distance and direction preserve the
///     near-to-far streaming wavefront within that class.
/// </summary>
public readonly record struct GenerationRequestPriority(
    GenerationPriorityClass Class,
    int Distance,
    double DirectionPenalty) : IComparable<GenerationRequestPriority>
{
    public static GenerationRequestPriority RelocationCritical { get; } =
        new(GenerationPriorityClass.RelocationCritical, 0, 0);

    public static GenerationRequestPriority Gameplay { get; } =
        new(GenerationPriorityClass.Gameplay, 0, 0);

    public static GenerationRequestPriority Background { get; } =
        new(GenerationPriorityClass.Background, 0, 0);

    public static GenerationRequestPriority GameplayAt(int distance, double directionPenalty) =>
        new(GenerationPriorityClass.Gameplay, distance, directionPenalty);

    public static GenerationRequestPriority BackgroundAt(int distance) =>
        new(GenerationPriorityClass.Background, distance, 0);

    public int CompareTo(GenerationRequestPriority other) =>
        (Class, Distance, DirectionPenalty).CompareTo(
            (other.Class, other.Distance, other.DirectionPenalty));
}

public readonly record struct GenerationWorkKey(
    string WorldId,
    int DimensionId,
    int ChunkX,
    int ChunkZ);

public readonly record struct GenerationWorkContext(
    GenerationWorkKey Key,
    GenerationDesiredStage DesiredStage,
    long Revision);

public readonly record struct GenerationCoordinatorSnapshot(
    int Queued,
    int Running,
    int Completed,
    int Owners,
    int Workers);

/// <summary>
///     Bounded, job-independent ownership for generation work. Same-coordinate requests share one
///     execution; stage and priority can be promoted, while a newer revision cancels stale work.
/// </summary>
public sealed class WorldGenerationCoordinator<TResult> : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<GenerationWorkContext, CancellationToken, TResult> _execute;
    private readonly Dictionary<GenerationWorkKey, Entry> _entries = [];
    private readonly PriorityQueue<Entry, (GenerationRequestPriority Priority, long Sequence)> _queue = new();
    private readonly Thread[] _workers;
    private bool _disposed;
    private long _sequence;

    public WorldGenerationCoordinator(
        int workerCount,
        Func<GenerationWorkContext, CancellationToken, TResult> execute)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workerCount, 1);
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _workers = new Thread[workerCount];
        for (var i = 0; i < workerCount; i++)
        {
            _workers[i] = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"WorldGeneration-{i}"
            };
            _workers[i].Start();
        }
    }

    public GenerationRequest<TResult> Request(
        GenerationWorkKey key,
        string owner,
        GenerationDesiredStage desiredStage,
        GenerationRequestPriority priority,
        long revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.TryGetValue(key, out var existing) && revision != existing.Revision)
            {
                if (revision < existing.Revision)
                    throw new InvalidOperationException(
                        $"Generation request for {key} has stale revision {revision}; current is {existing.Revision}.");
                Supersede(existing);
                existing = null;
            }

            var entry = existing ?? CreateEntry(key, revision);
            if (entry.Owners.ContainsKey(owner))
                throw new InvalidOperationException($"Owner '{owner}' already owns generation request {key}.");
            if (entry.State == WorkState.Completed && desiredStage > entry.DesiredStage)
                throw new InvalidOperationException(
                    $"Completed generation request {key} cannot be promoted without a newer revision.");
            entry.Owners[owner] = new OwnerDemand(desiredStage, priority);
            var previousStage = entry.DesiredStage;
            entry.RecalculateDemand();
            if (entry.State == WorkState.Queued &&
                (entry.Priority != entry.EnqueuedPriority || entry.DesiredStage != previousStage))
                Requeue(entry);
            Monitor.PulseAll(_gate);
            return new GenerationRequest<TResult>(this, entry, owner, entry.Completion.Task);
        }
    }

    private void UpdateDemand(
        Entry entry,
        string owner,
        GenerationDesiredStage desiredStage,
        GenerationRequestPriority priority)
    {
        lock (_gate)
        {
            if (_disposed || _entries.GetValueOrDefault(entry.Key) != entry ||
                !entry.Owners.ContainsKey(owner))
                return;

            if (entry.State == WorkState.Completed && desiredStage > entry.DesiredStage)
                throw new InvalidOperationException(
                    $"Completed generation request {entry.Key} cannot be promoted without a newer revision.");

            var previousStage = entry.DesiredStage;
            var previousPriority = entry.Priority;
            entry.Owners[owner] = new OwnerDemand(desiredStage, priority);
            entry.RecalculateDemand();
            if (entry.State == WorkState.Queued &&
                (entry.Priority != previousPriority || entry.DesiredStage != previousStage))
                Requeue(entry);
            Monitor.PulseAll(_gate);
        }
    }

    public GenerationCoordinatorSnapshot Snapshot()
    {
        lock (_gate)
            return new GenerationCoordinatorSnapshot(
                _entries.Values.Count(static entry => entry.State == WorkState.Queued),
                _entries.Values.Count(static entry => entry.State == WorkState.Running),
                _entries.Values.Count(static entry => entry.State == WorkState.Completed),
                _entries.Values.Sum(static entry => entry.Owners.Count),
                _workers.Length);
    }

    private Entry CreateEntry(GenerationWorkKey key, long revision)
    {
        var entry = new Entry(key, revision, _sequence++);
        _entries.Add(key, entry);
        Enqueue(entry);
        return entry;
    }

    private void Enqueue(Entry entry)
    {
        entry.State = WorkState.Queued;
        entry.EnqueuedPriority = entry.Priority;
        _queue.Enqueue(entry, (entry.Priority, entry.Sequence));
    }

    private void Requeue(Entry entry)
    {
        _queue.Remove(entry, out _, out _);
        Enqueue(entry);
    }

    private void Supersede(Entry entry)
    {
        if (entry.State == WorkState.Queued) _queue.Remove(entry, out _, out _);
        _entries.Remove(entry.Key);
        entry.Cancellation.Cancel();
        entry.Completion.TrySetCanceled(entry.Cancellation.Token);
    }

    private void Release(Entry entry, string owner)
    {
        lock (_gate)
        {
            if (!entry.Owners.Remove(owner)) return;
            if (entry.Owners.Count > 0)
            {
                entry.RecalculateDemand();
                if (entry.State == WorkState.Queued) Requeue(entry);
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
            GenerationWorkContext context;
            lock (_gate)
            {
                while (!_disposed && _queue.Count == 0) Monitor.Wait(_gate);
                if (_disposed) return;
                entry = _queue.Dequeue();
                if (entry.Owners.Count == 0 || entry.Cancellation.IsCancellationRequested) continue;
                entry.State = WorkState.Running;
                context = new GenerationWorkContext(entry.Key, entry.DesiredStage, entry.Revision);
            }

            try
            {
                var result = _execute(context, entry.Cancellation.Token);
                lock (_gate)
                {
                    if (_entries.GetValueOrDefault(entry.Key) != entry || entry.Owners.Count == 0)
                        continue;
                    if (entry.DesiredStage > context.DesiredStage)
                    {
                        Enqueue(entry);
                        Monitor.PulseAll(_gate);
                        continue;
                    }
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
        foreach (var worker in _workers)
            if (worker != Thread.CurrentThread) worker.Join(TimeSpan.FromSeconds(5));
    }

    private sealed class Entry(GenerationWorkKey key, long revision, long sequence)
    {
        public GenerationWorkKey Key { get; } = key;
        public long Revision { get; } = revision;
        public long Sequence { get; } = sequence;
        public Dictionary<string, OwnerDemand> Owners { get; } = new(StringComparer.Ordinal);
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<TResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public GenerationDesiredStage DesiredStage { get; private set; }
        public GenerationRequestPriority Priority { get; private set; } = GenerationRequestPriority.Background;
        public GenerationRequestPriority EnqueuedPriority { get; set; } = GenerationRequestPriority.Background;
        public WorkState State { get; set; }

        public void RecalculateDemand()
        {
            DesiredStage = Owners.Values.Max(static owner => owner.Stage);
            Priority = Owners.Values.Min(static owner => owner.Priority);
        }
    }

    private readonly record struct OwnerDemand(
        GenerationDesiredStage Stage,
        GenerationRequestPriority Priority);
    private enum WorkState { Queued, Running, Completed }

    public sealed class GenerationRequest<T> : IDisposable
    {
        private readonly WorldGenerationCoordinator<T> _coordinator;
        private readonly object _entry;
        private readonly string _owner;
        private int _disposed;

        internal GenerationRequest(WorldGenerationCoordinator<T> coordinator, object entry, string owner,
            Task<T> completion)
        {
            _coordinator = coordinator;
            _entry = entry;
            _owner = owner;
            Completion = completion;
        }

        public Task<T> Completion { get; }

        public void UpdateDemand(
            GenerationDesiredStage desiredStage,
            GenerationRequestPriority priority)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            _coordinator.UpdateDemand((WorldGenerationCoordinator<T>.Entry)_entry, _owner,
                desiredStage, priority);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _coordinator.Release((WorldGenerationCoordinator<T>.Entry)_entry, _owner);
        }
    }
}
