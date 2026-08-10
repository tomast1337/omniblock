namespace OmniBlock.Network.Snapshots;

/// <summary>
///     The state a delta-compressed snapshot is measured against, and the bookkeeping that keeps
///     both peers pointing at the same one.
///     <para>
///         The server encodes each snapshot as the difference from a specific earlier one, so both
///         ends must be able to name and reconstruct
///         that earlier state. This class is that mechanism, and it is deliberately identical on both
///         sides: the sender advances its baseline when an acknowledgement arrives, the receiver
///         advances its own to whatever baseline the incoming snapshot names, and if the two ever
///         disagreed about what a sequence number means the decode would silently produce garbage
///         positions rather than fail.
///     </para>
///     <para>
///         <b>Changes are staged rather than states copied.</b> The obvious implementation keeps a
///         ring of whole snapshots — a dictionary of every tracked entity per sequence, which at a
///         few hundred entities and a few dozen sequences is hundreds of thousands of entries per
///         player, rebuilt every tick. Staging only what changed makes the cost proportional to the
///         motion instead of to the world, which is the same reason the snapshot itself is a delta.
///     </para>
/// </summary>
public sealed class SnapshotBaseline
{
    /// <summary>
    ///     Unacknowledged snapshots kept before the stream gives up and resynchronises from nothing.
    ///     <para>
    ///         Three seconds at 20 TPS. Past that, a peer is not acknowledging — it has gone away, or
    ///         it is dropping snapshots faster than it applies them — and continuing to stage changes
    ///         against a baseline it will never confirm is an unbounded queue in service of a delta
    ///         nobody can decode. Resetting costs one absolute snapshot.
    ///     </para>
    /// </summary>
    public const int MaxStagedSnapshots = 64;

    private readonly Dictionary<int, EntitySnapshotState> _states = [];
    private readonly Queue<Staged> _staged = new();

    /// <summary>
    ///     The snapshot this baseline reflects. Zero means nothing has been confirmed and the next
    ///     snapshot must be absolute — which is also the state after <see cref="Reset" />.
    /// </summary>
    public uint Sequence { get; private set; }

    /// <summary>Snapshots sent or received but not yet folded in, for tests and diagnostics.</summary>
    public int StagedCount => _staged.Count;

    /// <summary>Entities the baseline holds a state for.</summary>
    public int Count => _states.Count;

    public bool TryGet(int entityId, out EntitySnapshotState state) => _states.TryGetValue(entityId, out state);

    /// <summary>
    ///     Records what a snapshot changed, without moving the baseline. The changes only take effect
    ///     when <see cref="AdvanceTo" /> reaches this sequence — for the sender that is when the peer
    ///     acknowledges it, for the receiver when a later snapshot names it as its baseline.
    /// </summary>
    /// <returns>False when the staging queue is full, meaning the caller should <see cref="Reset" />.</returns>
    public bool Stage(uint sequence, IReadOnlyList<KeyValuePair<int, EntitySnapshotState>> changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        if (_staged.Count >= MaxStagedSnapshots)
        {
            return false;
        }

        _staged.Enqueue(new Staged(sequence, [.. changes]));
        return true;
    }

    /// <summary>
    ///     Folds in every staged snapshot up to and including <paramref name="sequence" />, leaving
    ///     the baseline holding exactly the state that snapshot described.
    ///     <para>
    ///         A sequence already passed is not an error and does nothing: acknowledgements can
    ///         arrive out of order on a link that reorders, and the older one carries no information
    ///         the newer did not.
    ///     </para>
    /// </summary>
    /// <returns>
    ///     False when <paramref name="sequence" /> is ahead of everything staged, which means the
    ///     peers disagree about what has been sent and the stream must resynchronise.
    /// </returns>
    public bool AdvanceTo(uint sequence)
    {
        if (sequence == Sequence)
        {
            return true;
        }

        if (Before(sequence, Sequence))
        {
            return true;
        }

        while (_staged.Count > 0 && !Before(sequence, _staged.Peek().Sequence))
        {
            Staged staged = _staged.Dequeue();

            foreach ((int entityId, EntitySnapshotState state) in staged.Changes)
            {
                _states[entityId] = state;
            }

            Sequence = staged.Sequence;
        }

        return Sequence == sequence;
    }

    /// <summary>
    ///     Drops an entity, from the baseline and from anything staged. Both are needed: a staged
    ///     change would otherwise reintroduce it the next time the baseline advanced, and the peers
    ///     would then disagree about whether it is present.
    /// </summary>
    public void Forget(int entityId)
    {
        _states.Remove(entityId);

        if (_staged.Count == 0)
        {
            return;
        }

        Staged[] retained = [.. _staged.Select(s => new Staged(
            s.Sequence, [.. s.Changes.Where(c => c.Key != entityId)]))];

        _staged.Clear();
        foreach (Staged staged in retained)
        {
            _staged.Enqueue(staged);
        }
    }

    /// <summary>
    ///     Forgets everything, so the next snapshot is absolute. Used on a dimension change, on a
    ///     stream that has run out of staging room, and whenever the two peers are found to disagree.
    /// </summary>
    public void Reset()
    {
        _states.Clear();
        _staged.Clear();
        Sequence = 0;
    }

    /// <summary>
    ///     Sequence comparison that survives the counter wrapping, per RFC 1982. A session would have
    ///     to run for three years at 20 snapshots a second to wrap a <see cref="uint" />, so this is
    ///     not load-bearing — but a plain <c>&lt;</c> would turn that into an unrecoverable stream
    ///     rather than one hiccup, and the correct comparison is no harder to write.
    /// </summary>
    private static bool Before(uint left, uint right) => (int)(left - right) < 0;

    private readonly record struct Staged(uint Sequence, KeyValuePair<int, EntitySnapshotState>[] Changes);
}
