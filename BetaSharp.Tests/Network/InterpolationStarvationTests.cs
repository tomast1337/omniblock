using OmniBlock.Client.Network;

namespace OmniBlock.Tests.Network;

/// <summary>
///     What happens when an entity runs out of buffered future, and what happens when its data
///     comes back.
///     <para>
///         Three things have to hold: extrapolate, then freeze, and on recovery do not snap. The
///         third is expressed through the delay rather than through a second per-entity clock,
///         because the delay <em>is</em> the render-time
///         offset, so growing it at exactly the rate the server clock advances holds render time
///         still, and the existing asymmetric ramp then eases it back out for free.
///     </para>
///     <para>
///         These drive <see cref="EntityInterpolator.Advance" /> a tick at a time rather than
///         <c>Apply</c>, which needs a live world. The quantity under test is the sequence of
///         positions a stall produces, and that is entirely decided before any entity is written to.
///     </para>
/// </summary>
public sealed class InterpolationStarvationTests
{
    private const long TickMs = 50;
    private const long IntervalMs = 100;

    /// <summary>
    ///     One entity on a player's tracking frequency, moving along +X at one block per snapshot.
    ///     Position and server time are in step, so a sampled X reads directly as "which instant is
    ///     being rendered".
    /// </summary>
    private static SnapshotBuffer Moving(long fromMs = 1000, int snapshots = 8)
    {
        SnapshotBuffer buffer = new();
        for (int i = 0; i < snapshots; i++)
        {
            long time = fromMs + (i * IntervalMs);
            buffer.Push(new Snapshot(time, time / (double)IntervalMs, 0, 0, 0, 0));
        }

        return buffer;
    }

    private static void Push(SnapshotBuffer buffer, long timeMs) =>
        buffer.Push(new Snapshot(timeMs, timeMs / (double)IntervalMs, 0, 0, 0, 0));

    /// <summary>
    ///     A pass in which the stream is stalled, which is the only condition under which a frozen
    ///     entity is blamed on the network rather than on having stopped moving.
    /// </summary>
    private static EntityInterpolator.InterpolationTick Stalled(long serverTimeMs) =>
        new(serverTimeMs, TickMs, StreamStalled: true);

    /// <summary>
    ///     Runs the interpolator forward, feeding it nothing. Returns the last sample and kind.
    /// </summary>
    private static (SampleKind Kind, Snapshot Sample, long ServerTimeMs) Run(
        EntityInterpolator interpolator, SnapshotBuffer buffer, long serverTimeMs, int ticks)
    {
        SampleKind kind = SampleKind.Empty;
        Snapshot sample = default;

        for (int i = 0; i < ticks; i++)
        {
            kind = interpolator.Advance(1, buffer, Stalled(serverTimeMs), out sample);
            serverTimeMs += TickMs;
        }

        return (kind, sample, serverTimeMs);
    }

    /// <summary>
    ///     A stall long enough to exhaust extrapolation freezes the entity and counts one event —
    ///     one, not one per tick spent frozen. The question is "how often did this break down", and
    ///     a per-tick count answers "how long has it been broken", which the Frozen gauge already
    ///     says.
    /// </summary>
    [Fact]
    public void A_stall_past_the_extrapolation_cap_counts_one_starvation_event()
    {
        EntityInterpolator interpolator = new();
        SnapshotBuffer buffer = Moving();

        (SampleKind kind, _, _) = Run(interpolator, buffer, serverTimeMs: 1800, ticks: 40);

        Assert.Equal(SampleKind.Frozen, kind);
        Assert.Equal(1, interpolator.StarvationEvents);
    }

    /// <summary>
    ///     An entity that has only just come into range has one snapshot and no history to have run
    ///     past. Counting that as starvation would credit every new entity a margin for being new,
    ///     and would make the metric a count of entities seen rather than of stalls.
    /// </summary>
    [Fact]
    public void A_newly_tracked_entity_is_not_counted_as_starving()
    {
        EntityInterpolator interpolator = new();
        SnapshotBuffer buffer = Moving(snapshots: 1);

        (SampleKind kind, _, _) = Run(interpolator, buffer, serverTimeMs: 5000, ticks: 10);

        Assert.Equal(SampleKind.Frozen, kind);
        Assert.Equal(0, interpolator.StarvationEvents);
    }

