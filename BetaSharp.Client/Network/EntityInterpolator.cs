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
    ///     Ceiling on the per-entity delay. Dropped items update once a second, and honouring that
    ///     in full would render them two seconds in the past. Beyond this they starve and hold their
    ///     last known position instead, which is what the legacy scheme effectively did for them
    ///     anyway and is unobjectionable for entities that barely move.
    /// </summary>
    public const long MaxDelayMs = 600;

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

    /// <summary>
    ///     Range of per-entity delays applied this frame. A range rather than one number because the
    ///     delay tracks each entity's own update rate — seeing players at 200 ms and items at the
    ///     600 ms ceiling in the same frame is correct, not a fault.
    /// </summary>
    public long MinAppliedDelayMs { get; private set; }

    public long MaxAppliedDelayMs { get; private set; }

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
    ///     Samples every tracked entity and writes the result.
    ///     <para>
    ///         <b>Called once per tick, immediately after the entities tick</b> — not per frame.
    ///         Sampling per frame and pinning <c>Prev*</c> to match would make the rendered position
    ///         independent of <c>partialTicks</c>, which sounds right and is not: it collapses the
    ///         interval the renderer lerps across, so motion steps at snapshot rate, and it zeroes
    ///         the <c>X - PrevX</c> delta that <c>EntityLiving.Tick</c> turns into limb animation.
    ///     </para>
    ///     <para>
    ///         Sampling per tick instead leaves the existing two-stage arrangement intact: this sets
    ///         where the entity is at this tick, and the renderer glides between consecutive ticks
    ///         exactly as it always has. Render time still advances off the synchronised clock, so
    ///         the property that matters — that a stalled stream does not stall motion — is
    ///         unaffected.
    ///     </para>
    /// </summary>
    /// <param name="serverTimeMs">The client's estimate of the server's clock right now.</param>
    public void Apply(World world, long serverTimeMs)
    {
        InterpolatedCount = 0;
        ExtrapolatedCount = 0;
        FrozenCount = 0;
        ClampedCount = 0;
        MinAppliedDelayMs = 0;
        MaxAppliedDelayMs = 0;

        if (!Active || _buffers.Count == 0)
        {
            return;
        }

        foreach (Entity entity in world.Entities.Entities)
        {
            if (!_buffers.TryGetValue(entity.ID, out SnapshotBuffer? buffer))
            {
                continue;
            }

            long delay = DelayForMs(buffer);
            MinAppliedDelayMs = MinAppliedDelayMs == 0 ? delay : Math.Min(MinAppliedDelayMs, delay);
            MaxAppliedDelayMs = Math.Max(MaxAppliedDelayMs, delay);

            SampleKind kind = buffer.Sample(serverTimeMs - delay, out Snapshot sample);

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

            // Only the current position. Prev*/LastTick* are deliberately left alone: EntityManager
            // captured them before the tick and Entity.Tick set PrevX = X, so they already hold the
            // previous tick's sample. That gives the renderer a real interval to lerp across and
            // leaves X - PrevX equal to genuine per-tick movement.
            //
            // Writing Prev* here instead — as SetPositionAndAngles does — zeroes that delta, and
            // EntityLiving.Tick derives WalkProgress from it. The visible result is entities that
            // slide without animating, with only the head still turning.
            entity.SetPosition(sample.X, sample.Y, sample.Z);

            // Entity.SetRotation is protected internal and out of reach from this assembly; these
            // are the same two assignments it makes, wrap included.
            entity.Yaw = sample.Yaw % 360.0F;
            entity.Pitch = sample.Pitch % 360.0F;
        }

        PruneStale(serverTimeMs);
    }

    /// <summary>
    ///     How far behind the server clock this particular entity is rendered.
    ///     <para>
    ///         Per-entity, because the update rate is per-entity: <c>EntityTrackerEntry</c> sends
    ///         one update every <c>trackingFrequency</c> ticks and that ranges from 2 (players) to 20
    ///         (dropped items). A single global delay cannot serve both — sized for players it leaves
    ///         everything slower permanently starved, and sized for items it renders players half a
    ///         second in the past.
    ///     </para>
    ///     <para>
    ///         Twice the observed interval, because the newest snapshot is on average half an
    ///         interval old and can be a full one; anything less than one interval of delay leaves
    ///         render time past the newest snapshot much of the time, which is exactly the
    ///         Interpolated-zero case this replaced. <see cref="DelayMs" /> remains the floor, so
    ///         network jitter is still covered when it exceeds the update spacing.
    ///     </para>
    /// </summary>
    private long DelayForMs(SnapshotBuffer buffer)
    {
        long interval = buffer.MedianIntervalMs;

        // Below two snapshots there is no interval to measure and nothing to interpolate between,
        // so the network floor is as good an answer as exists.
        if (interval <= 0)
        {
            return DelayMs;
        }

        return Math.Clamp(Math.Max(DelayMs, interval * 2), DelayMs, MaxDelayMs);
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
