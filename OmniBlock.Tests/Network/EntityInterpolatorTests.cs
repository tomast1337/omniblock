using OmniBlock.Client.Network;

namespace OmniBlock.Tests.Network;

/// <summary>
///     Gating and lifecycle. <see cref="EntityInterpolator.Apply" /> needs a live world and is
///     covered by <see cref="SnapshotBufferTests" /> for the arithmetic it delegates to.
/// </summary>
public sealed class EntityInterpolatorTests
{
    private static EntityInterpolator Available() => new()
    {
        Available = true
    };

    // ---- gating ----

    [Fact]
    public void Not_active_until_a_timeline_exists()
    {
        // Enabled by default, but nothing to sample against until the server stamps and the clock
        // syncs. Active must stay false or the legacy path gets skipped with no replacement.
        EntityInterpolator interpolator = new();

        Assert.True(interpolator.Enabled);
        Assert.False(interpolator.Available);
        Assert.False(interpolator.Active);
    }

    [Fact]
    public void Available_alone_does_not_activate_a_disabled_interpolator()
    {
        EntityInterpolator interpolator = new()
        {
            Available = true,
            Enabled = false
        };
        Assert.False(interpolator.Active);
    }

    [Fact]
    public void Active_requires_both() => Assert.True(Available().Active);

    [Fact]
    public void An_entity_is_not_interpolating_while_the_timeline_is_missing()
    {
        // The regression this guards: if IsInterpolating returned true here, the caller would skip
        // the legacy retarget while Apply did nothing, and every remote entity would freeze.
        EntityInterpolator interpolator = new();
        interpolator.Record(1, 1000, 0, 0, 0, 0, 0);

        Assert.False(interpolator.IsInterpolating(1));
    }

    [Fact]
    public void An_entity_is_interpolating_once_the_timeline_exists()
    {
        var interpolator = Available();
        interpolator.Record(1, 1000, 0, 0, 0, 0, 0);

        Assert.True(interpolator.IsInterpolating(1));
    }

    [Fact]
    public void An_unknown_entity_is_never_interpolating() => Assert.False(Available().IsInterpolating(99));

    // ---- recording ----

    [Fact]
    public void A_snapshot_without_a_timestamp_is_dropped()
    {
        // Zero means the server does not stamp. Recording it would put a snapshot on no timeline,
        // which is the guess this whole mechanism replaces.
        var interpolator = Available();
        interpolator.Record(1, 0, 0, 0, 0, 0, 0);

        Assert.Equal(0, interpolator.TrackedCount);
        Assert.False(interpolator.IsInterpolating(1));
    }

    [Fact]
    public void Recording_tracks_one_buffer_per_entity()
    {
        var interpolator = Available();
        interpolator.Record(1, 1000, 0, 0, 0, 0, 0);
        interpolator.Record(1, 1050, 1, 0, 0, 0, 0);
        interpolator.Record(2, 1050, 0, 0, 0, 0, 0);

        Assert.Equal(2, interpolator.TrackedCount);
    }

    // ---- lifecycle ----

    [Fact]
    public void Forget_drops_only_that_entity()
    {
        var interpolator = Available();
        interpolator.Record(1, 1000, 0, 0, 0, 0, 0);
        interpolator.Record(2, 1000, 0, 0, 0, 0, 0);

        interpolator.Forget(1);

        Assert.Equal(1, interpolator.TrackedCount);
        Assert.False(interpolator.IsInterpolating(1));
        Assert.True(interpolator.IsInterpolating(2));
    }

    [Fact]
    public void Forgetting_an_untracked_entity_is_harmless()
    {
        var interpolator = Available();
        interpolator.Forget(42);

        Assert.Equal(0, interpolator.TrackedCount);
    }

    [Fact]
    public void Clear_drops_everything()
    {
        var interpolator = Available();
        interpolator.Record(1, 1000, 0, 0, 0, 0, 0);
        interpolator.Record(2, 1000, 0, 0, 0, 0, 0);

        interpolator.Clear();

        Assert.Equal(0, interpolator.TrackedCount);
    }

    [Fact]
    public void The_default_delay_is_the_documented_floor()
    {
        // The floor from clamp(2 * interval + 2 * jitter, floor, bound). Two tick intervals at
        // 20 TPS is the minimum that keeps two snapshots bracketing render time.
        Assert.Equal(100, EntityInterpolator.DefaultDelayMs);
        Assert.Equal(EntityInterpolator.DefaultDelayMs, new EntityInterpolator().DelayMs);
    }

