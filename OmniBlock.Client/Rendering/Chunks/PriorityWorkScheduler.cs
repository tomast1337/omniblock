namespace OmniBlock.Client.Rendering.Chunks;

internal enum MeshWorkPriority : byte
{
    Background,
    Foreground,
    Critical
}

/// <summary>
///     Keeps strict priority responsive without allowing an indefinitely replenished higher lane
///     (flowing fluids and their lighting are the common case) to stop world streaming entirely.
/// </summary>
internal sealed class MeshPriorityFairness
{
    internal const int CriticalBurstLimit = 8;
    internal const int HigherPriorityBurstLimit = 16;

    private int _criticalSinceForeground;
    private int _higherPrioritySinceBackground;

    public MeshWorkPriority Select(bool hasCritical, bool hasForeground, bool hasBackground)
    {
        if (hasBackground && _higherPrioritySinceBackground >= HigherPriorityBurstLimit)
        {
            _higherPrioritySinceBackground = 0;
            _criticalSinceForeground = 0;
            return MeshWorkPriority.Background;
        }

        if (hasForeground && _criticalSinceForeground >= CriticalBurstLimit)
        {
            _criticalSinceForeground = 0;
            _higherPrioritySinceBackground++;
            return MeshWorkPriority.Foreground;
        }

        if (hasCritical)
        {
            _criticalSinceForeground++;
            _higherPrioritySinceBackground++;
            return MeshWorkPriority.Critical;
        }

        if (hasForeground)
        {
            _criticalSinceForeground = 0;
            _higherPrioritySinceBackground++;
            return MeshWorkPriority.Foreground;
        }

        _criticalSinceForeground = 0;
        _higherPrioritySinceBackground = 0;
        return MeshWorkPriority.Background;
    }
}

/// <summary>
///     Small, thread-safe three-lane work queue. Gameplay changes overtake startup/safety-ring
///     work, which in turn overtakes background streaming. An already queued key can be promoted
///     without being duplicated.
/// </summary>
internal sealed class PriorityWorkScheduler<TKey, TValue> : IDisposable where TKey : notnull
{
    private readonly LinkedList<TKey> _critical = [];
    private readonly Dictionary<TKey, Entry> _entries = [];
    private readonly LinkedList<TKey> _background = [];
    private readonly LinkedList<TKey> _foreground = [];
    private readonly MeshPriorityFairness _fairness = new();
    private readonly SemaphoreSlim _available = new(0);
    private readonly object _gate = new();
    private bool _disposed;

    public int Count
    {
        get
        {
            lock (_gate) return _entries.Count;
        }
    }

    public bool Enqueue(TKey key, TValue value, MeshWorkPriority priority)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_entries.TryGetValue(key, out var existing))
            {
                PromoteEntry(key, existing, priority);

                return false;
            }

            var queue = QueueFor(priority);
            _entries.Add(key, new Entry(value, queue.AddLast(key), priority));
            _available.Release();
            return true;
        }
    }

    public bool Promote(TKey key, MeshWorkPriority priority)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry) || priority <= entry.Priority) return false;
            PromoteEntry(key, entry, priority);
            return true;
        }
    }

    /// <summary>
    ///     Reorders queued work inside each lane without changing lane priority or availability.
    ///     Work already claimed by a consumer is intentionally unaffected.
    /// </summary>
    public void ReorderWithinPriorities(Comparison<TKey> comparison)
    {
        lock (_gate)
        {
            Reorder(_critical);
            Reorder(_foreground);
            Reorder(_background);
        }

        void Reorder(LinkedList<TKey> queue)
        {
            if (queue.Count < 2) return;

            var keys = queue.ToList();
            keys.Sort(comparison);
            queue.Clear();
            foreach (var key in keys)
            {
                var node = queue.AddLast(key);
                _entries[key].Node = node;
            }
        }
    }

    public async ValueTask<(TValue Value, MeshWorkPriority Priority)> TakeAsync(CancellationToken cancellationToken)
    {
        await _available.WaitAsync(cancellationToken);

        lock (_gate)
        {
            var priority = _fairness.Select(_critical.Count > 0, _foreground.Count > 0, _background.Count > 0);
            var queue = QueueFor(priority);
            var node = queue.First!;
            queue.RemoveFirst();
            var entry = _entries[node.Value];
            _entries.Remove(node.Value);
            return (entry.Value, entry.Priority);
        }
    }

    public IReadOnlyList<TValue> Drain()
    {
        lock (_gate)
        {
            var values = _entries.Values.Select(static entry => entry.Value).ToArray();
            _entries.Clear();
            _critical.Clear();
            _foreground.Clear();
            _background.Clear();
            return values;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        _available.Dispose();
    }

    private LinkedList<TKey> QueueFor(MeshWorkPriority priority) => priority switch
    {
        MeshWorkPriority.Critical => _critical,
        MeshWorkPriority.Foreground => _foreground,
        _ => _background
    };

    private void PromoteEntry(TKey key, Entry entry, MeshWorkPriority priority)
    {
        if (priority <= entry.Priority) return;
        QueueFor(entry.Priority).Remove(entry.Node);
        entry.Node = QueueFor(priority).AddLast(key);
        entry.Priority = priority;
    }

    private sealed class Entry(TValue value, LinkedListNode<TKey> node, MeshWorkPriority priority)
    {
        public TValue Value { get; } = value;
        public LinkedListNode<TKey> Node { get; set; } = node;
        public MeshWorkPriority Priority { get; set; } = priority;
    }
}