    /// <summary>
    ///     The regression that shipped with the first cut of this and was caught in-game.
    ///     <para>
    ///         <c>EntityTrackerEntry</c> only emits a position packet when the entity moved or
    ///         turned, so a standing mob sends nothing at all until its 400-tick resync twenty
    ///         seconds later. Its buffer is then indistinguishable from one whose updates were lost,
    ///         and crediting it walks the delay to the hard bound for an entity that is not moving —
    ///         then spends thirty seconds of 110% playback repaying a debt that bought nothing.
    ///     </para>
    ///     <para>
    ///         Measured at 1180 events and 181 of 368 entities permanently mid-ramp on an otherwise
    ///         healthy loopback connection, which is a field of standing cows, not a network fault.
    ///     </para>
    /// </summary>
    [Fact]
    public void An_entity_that_merely_stopped_moving_earns_no_credit()
    {
        EntityInterpolator interpolator = new();
        SnapshotBuffer buffer = Moving();

        long serverTimeMs = 1800;
        SampleKind kind = SampleKind.Empty;

        // Nothing arrives for this entity, but the stream as a whole is healthy: other entities are
        // still being updated, so this one stopped rather than the network.
        for (int i = 0; i < 40; i++)
        {
            kind = interpolator.Advance(
                1, buffer, new EntityInterpolator.InterpolationTick(serverTimeMs, TickMs, false), out _);
            serverTimeMs += TickMs;
        }

        // Frozen is still correct — there is nothing to interpolate — but it is not the network's
        // doing, so it costs neither an event nor a millisecond of delay.
        Assert.Equal(SampleKind.Frozen, kind);
        Assert.Equal(0, interpolator.StarvationEvents);
        Assert.Equal(IntervalMs * 2, interpolator.AppliedDelayFor(1));
    }

    /// <summary>
    ///     The stream-age test itself: a pass in which something did arrive recently is not a stall,
    ///     and one in which nothing has is.
    /// </summary>
    [Fact]
    public void The_stream_is_judged_stalled_only_when_nothing_at_all_has_arrived()
    {
        EntityInterpolator interpolator = new() { Available = true };
        interpolator.Record(1, 1000, 0, 0, 0, 0, 0);

        Assert.False(
            interpolator.IsStreamStalledAt(1000 + EntityInterpolator.StreamStallMs),
            "a stream updated within the threshold is not stalled");

        Assert.True(interpolator.IsStreamStalledAt(1000 + EntityInterpolator.StreamStallMs + 1));

        // Any entity's update refreshes the judgement: the question is about the stream, not about
        // whichever entity happens to be frozen.
        interpolator.Record(2, 5000, 0, 0, 0, 0, 0);
        Assert.False(interpolator.IsStreamStalledAt(5000));
    }

    /// <summary>
    ///     While frozen the delay tracks the clock exactly, which is what "render time is pinned"
    ///     means in the one currency this class has. The raise limit does not apply: it exists to
    ///     keep rendered motion from slowing visibly, and there is no motion left to slow.
    /// </summary>
    [Fact]
    public void A_frozen_entitys_delay_grows_at_the_full_clock_rate()
    {
        EntityInterpolator interpolator = new();
        SnapshotBuffer buffer = Moving();

        Run(interpolator, buffer, serverTimeMs: 1800, ticks: 10);
        Assert.Equal(SampleKind.Frozen, interpolator.Advance(1, buffer, Stalled(2300), out _));

        long before = interpolator.AppliedDelayFor(1);
        interpolator.Advance(1, buffer, Stalled(2350), out _);

        Assert.Equal(before + TickMs, interpolator.AppliedDelayFor(1));
        Assert.True(
            TickMs > EntityInterpolator.DelayRaisePerTickMs,
            "the point of the exemption is that it exceeds the normal raise limit");
    }

