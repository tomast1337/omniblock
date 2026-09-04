using OmniBlock.Network.Messages;

namespace OmniBlock.Network.Snapshots;

/// <summary>
///     The receiving half of delta-compressed entity replication: reconstructs absolute positions
///     from a snapshot and the baseline it names, and tracks what to acknowledge.
///     <para>
///         The mirror of <see cref="PlayerSnapshotStream" />, sharing its
///         <see cref="SnapshotBaseline" /> so that "the state at sequence N" means the same thing
///         at both ends.
///     </para>
/// </summary>
public sealed class ClientSnapshotStream
{
    private readonly List<KeyValuePair<int, EntitySnapshotState>> _applied = [];
    private readonly SnapshotBaseline _baseline = new();

    /// <summary>
    ///     The newest snapshot successfully applied, and the value to acknowledge. Zero asks the
    ///     server to resynchronise: it is the state before the first snapshot, and also what this
    ///     stream falls back to when it can no longer follow.
    /// </summary>
    public uint AppliedSequence { get; private set; }

    /// <summary>Snapshots applied but not yet named as a baseline, for tests and diagnostics.</summary>
    public int Staged => _baseline.StagedCount;

    /// <summary>Snapshots dropped because their baseline could not be reconstructed.</summary>
    public long DroppedSnapshots { get; private set; }

    /// <summary>
    ///     Reconstructs the absolute state of every entity a snapshot mentions.
    ///     <para>
    ///         A snapshot whose baseline this stream cannot produce is dropped whole rather than
    ///         applied partially, and <see cref="AppliedSequence" /> is left where it was, so the
    ///         next acknowledgement still names a state the server can encode against. That is the
    ///         recovery path, and it costs one redundant delta rather than a resynchronisation.
    ///     </para>
    /// </summary>
    /// <returns>
    ///     The entities to move, or an empty list when the snapshot was dropped. The list is reused
    ///     between calls and is only valid until the next one.
    /// </returns>
    public IReadOnlyList<KeyValuePair<int, EntitySnapshotState>> Apply(EntitySnapshotMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        _applied.Clear();

        if (message.Baseline == 0)
        {
            // The server is speaking from nothing, so anything held here is from before that
            // decision and would only mask a record the snapshot did not carry.
            _baseline.Reset();
        }
        else if (!_baseline.AdvanceTo(message.Baseline))
        {
            DroppedSnapshots++;
            return _applied;
        }

        foreach (var delta in message.Deltas)
        {
            _applied.Add(new KeyValuePair<int, EntitySnapshotState>(
                delta.EntityId, EntitySnapshotMessage.Decode(delta, _baseline)));
        }

        if (!_baseline.Stage(message.Sequence, _applied))
        {
            // Out of room to stage means the server has stopped advancing its baseline — its
            // acknowledgements are not arriving. Announcing nothing is what asks it to start over.
            _baseline.Reset();
            AppliedSequence = 0;
            _applied.Clear();
            DroppedSnapshots++;
            return _applied;
        }

        AppliedSequence = message.Sequence;
        return _applied;
    }

    /// <summary>Drops an entity, on the destroy packet. See <see cref="PlayerSnapshotStream.Forget" />.</summary>
    public void Forget(int entityId) => _baseline.Forget(entityId);

    /// <summary>
    ///     Forgets everything and asks for a resynchronisation. Used on a dimension change and on
    ///     disconnect, where the entity IDs on the other side of it mean something different.
    /// </summary>
    public void Reset()
    {
        _baseline.Reset();
        AppliedSequence = 0;
    }
}