    /// <summary>
    ///     The jitter term. It reads 4 ms on loopback UDP and 0 on loopback TCP, which is why it
    ///     cannot be sized from a local session and why it is added rather than tuned: twice the
    ///     mean absolute deviation is the figure, and there is nothing local to calibrate against.
    /// </summary>
    [Theory]
    [InlineData(0, 300)]
    [InlineData(20, 340)]
    [InlineData(80, 460)]
    public void Jitter_widens_every_entitys_delay_by_twice_its_value(long jitterMs, long expected)
    {
        EntityInterpolator interpolator = new()
        {
            NetworkJitterMs = jitterMs
        };

        Assert.Equal(expected, interpolator.DelayForMs(BufferAtInterval(150)));
    }

    /// <summary>
    ///     Added to the interval term rather than folded into it. They answer different questions —
    ///     how often the server speaks about this entity, and how unevenly the network delivers what
    ///     it says — so an entity on a slow tracking frequency over a jittery link needs both
    ///     margins, not the larger of the two.
    /// </summary>
    [Fact]
    public void The_jitter_margin_applies_on_top_of_a_slow_tracking_frequency()
    {
        EntityInterpolator interpolator = new()
        {
            NetworkJitterMs = 100
        };

        // A dropped item at one update per second: 2000 ms of interval, plus 200 ms of jitter.
        Assert.Equal(2200, interpolator.DelayForMs(BufferAtInterval(1000)));
    }

    /// <summary>
    ///     Jitter cannot push the delay past the bound. It is measured from a peer that may be
    ///     misbehaving, and an unbounded delay is a worse failure than a jittery one.
    /// </summary>
    [Fact]
    public void The_jitter_margin_is_still_bounded()
    {
        EntityInterpolator interpolator = new()
        {
            NetworkJitterMs = 10_000
        };

        Assert.Equal(EntityInterpolator.MaxDelayMs, interpolator.DelayForMs(BufferAtInterval(150)));
    }

    /// <summary>
    ///     The floor case: too few snapshots to measure an interval still gets the jitter margin,
    ///     since a newly-tracked entity on a jittery link is exactly where the margin is needed.
    /// </summary>
    [Fact]
    public void A_buffer_with_no_measurable_interval_still_gets_the_jitter_margin()
    {
        EntityInterpolator interpolator = new()
        {
            NetworkJitterMs = 40
        };

        Assert.Equal(
            EntityInterpolator.DefaultDelayMs + 80,
            interpolator.DelayForMs(BufferAtInterval(150, 1)));
    }

    private static SnapshotBuffer BufferAtInterval(long intervalMs, int snapshots = 8)
    {
        SnapshotBuffer buffer = new();
        for (var i = 0; i < snapshots; i++)
        {
            buffer.Push(new Snapshot(1000 + i * intervalMs, i, 0, 0, 0, 0));
        }

        return buffer;
    }

    /// <summary>
    ///     The delay tracks each entity's own update rate, because the rate is per-entity:
    ///     <c>EntityTrackerEntry</c> sends every <c>trackingFrequency</c> ticks, 2 for players
    ///     through 20 for dropped items.
    /// </summary>
    [Theory]
    [InlineData(100, 200)] // players, every 2 ticks
    [InlineData(150, 300)] // mobs, every 3 ticks
    [InlineData(500, 1000)] // projectiles, every 10 ticks
    [InlineData(1000, 2000)] // dropped items, every 20 ticks
    public void The_delay_is_twice_the_observed_interval(long intervalMs, long expectedDelayMs)
    {
        EntityInterpolator interpolator = new();

        Assert.Equal(expectedDelayMs, interpolator.DelayForMs(BufferAtInterval(intervalMs)));
    }

    /// <summary>
    ///     The regression this replaced. A 600 ms ceiling capped exactly the entities that needed
    ///     the most delay — anything slower than a 300 ms update — so they were rendered ahead of
    ///     their own newest snapshot every frame and froze. Measured at 29 of 985 entities.
    /// </summary>
    [Fact]
    public void An_entity_on_a_slow_tracking_frequency_is_not_capped_into_starvation()
    {
        EntityInterpolator interpolator = new();

        // A dropped item: one update per second, so it needs two seconds of delay to have
        // snapshots on both sides of render time.
        var delay = interpolator.DelayForMs(BufferAtInterval(1000));

        Assert.True(delay >= 2000, $"delay of {delay} ms cannot bracket a 1000 ms update interval");
        Assert.True(delay <= EntityInterpolator.MaxDelayMs);
    }