    /// <summary>
    ///     The property the whole mechanism exists for. Render time is held where playback stopped,
    ///     so the entity's rendered position does not creep while it is frozen.
    /// </summary>
    [Fact]
    public void A_frozen_entity_holds_one_position_rather_than_drifting()
    {
        EntityInterpolator interpolator = new();
        SnapshotBuffer buffer = Moving();

        Run(interpolator, buffer, serverTimeMs: 1800, ticks: 12);

        (_, Snapshot first, long serverTimeMs) = Run(interpolator, buffer, 2400, ticks: 1);
        (SampleKind kind, Snapshot later, _) = Run(interpolator, buffer, serverTimeMs, ticks: 20);

        Assert.Equal(SampleKind.Frozen, kind);
        Assert.Equal(first.X, later.X, precision: 6);
    }

    /// <summary>
    ///     Recovery, stated as the number that matters: how much of the entity's timeline is
    ///     skipped when its data comes back.
    ///     <para>
    ///         Without the pin, render time runs on through the whole stall and the entity resumes
    ///         from wherever the clock had reached — a two-second stall discards two seconds of
    ///         motion in one tick. With it, the skip is bounded by the extrapolation cap rather than
    ///         by the stall, which is what makes a long stall recoverable at all.
    ///     </para>
    ///     <para>
    ///         Two things bound how long a stall this survives, and the tighter one is not the
    ///         obvious one. <see cref="EntityInterpolator.MaxDelayMs" /> caps the credit at three
    ///         seconds, but <see cref="SnapshotBuffer.Capacity" /> caps the <em>history</em> at
    ///         twenty updates — two seconds for a player, and less for anything tracked faster. Past
    ///         that the backlog overwrites the entries render time was pinned among, and it falls off
    ///         the back of the buffer no matter how much credit it holds.
    ///     </para>
    /// </summary>
    [Theory]
    [InlineData(10)]   // half a second
    [InlineData(20)]   // one second
    [InlineData(30)]   // one and a half, still inside the twenty updates the ring retains
    public void Recovery_skips_no_more_than_the_extrapolation_cap_however_long_the_stall(int stallTicks)
    {
        EntityInterpolator interpolator = new();
        SnapshotBuffer buffer = Moving();

        (_, Snapshot frozen, long serverTimeMs) = Run(interpolator, buffer, 1800, ticks: stallTicks);
        Assert.Equal(SampleKind.Frozen, interpolator.Advance(1, buffer, Stalled(serverTimeMs), out frozen));
        serverTimeMs += TickMs;

        // The backlog lands: the server's updates for the whole stall arrive at once.
        for (long time = 1800; time <= serverTimeMs; time += IntervalMs)
        {
            Push(buffer, time);
        }

        interpolator.Advance(1, buffer, Stalled(serverTimeMs), out Snapshot resumed);

        // X is in units of IntervalMs, so a skip of one X is 100 ms of the entity's own timeline.
        double skippedMs = (resumed.X - frozen.X) * IntervalMs;

        Assert.True(skippedMs >= 0, $"recovery went backwards by {-skippedMs} ms");
        Assert.True(
            skippedMs <= SnapshotBuffer.ExtrapolationCapMs + TickMs,
            $"recovery skipped {skippedMs} ms of the entity's timeline after a "
            + $"{stallTicks * TickMs} ms stall");
    }

    /// <summary>
    ///     The other side of the bounds, stated so it is a decision rather than a surprise. A stall
    ///     that outlasts either the credit or the retained history does skip on recovery, and that is
    ///     the intended trade: holding render time indefinitely would render an entity arbitrarily
    ///     far in the past, which is worse than one visible correction.
    /// </summary>
    [Fact]
    public void A_stall_past_the_bounds_gives_up_and_skips()
    {
        EntityInterpolator interpolator = new();
        SnapshotBuffer buffer = Moving();

        (_, Snapshot frozen, long serverTimeMs) = Run(interpolator, buffer, 1800, ticks: 120);
        Assert.Equal(EntityInterpolator.MaxDelayMs, interpolator.AppliedDelayFor(1));

        for (long time = 1800; time <= serverTimeMs; time += IntervalMs)
        {
            Push(buffer, time);
        }

        interpolator.Advance(1, buffer, Stalled(serverTimeMs), out Snapshot resumed);

        Assert.True(
            (resumed.X - frozen.X) * IntervalMs > SnapshotBuffer.ExtrapolationCapMs,
            "past the bounds the entity is expected to skip; if it no longer does, a bound moved");
    }

