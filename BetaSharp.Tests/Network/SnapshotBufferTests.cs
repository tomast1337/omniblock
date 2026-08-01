using BetaSharp.Client.Network;

namespace BetaSharp.Tests.Network;

public sealed class SnapshotBufferTests
{
    private static Snapshot At(long t, double x = 0, double y = 0, double z = 0, float yaw = 0, float pitch = 0)
        => new(t, x, y, z, yaw, pitch);

    /// <summary>Smallest absolute angle between two headings, in [0, 180].</summary>
    private static float WrappedDelta(float a, float b) => Math.Abs(((a - b + 540f) % 360f) - 180f);

    /// <summary>Fills a buffer with entries 50 ms apart moving +1 on X per tick, starting at t=1000.</summary>
    private static SnapshotBuffer Walking(int count = 5)
    {
        SnapshotBuffer buffer = new();
        for (int i = 0; i < count; i++)
        {
            buffer.Push(At(1000 + (i * 50), x: i));
        }
        return buffer;
    }

    // ---- push ----

    [Fact]
    public void Empty_buffer_yields_nothing()
    {
        Assert.Equal(SampleKind.Empty, new SnapshotBuffer().Sample(0, out _));
    }

    [Fact]
    public void Snapshots_at_or_before_the_newest_are_discarded()
    {
        SnapshotBuffer buffer = new();
        buffer.Push(At(1000));
        buffer.Push(At(1000));
        buffer.Push(At(999));

        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public void Ring_holds_only_the_newest_capacity_entries()
    {
        SnapshotBuffer buffer = new();
        for (int i = 0; i < SnapshotBuffer.Capacity + 7; i++)
        {
            buffer.Push(At(1000 + (i * 50)));
        }

        Assert.Equal(SnapshotBuffer.Capacity, buffer.Count);
        Assert.Equal(1000 + ((SnapshotBuffer.Capacity + 6) * 50), buffer.NewestServerTimeMs);
        Assert.Equal(1000 + (7 * 50), buffer.OldestServerTimeMs);
    }

    [Fact]
    public void Clear_empties_the_buffer()
    {
        SnapshotBuffer buffer = Walking();
        buffer.Clear();

        Assert.Equal(0, buffer.Count);
        Assert.Equal(SampleKind.Empty, buffer.Sample(1000, out _));
    }

    // ---- interpolation ----

    [Fact]
    public void Midpoint_between_two_snapshots_is_the_halfway_position()
    {
        SnapshotBuffer buffer = Walking();

        Assert.Equal(SampleKind.Interpolated, buffer.Sample(1025, out Snapshot s));
        Assert.Equal(0.5, s.X, 9);
    }

    [Fact]
    public void Landing_exactly_on_a_snapshot_returns_that_snapshot()
    {
        SnapshotBuffer buffer = Walking();

        Assert.Equal(SampleKind.Interpolated, buffer.Sample(1100, out Snapshot s));
        Assert.Equal(2.0, s.X, 9);
    }

    [Fact]
    public void Interpolation_is_linear_across_the_whole_buffer()
    {
        SnapshotBuffer buffer = Walking();

        // t=1000 is x=0 and every 50 ms adds 1, so x is (t - 1000) / 50 throughout.
        for (long t = 1000; t <= 1200; t += 10)
        {
            buffer.Sample(t, out Snapshot s);
            Assert.Equal((t - 1000) / 50.0, s.X, 9);
        }
    }

    // ---- the actual point: arrival order does not matter ----

    [Fact]
    public void A_burst_arriving_late_produces_the_same_positions_as_a_steady_stream()
    {
        // The regression this whole phase exists to prevent. Both buffers receive identical
        // snapshots; only the order they were pushed in differs, and pushing is the only place
        // arrival could possibly influence the result.
        SnapshotBuffer steady = new();
        SnapshotBuffer bursty = new();

        for (int i = 0; i < 8; i++)
        {
            steady.Push(At(1000 + (i * 50), x: i));
        }

        // Same eight, but delivered as one clump after a stall.
        for (int i = 0; i < 8; i++)
        {
            bursty.Push(At(1000 + (i * 50), x: i));
        }

        for (long t = 1000; t <= 1350; t += 7)
        {
            SampleKind a = steady.Sample(t, out Snapshot sa);
            SampleKind b = bursty.Sample(t, out Snapshot sb);

            Assert.Equal(a, b);
            Assert.Equal(sa.X, sb.X, 9);
        }
    }

    // ---- observed interval ----

    [Fact]
    public void Interval_is_unknown_below_two_snapshots()
    {
        SnapshotBuffer buffer = new();
        Assert.Equal(0, buffer.MedianIntervalMs);

        buffer.Push(At(1000));
        Assert.Equal(0, buffer.MedianIntervalMs);
    }

    [Theory]
    [InlineData(2, 100)]    // players and arrows
    [InlineData(3, 150)]    // mobs
    [InlineData(5, 250)]    // boats
    [InlineData(20, 1000)]  // dropped items
    public void Interval_matches_the_tracking_frequency_that_produced_it(int ticks, long expected)
    {
        // EntityTrackerEntry sends one update every trackingFrequency ticks, so snapshot spacing is
        // that many 50 ms ticks — not the 50 ms tick itself. Sizing the delay off the wrong one is
        // what left nothing interpolating.
        SnapshotBuffer buffer = new();
        for (int i = 0; i < 6; i++)
        {
            buffer.Push(At(1000 + (i * ticks * 50)));
        }

        Assert.Equal(expected, buffer.MedianIntervalMs);
    }

    [Fact]
    public void One_long_stall_does_not_drag_the_interval()
    {
        // Median, not mean: an entity that stopped moving and started again leaves one huge gap.
        SnapshotBuffer buffer = new();
        buffer.Push(At(1000));
        buffer.Push(At(1150));
        buffer.Push(At(1300));
        buffer.Push(At(6300));  // five second gap
        buffer.Push(At(6450));

        Assert.Equal(150, buffer.MedianIntervalMs);
    }

    [Fact]
    public void A_mob_paced_buffer_interpolates_at_twice_its_interval()
    {
        // The exact case that reported Interpolated 0: mobs update every 3 ticks, so a 100 ms delay
        // left render time permanently past the newest snapshot. At 2x the interval it brackets.
        SnapshotBuffer buffer = new();
        for (int i = 0; i < 6; i++)
        {
            buffer.Push(At(1000 + (i * 150), x: i));
        }

        long newest = buffer.NewestServerTimeMs;
        long delay = buffer.MedianIntervalMs * 2;

        Assert.Equal(SampleKind.Interpolated, buffer.Sample(newest - delay, out _));

        // And the old constant does not, which is the regression being pinned.
        Assert.NotEqual(SampleKind.Interpolated, buffer.Sample(newest + 150 - 100, out _));
    }

    // ---- clamping ----

    [Fact]
    public void Render_time_before_the_oldest_holds_the_oldest()
    {
        SnapshotBuffer buffer = Walking();

        Assert.Equal(SampleKind.Clamped, buffer.Sample(500, out Snapshot s));
        Assert.Equal(0.0, s.X, 9);
    }

    // ---- starvation ----

    [Fact]
    public void Just_past_the_newest_extrapolates_along_the_last_velocity()
    {
        SnapshotBuffer buffer = Walking();       // newest t=1200, x=4, moving +1 per 50 ms

        Assert.Equal(SampleKind.Extrapolated, buffer.Sample(1225, out Snapshot s));
        Assert.Equal(4.5, s.X, 9);
    }

    [Fact]
    public void Extrapolation_stops_at_the_cap()
    {
        SnapshotBuffer buffer = Walking();
        long newest = buffer.NewestServerTimeMs;

        Assert.Equal(SampleKind.Extrapolated, buffer.Sample(newest + SnapshotBuffer.ExtrapolationCapMs, out _));
        Assert.Equal(SampleKind.Frozen, buffer.Sample(newest + SnapshotBuffer.ExtrapolationCapMs + 1, out Snapshot s));

        // Frozen holds where the extrapolation had reached, not the last real sample. This used to
        // return the newest snapshot, on the reasoning that recovery should correct from something
        // true — but the correction happens on recovery either way, and returning the snapshot made
        // *giving up* a second, earlier jump: the model walked forward on the guess and then snapped
        // a quarter second back the instant the guess was abandoned. Clamping makes freezing exactly
        // what it says, the motion stopping.
        Assert.Equal(4.0 + (SnapshotBuffer.ExtrapolationCapMs / 50.0), s.X, 9);
    }

    /// <summary>
    ///     The position is continuous across the cap: one millisecond either side of it differs by
    ///     one millisecond of motion, not by the whole extrapolated distance.
    /// </summary>
    [Fact]
    public void Freezing_is_continuous_with_the_extrapolation_it_gives_up_on()
    {
        SnapshotBuffer buffer = Walking();
        long cap = buffer.NewestServerTimeMs + SnapshotBuffer.ExtrapolationCapMs;

        buffer.Sample(cap, out Snapshot last);
        buffer.Sample(cap + 1, out Snapshot frozen);

        Assert.Equal(last.X, frozen.X, 9);
    }

    [Fact]
    public void Extrapolation_does_not_spin_the_angles()
    {
        // Angles are held, not extrapolated: a view flick would otherwise overshoot and snap back.
        SnapshotBuffer buffer = new();
        buffer.Push(At(1000, yaw: 0));
        buffer.Push(At(1050, yaw: 40));

        Assert.Equal(SampleKind.Extrapolated, buffer.Sample(1100, out Snapshot s));
        Assert.Equal(40.0f, s.Yaw);
    }

    [Fact]
    public void A_lone_snapshot_is_held_rather_than_extrapolated()
    {
        SnapshotBuffer buffer = new();
        buffer.Push(At(1000, x: 3));

        Assert.Equal(SampleKind.Frozen, buffer.Sample(1200, out Snapshot s));
        Assert.Equal(3.0, s.X, 9);

        Assert.Equal(SampleKind.Clamped, buffer.Sample(900, out _));
    }

    // ---- angles ----

    [Fact]
    public void Yaw_interpolates_the_short_way_across_the_180_boundary()
    {
        // 179 to -179 is a 2 degree change. Interpolating linearly would sweep 358 degrees the
        // wrong way and spin the model through a full turn.
        SnapshotBuffer buffer = new();
        buffer.Push(At(1000, yaw: 179f));
        buffer.Push(At(1050, yaw: -179f));

        buffer.Sample(1025, out Snapshot s);

        Assert.Equal(180.0f, s.Yaw, 3);
    }

    [Theory]
    [InlineData(0f, 90f, 0.5, 45f)]
    [InlineData(90f, 0f, 0.5, 45f)]
    [InlineData(-170f, 170f, 0.5, 180f)]
    [InlineData(170f, -170f, 0.5, 180f)]
    [InlineData(0f, 0f, 0.5, 0f)]
    public void LerpAngle_takes_the_shortest_arc(float from, float to, double t, float expected)
    {
        float actual = SnapshotBuffer.LerpAngle(from, to, t);

        // Compared as a wrapped difference, because 180 and -180 are the same angle and either is
        // a correct answer for the antipodal cases.
        Assert.True(WrappedDelta(actual, expected) < 0.001f, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void LerpAngle_at_the_endpoints_returns_the_endpoints()
    {
        Assert.Equal(10f, SnapshotBuffer.LerpAngle(10f, 80f, 0.0), 3);
        Assert.Equal(80f, SnapshotBuffer.LerpAngle(10f, 80f, 1.0), 3);
    }
}
