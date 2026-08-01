using BetaSharp.Client.Network;

namespace BetaSharp.Tests.Network;

/// <summary>
///     Gating and lifecycle. <see cref="EntityInterpolator.Apply" /> needs a live world and is
///     covered by <see cref="SnapshotBufferTests" /> for the arithmetic it delegates to.
/// </summary>
public sealed class EntityInterpolatorTests
{
    private static EntityInterpolator Available() => new() { Available = true };

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
        EntityInterpolator interpolator = new() { Available = true, Enabled = false };
        Assert.False(interpolator.Active);
    }

    [Fact]
    public void Active_requires_both()
    {
        Assert.True(Available().Active);
    }

    [Fact]
    public void An_entity_is_not_interpolating_while_the_timeline_is_missing()
    {
        // The regression this guards: if IsInterpolating returned true here, the caller would skip
        // the legacy retarget while Apply did nothing, and every remote entity would freeze.
        EntityInterpolator interpolator = new();
        interpolator.Record(1, serverTimeMs: 1000, 0, 0, 0, 0, 0);

        Assert.False(interpolator.IsInterpolating(1));
    }

    [Fact]
    public void An_entity_is_interpolating_once_the_timeline_exists()
    {
        EntityInterpolator interpolator = Available();
        interpolator.Record(1, serverTimeMs: 1000, 0, 0, 0, 0, 0);

        Assert.True(interpolator.IsInterpolating(1));
    }

    [Fact]
    public void An_unknown_entity_is_never_interpolating()
    {
        Assert.False(Available().IsInterpolating(99));
    }

    // ---- recording ----

    [Fact]
    public void A_snapshot_without_a_timestamp_is_dropped()
    {
        // Zero means the server does not stamp. Recording it would put a snapshot on no timeline,
        // which is the guess this whole mechanism replaces.
        EntityInterpolator interpolator = Available();
        interpolator.Record(1, serverTimeMs: 0, 0, 0, 0, 0, 0);

        Assert.Equal(0, interpolator.TrackedCount);
        Assert.False(interpolator.IsInterpolating(1));
    }

    [Fact]
    public void Recording_tracks_one_buffer_per_entity()
    {
        EntityInterpolator interpolator = Available();
        interpolator.Record(1, 1000, 0, 0, 0, 0, 0);
        interpolator.Record(1, 1050, 1, 0, 0, 0, 0);
        interpolator.Record(2, 1050, 0, 0, 0, 0, 0);

        Assert.Equal(2, interpolator.TrackedCount);
    }

    // ---- lifecycle ----

    [Fact]
    public void Forget_drops_only_that_entity()
    {
        EntityInterpolator interpolator = Available();
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
        EntityInterpolator interpolator = Available();
        interpolator.Forget(42);

        Assert.Equal(0, interpolator.TrackedCount);
    }

    [Fact]
    public void Clear_drops_everything()
    {
        EntityInterpolator interpolator = Available();
        interpolator.Record(1, 1000, 0, 0, 0, 0, 0);
        interpolator.Record(2, 1000, 0, 0, 0, 0, 0);

        interpolator.Clear();

        Assert.Equal(0, interpolator.TrackedCount);
    }

    [Fact]
    public void The_default_delay_is_the_documented_floor()
    {
        // clamp(2 * tickInterval + 2 * jitter, 100, 500) with zero jitter. Two tick intervals at
        // 20 TPS is the minimum that keeps two snapshots bracketing render time.
        Assert.Equal(100, EntityInterpolator.DefaultDelayMs);
        Assert.Equal(EntityInterpolator.DefaultDelayMs, new EntityInterpolator().DelayMs);
    }

    private static SnapshotBuffer BufferAtInterval(long intervalMs, int snapshots = 8)
    {
        SnapshotBuffer buffer = new();
        for (int i = 0; i < snapshots; i++)
        {
            buffer.Push(new Snapshot(1000 + (i * intervalMs), i, 0, 0, 0, 0));
        }

        return buffer;
    }

    /// <summary>
    ///     The delay tracks each entity's own update rate, because the rate is per-entity:
    ///     <c>EntityTrackerEntry</c> sends every <c>trackingFrequency</c> ticks, 2 for players
    ///     through 20 for dropped items.
    /// </summary>
    [Theory]
    [InlineData(100, 200)]    // players, every 2 ticks
    [InlineData(150, 300)]    // mobs, every 3 ticks
    [InlineData(500, 1000)]   // projectiles, every 10 ticks
    [InlineData(1000, 2000)]  // dropped items, every 20 ticks
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
        long delay = interpolator.DelayForMs(BufferAtInterval(1000));

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

        Assert.Equal(EntityInterpolator.DefaultDelayMs, interpolator.DelayForMs(BufferAtInterval(150, snapshots: 1)));
    }
}
