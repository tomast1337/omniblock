using Silk.NET.Maths;

namespace OmniBlock.Client.Rendering.Chunks;

/// <summary>
///     Main-thread keyed queue for section mesh requests. A section owns one live entry, while
///     stale heap nodes created by promotion are discarded lazily.
/// </summary>
internal sealed class SectionMeshRequestQueue
{
    internal delegate (int Tier, double DistanceSquared, long EnqueuedAt) Ranker(SectionRenderState state);

    private readonly PriorityQueue<QueueNode, QueueRank>[] _lanes =
    [
        new(),
        new(),
        new()
    ];
    private readonly Dictionary<Vector3D<int>, Entry> _entries = [];
    private readonly MeshPriorityFairness _fairness = new();
    private long _token;

    public int Count => _entries.Count;
    public IEnumerable<SectionRenderState> Items => _entries.Values.Select(static entry => entry.State);

    public int CountPriority(MeshWorkPriority priority) =>
        _entries.Values.Count(entry => entry.State.RequestedPriority == priority);

    public bool Contains(Vector3D<int> position) => _entries.ContainsKey(position);

    public bool Enqueue(SectionRenderState state, in (int Tier, double DistanceSquared, long EnqueuedAt) rank)
    {
        if (_entries.TryGetValue(state.Position, out var existing))
        {
            existing.State = state;
            existing.QueuedPriority = state.RequestedPriority;
            Push(existing, rank);
            return false;
        }

        var entry = new Entry(state);
        _entries.Add(state.Position, entry);
        Push(entry, rank);
        return true;
    }

    public bool Promote(
        Vector3D<int> position,
        in (int Tier, double DistanceSquared, long EnqueuedAt) rank)
    {
        if (!_entries.TryGetValue(position, out var entry) ||
            entry.State.RequestedPriority <= entry.QueuedPriority)
            return false;

        entry.QueuedPriority = entry.State.RequestedPriority;
        Push(entry, rank);
        return true;
    }

    public bool TryDequeue(out SectionRenderState state)
    {
        var hasCritical = HasLive(MeshWorkPriority.Critical);
        var hasForeground = HasLive(MeshWorkPriority.Foreground);
        var hasBackground = HasLive(MeshWorkPriority.Background);
        if (!hasCritical && !hasForeground && !hasBackground)
        {
            state = null!;
            return false;
        }

        var priority = _fairness.Select(hasCritical, hasForeground, hasBackground);
        var lane = _lanes[(int)priority];
        Prune(lane);
        var node = lane.Dequeue();
        state = _entries[node.Position].State;
        _entries.Remove(node.Position);
        return true;
    }

    public bool Remove(Vector3D<int> position) => _entries.Remove(position);

    public void RemoveWhere(Predicate<SectionRenderState> predicate)
    {
        foreach (var (position, entry) in _entries.ToArray())
        {
            if (predicate(entry.State)) _entries.Remove(position);
        }
    }

    /// <summary>Rebuilds heap ranks after the camera crosses a section boundary.</summary>
    public void Reprioritize(Ranker ranker)
    {
        foreach (var lane in _lanes) lane.Clear();
        foreach (var entry in _entries.Values) Push(entry, ranker(entry.State));
    }

    public void Clear()
    {
        _entries.Clear();
        foreach (var lane in _lanes) lane.Clear();
    }

    private bool HasLive(MeshWorkPriority priority)
    {
        var lane = _lanes[(int)priority];
        Prune(lane);
        return lane.Count > 0;
    }

    private void Prune(PriorityQueue<QueueNode, QueueRank> lane)
    {
        while (lane.TryPeek(out var node, out _) &&
               (!_entries.TryGetValue(node.Position, out var entry) || entry.Token != node.Token))
            lane.Dequeue();
    }

    private void Push(Entry entry, in (int Tier, double DistanceSquared, long EnqueuedAt) rank)
    {
        entry.Token = ++_token;
        _lanes[(int)entry.QueuedPriority].Enqueue(
            new QueueNode(entry.State.Position, entry.Token),
            new QueueRank(rank.Tier, rank.DistanceSquared, rank.EnqueuedAt, entry.Token));
    }

    private sealed class Entry(SectionRenderState state)
    {
        public SectionRenderState State = state;
        public MeshWorkPriority QueuedPriority = state.RequestedPriority;
        public long Token;
    }

    private readonly record struct QueueNode(Vector3D<int> Position, long Token);

    private readonly record struct QueueRank(
        int Tier,
        double DistanceSquared,
        long EnqueuedAt,
        long Token) : IComparable<QueueRank>
    {
        public int CompareTo(QueueRank other)
        {
            var tier = Tier.CompareTo(other.Tier);
            if (tier != 0) return tier;
            var distance = DistanceSquared.CompareTo(other.DistanceSquared);
            if (distance != 0) return distance;
            var age = EnqueuedAt.CompareTo(other.EnqueuedAt);
            return age != 0 ? age : Token.CompareTo(other.Token);
        }
    }
}
