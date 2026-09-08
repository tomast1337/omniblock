namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>
///     Small, thread-safe two-lane work queue. Gameplay-visible work always overtakes queued
///     background work, and an already queued key can be promoted without being duplicated.
/// </summary>
internal sealed class PriorityWorkScheduler<TKey, TValue> : IDisposable where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> _entries = [];
    private readonly LinkedList<TKey> _background = [];
    private readonly LinkedList<TKey> _urgent = [];
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

    public bool Enqueue(TKey key, TValue value, bool urgent)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_entries.TryGetValue(key, out var existing))
            {
                if (urgent && !existing.Urgent)
                {
                    _background.Remove(existing.Node);
                    existing.Node = _urgent.AddLast(key);
                    existing.Urgent = true;
                }

                return false;
            }

            var queue = urgent ? _urgent : _background;
            _entries.Add(key, new Entry(value, queue.AddLast(key), urgent));
            _available.Release();
            return true;
        }
    }

    public bool Promote(TKey key)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry) || entry.Urgent) return false;
            _background.Remove(entry.Node);
            entry.Node = _urgent.AddLast(key);
            entry.Urgent = true;
            return true;
        }
    }

    public async ValueTask<(TValue Value, bool Urgent)> TakeAsync(CancellationToken cancellationToken)
    {
        await _available.WaitAsync(cancellationToken);

        lock (_gate)
        {
            var queue = _urgent.Count > 0 ? _urgent : _background;
            var node = queue.First!;
            queue.RemoveFirst();
            var entry = _entries[node.Value];
            _entries.Remove(node.Value);
            return (entry.Value, entry.Urgent);
        }
    }

    public IReadOnlyList<TValue> Drain()
    {
        lock (_gate)
        {
            var values = _entries.Values.Select(static entry => entry.Value).ToArray();
            _entries.Clear();
            _urgent.Clear();
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

    private sealed class Entry(TValue value, LinkedListNode<TKey> node, bool urgent)
    {
        public TValue Value { get; } = value;
        public LinkedListNode<TKey> Node { get; set; } = node;
        public bool Urgent { get; set; } = urgent;
    }
}
