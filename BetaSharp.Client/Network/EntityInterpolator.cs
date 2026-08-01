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
    ///         The floor from §3.4's <c>clamp(2 * tickInterval + 2 * jitter, …)</c>. Two tick
    ///         intervals is the minimum that keeps two snapshots bracketing render time at 20 TPS.
    ///         The jitter term is added on top, from <see cref="NetworkJitterMs" />.
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
    ///     Most the delay may grow per tick, when an entity's updates slow down and it needs a
    ///     deeper buffer.
    ///     <para>
    ///         <b>This is a speed limit on time itself, and that is what sets the number.</b> Render
    ///         time is <c>serverTime - delay</c>, so while the delay grows by <i>d</i> per 50 ms
    ///         tick, render time advances by <c>50 - d</c> and the entity plays at
    ///         <c>(50 - d) / 50</c> speed. At 20 ms it moves at 60% — slower, but always forward.
    ///         At 50 it would stop dead, and past 50 it would visibly walk backwards, which is why
    ///         the value has to stay well under the tick interval rather than being tuned for how
    ///         quickly the buffer refills.
    ///     </para>
    ///     <para>
    ///         Adjusting at all is the alternative to snapping. Recomputing the delay from the
    ///         observed interval every tick makes it jump the moment the median moves — an entity
    ///         that stalls goes from 300 ms to 800 ms of delay in one tick and teleports half a
    ///         second into its own past. Ramping converts that teleport into a brief slow-motion,
    ///         which is the §3.5 requirement not to snap on recovery.
    ///     </para>
    /// </summary>
    public const long DelayRaisePerTickMs = 20;

    /// <summary>
    ///     Most the delay may shrink per tick, when an entity's updates speed up again.
    ///     <para>
    ///         Slower than the rise, per §3.4's asymmetry. Shrinking early re-enters starvation and
    ///         oscillates, and there is no urgency: too much delay costs a little latency, too
    ///         little costs a freeze. The same speed-limit arithmetic applies with the sign flipped
    ///         — at 5 ms the entity plays at 110%, fast enough to converge and slow enough not to
    ///         read as a skip.
    ///     </para>
    /// </summary>
    public const long DelayLowerPerTickMs = 5;

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

    /// <summary>Per-entity delay state: what is in force, what starvation has bought it, and whether
    ///     it is starving right now.</summary>
    private readonly Dictionary<int, DelayState> _delays = [];

    /// <summary>Server time at the previous <see cref="Apply" />, so a starving entity's render time
    ///     can be pinned against however much the clock actually advanced.</summary>
    private long _lastServerTimeMs;

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

    /// <summary>
    ///     Mean absolute deviation of round-trip time, from <see cref="ServerClock.JitterMs" />.
    ///     Twice this is added to every entity's delay, per §3.4.
    ///     <para>
    ///         Connection-wide rather than per-entity, and that is the point: the interval term
    ///         covers how often the <em>server</em> chooses to speak about this entity, and this
    ///         covers how unevenly the <em>network</em> delivers whatever it says. They are
    ///         independent — a player tracked every 100 ms on a link with 80 ms of jitter needs both
    ///         terms, and either alone leaves render time past the newest snapshot a good fraction of
    ///         the time.
    ///     </para>
    ///     <para>
    ///         Twice rather than once because the margin has to cover a deviation in the direction
    ///         that hurts, and the mean absolute deviation is an average over both. It reads 4 ms on
    ///         loopback UDP, so it contributes nothing there by design; it is sized for a real link,
    ///         where it is the difference between absorbing jitter and merely measuring it.
    ///     </para>
    /// </summary>
    public long NetworkJitterMs { get; set; }

    /// <summary>
    ///     Times an entity has run out of buffered future since the connection opened — counted per
    ///     entry into starvation, not per frame spent in it.
    ///     <para>
    ///         §3.5's metric: this is what says the buffer is undersized for this connection.
    ///         <see cref="FrozenCount" /> cannot answer that, because it is instantaneous and a
    ///         standing handful of genuinely idle entities looks identical to a stream that keeps
    ///         breaking down.
    ///     </para>
    /// </summary>
    public long StarvationEvents { get; private set; }

    // Per-frame counts, for the overlay. Rising Frozen is the signal that the delay is undersized.
    public int InterpolatedCount { get; private set; }
    public int ExtrapolatedCount { get; private set; }
    public int FrozenCount { get; private set; }
    public int ClampedCount { get; private set; }

    /// <summary>
    ///     Entities whose delay was mid-ramp this tick. Steady traffic converges and leaves this at
    ///     zero, so a number that stays high means the observed interval is unstable rather than
    ///     that any single entity is in trouble.
    /// </summary>
    public int AdjustingCount { get; private set; }

    /// <summary>
    ///     Range of per-entity delays applied this frame. A range rather than one number because the
    ///     delay tracks each entity's own update rate — seeing players at 200 ms and dropped items
    ///     at 2000 ms in the same frame is correct, not a fault.
    /// </summary>
    public long MinAppliedDelayMs { get; private set; }

    public long MaxAppliedDelayMs { get; private set; }

    /// <summary>Entities currently carrying a snapshot buffer.</summary>
    public int TrackedCount => _buffers.Count;

    /// <summary>
    ///     Records where the server says an entity was, as of the batch currently being read.
    ///     <paramref name="serverTimeMs" /> comes from the most recent <c>TickStampMessage</c>;
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
        _delays.Remove(entityId);
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

        // The ramp has to go with them. A retained delay would be eased away from on the new
        // timeline, which is the snap this avoids, applied to the one discontinuity that is
        // supposed to be a cut. Starvation credit goes the same way: it was bought against a
        // timeline that no longer exists.
        _delays.Clear();

        // Not zero: zero means "no previous pass" and suppresses the first advance. After a step the
        // next pass genuinely has no comparable previous time, which is the same thing.
        _lastServerTimeMs = 0;
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
        AdjustingCount = 0;
        MinAppliedDelayMs = 0;
        MaxAppliedDelayMs = 0;

        if (!Active || _buffers.Count == 0)
        {
            _lastServerTimeMs = serverTimeMs;
            return;
        }

        // How far the shared timeline moved since the last pass. A starving entity's delay grows by
        // exactly this, which pins its render time; see NoteSample.
        long advanceMs = _lastServerTimeMs == 0 ? 0 : Math.Max(0, serverTimeMs - _lastServerTimeMs);
        _lastServerTimeMs = serverTimeMs;

        foreach (Entity entity in world.Entities.Entities)
        {
            if (!_buffers.TryGetValue(entity.ID, out SnapshotBuffer? buffer))
            {
                continue;
            }

            SampleKind kind = Advance(entity.ID, buffer, serverTimeMs, advanceMs, out Snapshot sample);

            if (kind == SampleKind.Empty)
            {
                continue;
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
    ///     One entity's whole per-tick step: settle its delay, sample it, and book the outcome
    ///     against its starvation credit.
    ///     <para>
    ///         Split out from <see cref="Apply" /> because it is the unit the adaptation logic lives
    ///         in and the only part of it that needs no <see cref="World" />. Driving it directly is
    ///         what lets a stall be tested as a sequence of ticks rather than as a live connection.
    ///     </para>
    /// </summary>
    /// <param name="advanceMs">
    ///     Server-clock time since the previous pass — 50 ms in normal operation. Passed in rather
    ///     than assumed, so a pass that took two ticks credits a starving entity for both.
    /// </param>
    internal SampleKind Advance(
        int entityId, SnapshotBuffer buffer, long serverTimeMs, long advanceMs, out Snapshot sample)
    {
        long delay = SmoothedDelayFor(entityId, buffer);
        MinAppliedDelayMs = MinAppliedDelayMs == 0 ? delay : Math.Min(MinAppliedDelayMs, delay);
        MaxAppliedDelayMs = Math.Max(MaxAppliedDelayMs, delay);

        SampleKind kind = buffer.Sample(serverTimeMs - delay, out sample);
        NoteSample(entityId, kind, buffer, advanceMs);

        switch (kind)
        {
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

        return kind;
    }

    /// <summary>The delay currently in force for one entity, for tests and diagnostics.</summary>
    internal long AppliedDelayFor(int entityId) =>
        _delays.TryGetValue(entityId, out DelayState state) ? state.AppliedMs : 0;

    /// <summary>
    ///     The delay in force for one entity: <see cref="DelayForMs" />'s answer plus whatever
    ///     starvation has bought it, approached at a bounded rate rather than adopted outright.
    ///     <para>
    ///         The target moves whenever the observed median interval does, and that happens
    ///         constantly — an entity stops moving and its updates stop, a burst arrives after a
    ///         stall, a mob crosses into a different tracking frequency. Each of those would
    ///         otherwise reposition the entity by the whole difference in a single tick.
    ///     </para>
    ///     <para>
    ///         The first sighting is adopted whole. There is nothing to ease away from, and ramping
    ///         from the floor to a dropped item's two seconds would spend the ramp in slow motion
    ///         for no benefit.
    ///     </para>
    /// </summary>
    internal long SmoothedDelayFor(int entityId, SnapshotBuffer buffer)
    {
        if (!_delays.TryGetValue(entityId, out DelayState state))
        {
            long first = DelayForMs(buffer);
            _delays[entityId] = new DelayState { AppliedMs = first };
            return first;
        }

        long target = Math.Clamp(DelayForMs(buffer) + state.MarginMs, DelayMs, MaxDelayMs);

        if (target != state.AppliedMs)
        {
            state.AppliedMs = target > state.AppliedMs
                // A starving entity is exempt from the raise limit. That limit exists to stop
                // rendered time from slowing enough to be visible, and a starving entity's rendered
                // time has already stopped — there is no motion left to slow. Tracking the target
                // outright is exactly what makes playback resume from where it froze.
                ? (state.Starving ? target : Math.Min(target, state.AppliedMs + DelayRaisePerTickMs))
                : Math.Max(target, state.AppliedMs - DelayLowerPerTickMs);

            AdjustingCount++;
        }

        // Repay the credit starvation bought, but only while not starving, and at the same rate the
        // applied delay is allowed to fall — repaying faster than that would leave the target below
        // the applied value and turn the repayment into the ramp's own business rather than a
        // decision made here.
        if (!state.Starving && state.MarginMs > 0)
        {
            state.MarginMs = Math.Max(0, state.MarginMs - DelayLowerPerTickMs);
        }

        _delays[entityId] = state;
        return state.AppliedMs;
    }

    /// <summary>
    ///     Books the outcome of one sample against the entity's starvation credit.
    ///     <para>
    ///         <b>While an entity is frozen its delay grows by exactly the clock's own advance</b>,
    ///         which pins render time where playback stopped. That is §3.5's "on recovery, do not
    ///         snap", arrived at from the other end: the delay <em>is</em> the render-time offset, so
    ///         holding render time still and growing the delay at the tick rate are the same
    ///         operation, and expressing it as the latter means the existing asymmetric ramp handles
    ///         the way back out for free.
    ///     </para>
    ///     <para>
    ///         Without it, recovery skips. Render time keeps advancing through the stall while the
    ///         entity holds at the newest snapshot it had; when the backlog lands, render time is
    ///         already a stall's worth into the new data and the entity jumps straight to it,
    ///         discarding the motion in between. Pinning means the buffer refills <em>ahead</em> of
    ///         render time and playback continues from the frozen instant, at 110% speed until the
    ///         credit is repaid.
    ///     </para>
    ///     <para>
    ///         The credit is bounded by <see cref="MaxDelayMs" /> through the clamp in
    ///         <see cref="SmoothedDelayFor" />, so a stall longer than that does eventually skip
    ///         rather than accumulate render time debt without limit.
    ///     </para>
    /// </summary>
    private void NoteSample(int entityId, SampleKind kind, SnapshotBuffer buffer, long advanceMs)
    {
        if (!_delays.TryGetValue(entityId, out DelayState state))
        {
            return;
        }

        // Frozen with fewer than two snapshots is an entity that has only just come into range, not
        // one that ran out of future: there is no history to have run past. Crediting it would hand
        // every newly-tracked entity a margin for the crime of being new.
        bool starving = kind == SampleKind.Frozen && buffer.Count >= 2;

        if (starving)
        {
            if (!state.Starving)
            {
                StarvationEvents++;
            }

            state.MarginMs += advanceMs;
        }

        state.Starving = starving;
        _delays[entityId] = state;
    }

    /// <summary>
    ///     How far behind the server clock this particular entity is rendered, before starvation
    ///     credit and before the ramp.
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
    ///         Interpolated-zero case this replaced. <see cref="DelayMs" /> remains the floor and
    ///         <see cref="MaxDelayMs" /> is a bound against an entity that stopped updating rather
    ///         than a quality setting — see its own remarks for why a tighter ceiling starved the
    ///         entities that needed it most.
    ///     </para>
    /// </summary>
    internal long DelayForMs(SnapshotBuffer buffer)
    {
        long interval = buffer.MedianIntervalMs;

        // §3.4's jitter term. Independent of the update rate, so it is added rather than folded in:
        // a slow-updating entity on a jittery link needs both margins, not the larger of them.
        long jitterMargin = 2 * NetworkJitterMs;

        // Below two snapshots there is no interval to measure and nothing to interpolate between,
        // so the network floor is as good an answer as exists.
        long unclamped = interval <= 0 ? DelayMs + jitterMargin : (interval * 2) + jitterMargin;

        return Math.Clamp(unclamped, DelayMs, MaxDelayMs);
    }

    /// <summary>
    ///     One entity's adaptation state.
    /// </summary>
    /// <param name="AppliedMs">The delay in force, which chases the target rather than jumping to it.</param>
    /// <param name="MarginMs">
    ///     Extra delay bought by starvation, on top of what the update interval and jitter ask for.
    ///     Repaid slowly once the entity is fed again.
    /// </param>
    /// <param name="Starving">
    ///     Whether the last sample ran past the buffer. Carried between passes because the delay for
    ///     the next one is computed before that pass's sample exists.
    /// </param>
    private record struct DelayState(long AppliedMs, long MarginMs, bool Starving);

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
