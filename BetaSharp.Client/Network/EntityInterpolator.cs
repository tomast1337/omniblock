using BetaSharp.Entities;
using BetaSharp.Worlds.Core;

namespace BetaSharp.Client.Network;

/// <summary>
///     Drives remote entities from buffered server snapshots sampled at render time, rather than
///     moving them a fraction of the way toward the last-received target each tick.
///     <para>
///         Phase 4 of <c>docs/time-sync-and-interpolation.md</c>. See <see cref="SnapshotBuffer" />
///         for why sampling against the clock instead of against packet arrivals is the fix.
///     </para>
///     <para>
///         <b>Applied at render time, not tick time.</b> Entities are sampled once per frame and
///         written into every field the renderers interpolate between — <c>PrevX</c>/<c>X</c> and
///         <c>LastTickX</c>/<c>X</c> both appear in renderer position formulas, so all three are set
///         to the sampled value. That makes the rendered position independent of
///         <c>partialTicks</c>, which is correct here: the sample already accounts for where inside
///         the frame we are.
///     </para>
/// </summary>
public sealed class EntityInterpolator
{
    /// <summary>
    ///     Render this far behind the server's clock. The buffer needs snapshots on both sides of
    ///     render time to interpolate, so the delay is what absorbs jitter and stalls.
    ///     <para>
    ///         Held at the floor from §3.4's <c>clamp(2 * tickInterval + 2 * jitter, 100, 500)</c>.
    ///         Two tick intervals is the minimum that keeps two snapshots bracketing render time at
    ///         20 TPS; the jitter term is zero on the only connection measured so far. Phase 5 makes
    ///         this adaptive, which is where the term starts to matter.
    ///     </para>
    /// </summary>
    public const long DefaultDelayMs = 100;

    /// <summary>
    ///     Buffers not sampled for this long are dropped. A backstop only — the normal removal path
    ///     is <see cref="Forget" /> from the entity-destroy packet — for entities that leave
    ///     tracking range without one, which would otherwise leak a buffer per entity per session.
    /// </summary>
    private const long StaleBufferMs = 10_000;

    /// <summary>
    ///     The active connection's interpolator, for the debug overlay's A/B toggle.
    ///     <para>
    ///         Static because the overlay is a global view with no route to the connection, the same
    ///         reason <c>ClientMetrics</c> is a global registry. Everything the overlay only reads
    ///         goes through metrics; this exists solely because the toggle needs to write.
    ///     </para>
    /// </summary>
    public static EntityInterpolator? Current { get; set; }

    private readonly Dictionary<int, SnapshotBuffer> _buffers = [];
    private readonly Dictionary<int, long> _lastSeen = [];
    private readonly List<int> _pendingRemoval = [];

    /// <summary>
    ///     Whether sampled positions are actually written to entities. Off falls back to the legacy
    ///     move-toward-target behaviour, so the two can be compared live on one connection rather
    ///     than across two sessions with different network conditions.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Whether a shared timeline exists to sample against — a synchronised clock and a server
    ///     that stamps its batches. Distinct from <see cref="Enabled" />, which is the user's A/B
    ///     switch, because the two fail differently: this one going false must hand control back to
    ///     the legacy path, and conflating them would let a preference silently disable the
    ///     fallback.
    /// </summary>
    public bool Available { get; set; }

    /// <summary>True when snapshots are actually driving entities.</summary>
    public bool Active => Enabled && Available;

    public long DelayMs { get; set; } = DefaultDelayMs;

    // Per-frame counts, for the overlay. Rising Frozen is the signal that the delay is undersized.
    public int InterpolatedCount { get; private set; }
    public int ExtrapolatedCount { get; private set; }
    public int FrozenCount { get; private set; }
    public int ClampedCount { get; private set; }

    /// <summary>Entities currently carrying a snapshot buffer.</summary>
    public int TrackedCount => _buffers.Count;

    /// <summary>
    ///     Records where the server says an entity was, as of the batch currently being read.
    ///     <paramref name="serverTimeMs" /> comes from the most recent <c>TickStampS2CPacket</c>;
    ///     a zero means the server does not stamp and the snapshot is dropped, since a snapshot
    ///     without a timeline is exactly the guess this replaces.
    /// </summary>
    public void Record(int entityId, long serverTimeMs, double x, double y, double z, float yaw, float pitch)
    {
        if (serverTimeMs == 0)
        {
            return;
        }

        if (!_buffers.TryGetValue(entityId, out SnapshotBuffer? buffer))
        {
            buffer = new SnapshotBuffer();
            _buffers[entityId] = buffer;
        }

        buffer.Push(new Snapshot(serverTimeMs, x, y, z, yaw, pitch));
        _lastSeen[entityId] = serverTimeMs;
    }

    /// <summary>Whether an entity is being driven from snapshots, so callers can skip the legacy retarget.</summary>
    public bool IsInterpolating(int entityId) => Active && _buffers.ContainsKey(entityId);

    public void Forget(int entityId)
    {
        _buffers.Remove(entityId);
        _lastSeen.Remove(entityId);
    }

    /// <summary>
    ///     Drops every buffer. Used on a clock step and on world change: buffered stamps are on the
    ///     old timeline, and interpolating across that discontinuity would glide the entity through
    ///     the correction rather than cut to it.
    /// </summary>
    public void Clear()
    {
        _buffers.Clear();
        _lastSeen.Clear();
    }

    /// <summary>
    ///     Samples every tracked entity and writes the result. Call once per frame, before entities
    ///     are rendered.
    /// </summary>
    /// <param name="serverTimeMs">The client's estimate of the server's clock right now.</param>
    public void Apply(World world, long serverTimeMs)
    {
        InterpolatedCount = 0;
        ExtrapolatedCount = 0;
        FrozenCount = 0;
        ClampedCount = 0;

        if (!Active || _buffers.Count == 0)
        {
            return;
        }

        long renderTimeMs = serverTimeMs - DelayMs;

        foreach (Entity entity in world.Entities.Entities)
        {
            if (!_buffers.TryGetValue(entity.ID, out SnapshotBuffer? buffer))
            {
                continue;
            }

            SampleKind kind = buffer.Sample(renderTimeMs, out Snapshot sample);

            switch (kind)
            {
                case SampleKind.Empty:
                    continue;
                case SampleKind.Interpolated:
                    InterpolatedCount++;
                    break;
                case SampleKind.Extrapolated:
                    ExtrapolatedCount++;
                    break;
                case SampleKind.Frozen:
                    FrozenCount++;
                    break;
                case SampleKind.Clamped:
                    ClampedCount++;
                    break;
            }

            // Sets X/Y/Z with their Prev counterparts and refreshes the bounding box. LastTick*
            // is not covered by it and is read by EntityRenderer and the camera offsets, so it is
            // pinned separately; leaving it stale would reintroduce a per-frame wobble through the
            // other interpolation formula.
            entity.SetPositionAndAngles(sample.X, sample.Y, sample.Z, sample.Yaw, sample.Pitch);
            entity.LastTickX = sample.X;
            entity.LastTickY = sample.Y;
            entity.LastTickZ = sample.Z;
        }

        PruneStale(serverTimeMs);
    }

    private void PruneStale(long serverTimeMs)
    {
        _pendingRemoval.Clear();

        foreach ((int id, long seen) in _lastSeen)
        {
            if (serverTimeMs - seen > StaleBufferMs)
            {
                _pendingRemoval.Add(id);
            }
        }

        foreach (int id in _pendingRemoval)
        {
            Forget(id);
        }
    }
}
