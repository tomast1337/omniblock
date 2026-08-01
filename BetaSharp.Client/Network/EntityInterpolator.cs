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
    ///     Hard bound on the per-entity delay, for an entity whose updates have stopped or gone
    ///     pathologically slow. Not a quality knob — nothing normal reaches it.
    ///     <para>
    ///         This was 600 ms, chosen as "far enough in the past to be objectionable", and it was
    ///         the wrong shape rather than the wrong size. A single ceiling can only ever bind on
    ///         entities that update slowly, which are precisely the ones that need the most delay:
    ///         dropped items update every 20 ticks, want ~2000 ms, got 600, and starved by
    ///         construction. Measured at 29 of 985 entities frozen with the rest interpolating.
    ///     </para>
    ///     <para>
    ///         The reasoning that fixed the delay applies to the ceiling too. An entity's update
    ///         rate is the server's own statement of how much fidelity it deserves —
    ///         <c>EntityTrackerEntry</c> assigns 2 ticks to players, 3 to mobs, 10 to projectiles,
    ///         20 to dropped items — so an entity the server sends once a second is already known
    ///         only to one-second resolution, and rendering it two seconds back costs nothing that
    ///         was ever visible. Scaling with the interval reads that signal instead of overriding
    ///         it.
    ///     </para>
    /// </summary>
    public const long MaxDelayMs = 3000;

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
    ///         <b>Called once per tick, immediately before the entities tick</b> — not per frame,
    ///         and not after. This sets each entity's target; <c>TickMovement</c> moves it there
    ///         during the tick, between <c>Entity.Tick</c> assigning <c>PrevX = X</c> and
    ///         <c>EntityLiving.Tick</c> reading <c>X - PrevX</c>. Both the renderer's interpolation
    ///         interval and the walk animation are derived from that window, so a position written
    ///         outside it satisfies neither.
    ///     </para>
    ///     <para>
    ///         Render time still advances off the synchronised clock, so the property that matters —
    ///         that a stalled stream does not stall motion — is unaffected by sampling at tick rate.
    ///         The renderer's existing <c>partialTicks</c> lerp supplies the sub-tick smoothing.
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

            // The engine's own in-tick movement hook, with one step so the entity lands exactly on
            // the sample instead of a fraction of the way toward it.
            //
            // Setting the position directly does not work, in either order. Entity.Tick assigns
            // PrevX = X at its start and EntityLiving.Tick reads X - PrevX after TickMovement, so a
            // write before the tick is erased and a write after it comes too late — either way the
            // delta is zero, WalkProgress never advances and the legs never move. Routing through
            // NewPos* puts the movement where every consumer expects it: between those two points,
            // which is exactly where the scheme this replaces put it.
            entity.SetPositionAndAnglesAvoidEntities(sample.X, sample.Y, sample.Z, sample.Yaw, sample.Pitch, 1);
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
    ///         network jitter is still covered when it exceeds the update spacing, and
    ///         <see cref="MaxDelayMs" /> is a bound against an entity that stopped updating rather
    ///         than a quality setting — see its own remarks for why a tighter ceiling starved the
    ///         entities that needed it most.
    ///     </para>
    /// </summary>
    internal long DelayForMs(SnapshotBuffer buffer)
    {
        long interval = buffer.MedianIntervalMs;

        // Below two snapshots there is no interval to measure and nothing to interpolate between,
        // so the network floor is as good an answer as exists.
        if (interval <= 0)
        {
            return DelayMs;
        }

        return Math.Clamp(interval * 2, DelayMs, MaxDelayMs);
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
