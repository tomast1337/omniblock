using BetaSharp.Network.Messages;

namespace BetaSharp.Network.Snapshots;

/// <summary>
///     The sending half of delta-compressed entity replication: one per recipient, holding what that
///     recipient has confirmed and turning each tracking pass into the difference from it.
///     <para>
///         Phase 6 of <c>docs/network-rewrite.md</c> §4.4. Per recipient rather than per entity,
///         because a delta is only meaningful against a state a particular peer is known to hold —
///         two players who joined a minute apart have confirmed different things about the same cow.
///     </para>
/// </summary>
public sealed class PlayerSnapshotStream
{
    private readonly SnapshotBaseline _baseline = new();
    private readonly List<KeyValuePair<int, EntitySnapshotState>> _changes = [];
    private readonly List<EntitySnapshotMessage.EntityDelta> _deltas = [];

    private uint _nextSequence = 1;

    /// <summary>The snapshot the next delta will be measured against, for tests and diagnostics.</summary>
    public uint BaselineSequence => _baseline.Sequence;

    /// <summary>Snapshots sent but not yet confirmed.</summary>
    public int InFlight => _baseline.StagedCount;

    /// <summary>
    ///     Folds in everything the peer confirms receiving.
    ///     <para>
    ///         Zero is a peer announcing it has nothing — a client that reset its own baseline after
    ///         losing track. It resets this end too, which costs one absolute snapshot and is the
    ///         only way back into agreement.
    ///     </para>
    /// </summary>
    public void Acknowledge(uint sequence)
    {
        if (sequence == 0)
        {
            _baseline.Reset();
            return;
        }

        // A sequence this end never sent means the peers disagree about the stream itself, which no
        // delta can repair.
        if (!_baseline.AdvanceTo(sequence))
        {
            _baseline.Reset();
        }
    }

    /// <summary>
    ///     Drops an entity, on destruction or on it leaving this player's view. Without it the
    ///     baseline keeps a state for an entity the client has forgotten, and if the entity comes
    ///     back into range the delta would be measured against a position from before it left.
    /// </summary>
    public void Forget(int entityId) => _baseline.Forget(entityId);

    /// <summary>Forgets everything, so the next snapshot is absolute. Used on a dimension change.</summary>
    public void Reset() => _baseline.Reset();

    /// <summary>
    ///     Encodes one tracking pass.
    ///     <para>
    ///         <paramref name="current" /> holds only the entities this pass had something to say
    ///         about — an entity whose tracking frequency did not come round is absent, and its
    ///         baseline state simply stands. That is the difference between a snapshot protocol that
    ///         costs the world every tick and one that costs the motion.
    ///     </para>
    /// </summary>
    /// <param name="current">
    ///     Entity states, which this call sorts by ID in place. Sorted because the wire encodes the
    ///     gaps between consecutive IDs, and a gap is only small if the IDs ascend.
    /// </param>
    /// <returns>Null when nothing in this pass differs from what the peer already holds.</returns>
    public EntitySnapshotMessage? Build(List<KeyValuePair<int, EntitySnapshotState>> current)
    {
        ArgumentNullException.ThrowIfNull(current);

        // Checked before encoding rather than after. A peer that has stopped acknowledging needs the
        // reset to happen first, so that this pass is encoded absolute against the empty baseline
        // instead of as deltas that are then discovered to have nowhere to be staged.
        if (_baseline.StagedCount >= SnapshotBaseline.MaxStagedSnapshots)
        {
            _baseline.Reset();
        }

        current.Sort(static (left, right) => left.Key.CompareTo(right.Key));

        _deltas.Clear();
        _changes.Clear();

        foreach ((int entityId, EntitySnapshotState state) in current)
        {
            EntitySnapshotMessage.EntityDelta? delta =
                EntitySnapshotMessage.Encode(entityId, state, _baseline);

            if (delta is null)
            {
                continue;
            }

            _deltas.Add(delta.Value);
            _changes.Add(new KeyValuePair<int, EntitySnapshotState>(entityId, state));
        }

        if (_deltas.Count == 0)
        {
            return null;
        }

        uint sequence = _nextSequence++;

        // Zero is reserved for "no baseline", so a wrap has to skip it rather than emit a snapshot
        // the peer would read as a resynchronisation.
        if (_nextSequence == 0)
        {
            _nextSequence = 1;
        }

        _baseline.Stage(sequence, _changes);

        return new EntitySnapshotMessage
        {
            Sequence = sequence,
            Baseline = _baseline.Sequence,
            Deltas = [.. _deltas],
        };
    }
}