    [Fact]
    public void The_floor_still_applies_to_fast_updating_entities()
    {
        EntityInterpolator interpolator = new();

        // 20 ms apart is faster than the server ticks; the network floor wins over 2 x interval.
        Assert.Equal(EntityInterpolator.DefaultDelayMs, interpolator.DelayForMs(BufferAtInterval(20)));
    }

    /// <summary>
    ///     The bound is for an entity that has stopped updating, not a quality setting. It has to
    ///     still bind, or a buffer left behind by a stalled entity would grow an unbounded delay.
    /// </summary>
    [Fact]
    public void An_entity_that_stopped_updating_is_bounded()
    {
        EntityInterpolator interpolator = new();

        Assert.Equal(EntityInterpolator.MaxDelayMs, interpolator.DelayForMs(BufferAtInterval(30_000)));
    }

    [Fact]
    public void A_buffer_with_one_snapshot_has_no_interval_and_falls_back_to_the_floor()
    {
        EntityInterpolator interpolator = new();

        Assert.Equal(EntityInterpolator.DefaultDelayMs, interpolator.DelayForMs(BufferAtInterval(150, 1)));
    }

    /// <summary>
    ///     Nothing to ease away from on first sight, and ramping from the floor to a dropped item's
    ///     two seconds would spend the whole ramp in slow motion for no benefit.
    /// </summary>
    [Fact]
    public void The_first_sighting_adopts_its_target_delay_whole()
    {
        EntityInterpolator interpolator = new();

        Assert.Equal(2000, interpolator.SmoothedDelayFor(1, BufferAtInterval(1000)));
    }

    /// <summary>
    ///     The reason the ramp exists. Recomputing from the observed median every tick makes the
    ///     delay jump the moment the median moves, and the entity is repositioned by the whole
    ///     difference in one tick — a teleport into its own past.
    /// </summary>
    [Fact]
    public void A_delay_increase_is_approached_at_the_raise_rate()
    {
        EntityInterpolator interpolator = new();

        interpolator.SmoothedDelayFor(1, BufferAtInterval(150)); // settles at 300

        var afterOneTick = interpolator.SmoothedDelayFor(1, BufferAtInterval(400)); // target 800

        Assert.Equal(300 + EntityInterpolator.DelayRaisePerTickMs, afterOneTick);
    }

    [Fact]
    public void A_delay_decrease_is_approached_at_the_slower_lower_rate()
    {
        EntityInterpolator interpolator = new();

        interpolator.SmoothedDelayFor(1, BufferAtInterval(400)); // settles at 800

        var afterOneTick = interpolator.SmoothedDelayFor(1, BufferAtInterval(150)); // target 300

        Assert.Equal(800 - EntityInterpolator.DelayLowerPerTickMs, afterOneTick);
    }

    [Fact]
    public void The_ramp_converges_on_its_target_and_then_holds()
    {
        EntityInterpolator interpolator = new();

        interpolator.SmoothedDelayFor(1, BufferAtInterval(150));

        var slower = BufferAtInterval(400);
        long delay = 0;
        for (var tick = 0; tick < 200; tick++)
        {
            delay = interpolator.SmoothedDelayFor(1, slower);
        }

        Assert.Equal(800, delay);
        Assert.Equal(800, interpolator.SmoothedDelayFor(1, slower));
    }

    /// <summary>
    ///     The constraint that sets the raise rate. Render time is <c>serverTime - delay</c>, so a
    ///     delay growing by <i>d</i> per 50 ms tick advances render time by <c>50 - d</c>. At the
    ///     tick interval the entity stops; past it, it walks backwards.
    /// </summary>
    [Fact]
    public void The_raise_rate_stays_below_the_tick_interval_so_render_time_never_reverses()
    {
        const long tickIntervalMs = 50;

        Assert.True(
            EntityInterpolator.DelayRaisePerTickMs < tickIntervalMs,
            "a raise rate at or above the tick interval stalls or reverses rendered motion");
        Assert.True(EntityInterpolator.DelayLowerPerTickMs < EntityInterpolator.DelayRaisePerTickMs);
    }

    [Fact]
    public void Forgetting_an_entity_drops_its_ramp_state()
    {
        EntityInterpolator interpolator = new();

        interpolator.SmoothedDelayFor(1, BufferAtInterval(400)); // settles at 800
        interpolator.Forget(1);

        // Re-adopted whole rather than eased down from 800.
        Assert.Equal(300, interpolator.SmoothedDelayFor(1, BufferAtInterval(150)));
    }
}