    /// <summary>
    ///     Which bound binds first, made explicit because it is the surprising one. Twenty updates of
    ///     history is two seconds for a player and 600 ms for anything tracked every tick, and both
    ///     are shorter than the three-second credit — so on a fast-tracked entity the buffer runs out
    ///     of past long before the delay runs out of room.
    /// </summary>
    [Fact]
    public void Retained_history_is_the_tighter_of_the_two_bounds_for_fast_tracked_entities()
    {
        long historyMs = SnapshotBuffer.Capacity * IntervalMs;

        Assert.True(
            historyMs < EntityInterpolator.MaxDelayMs,
            $"{historyMs} ms of history against a {EntityInterpolator.MaxDelayMs} ms credit: if the "
            + "credit is now the tighter one, the recovery bound is no longer the ring's capacity");
    }

    /// <summary>
    ///     The credit is repaid, or a single stall would leave the entity permanently rendered
    ///     further behind than its update rate warrants.
    /// </summary>
    [Fact]
    public void The_credit_a_stall_bought_is_repaid_once_the_entity_is_fed_again()
    {
        EntityInterpolator interpolator = new();
        SnapshotBuffer buffer = Moving();

        (_, _, long serverTimeMs) = Run(interpolator, buffer, 1800, ticks: 20);
        long stalled = interpolator.AppliedDelayFor(1);
        Assert.True(stalled > IntervalMs * 2, "the stall should have bought margin over the base delay");

        // Feed it steadily again for long enough to unwind, one snapshot per two ticks.
        for (int tick = 0; tick < 400; tick++)
        {
            if (tick % 2 == 0)
            {
                Push(buffer, serverTimeMs);
            }

            // Healthy again, so the stream is no longer stalled and the credit is repaid.
            interpolator.Advance(
                1, buffer, new EntityInterpolator.InterpolationTick(serverTimeMs, TickMs, false), out _);
            serverTimeMs += TickMs;
        }

        Assert.Equal(IntervalMs * 2, interpolator.AppliedDelayFor(1));
        Assert.True(interpolator.AppliedDelayFor(1) < stalled);
    }

    /// <summary>
    ///     The credit cannot outrun the hard bound, or a peer that stopped sending entirely would
    ///     accumulate render-time debt without limit and then discard all of it at once.
    /// </summary>
    [Fact]
    public void Starvation_credit_is_bounded_by_the_hard_delay_bound()
    {
        EntityInterpolator interpolator = new();
        SnapshotBuffer buffer = Moving();

        Run(interpolator, buffer, 1800, ticks: 2000);

        Assert.Equal(EntityInterpolator.MaxDelayMs, interpolator.AppliedDelayFor(1));
    }

    /// <summary>
    ///     A clock step invalidates the timeline the credit was bought against, so it goes with the
    ///     buffers rather than being eased away from on a timeline it does not belong to.
    /// </summary>
    [Fact]
    public void A_clock_step_drops_starvation_credit_with_the_buffers()
    {
        EntityInterpolator interpolator = new();
        SnapshotBuffer buffer = Moving();

        Run(interpolator, buffer, 1800, ticks: 20);
        Assert.True(interpolator.AppliedDelayFor(1) > IntervalMs * 2);

        interpolator.Clear();

        Assert.Equal(IntervalMs * 2, interpolator.SmoothedDelayFor(1, buffer));
    }

    /// <summary>
    ///     Freezing must not itself move the entity. The position at the cap is where extrapolation
    ///     had reached; returning the newest snapshot instead would snap it back by up to a quarter
    ///     second of motion at the exact moment the guess was abandoned.
    /// </summary>
    [Fact]
    public void Freezing_holds_the_extrapolated_position_rather_than_snapping_back()
    {
        SnapshotBuffer buffer = Moving();
        long newest = buffer.NewestServerTimeMs;

        buffer.Sample(newest + SnapshotBuffer.ExtrapolationCapMs, out Snapshot atCap);
        SampleKind kind = buffer.Sample(newest + SnapshotBuffer.ExtrapolationCapMs + 1, out Snapshot pastCap);

        Assert.Equal(SampleKind.Frozen, kind);
        Assert.Equal(atCap.X, pastCap.X, precision: 6);
    }
}
