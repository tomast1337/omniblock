using OmniBlock.Client.Network;

namespace OmniBlock.Tests.Network;

public sealed class ServerClockTests
{
    // Helper: fire the burst, return the probes so tests can construct matching responses.
    private static List<(uint Seq, long T0)> FireBurst(ServerClock clock, int count = 8)
    {
        List<(uint, long)> probes = [];
        for (int i = 0; i < count; i++)
        {
            var probe = clock.Poll();
            if (probe is null) break;
            probes.Add(probe.Value);
        }
        return probes;
    }

    // Helper: construct a response from a real probe with a known offset and one-way delay.
    private static void Complete(ServerClock clock, uint seq, long t0, long offset, long delay)
    {
        long t1 = t0 + delay + offset;
        long t2 = t1;           // server handles instantly
        long t3 = t2 - offset + delay;
        clock.Complete(seq, t0, t1, t2, t3);
    }

    // ---- synchronised state ----

    [Fact]
    public void Not_synchronised_until_a_probe_completes()
    {
        ServerClock clock = new();
        FireBurst(clock);
        Assert.False(clock.Synchronised);
    }

    [Fact]
    public void First_completed_probe_flips_synchronised()
    {
        ServerClock clock = new();
        List<(uint Seq, long T0)> probes = FireBurst(clock);
        Assert.NotEmpty(probes);

        Complete(clock, probes[0].Seq, probes[0].T0, offset: 0, delay: 25);
        Assert.True(clock.Synchronised);
    }

    // ---- validation ----

    [Fact]
    public void Wrong_echoed_T0_is_dropped()
    {
        ServerClock clock = new();
        List<(uint Seq, long T0)> probes = FireBurst(clock);

        // T0 deliberately wrong.
        clock.Complete(probes[0].Seq, t0: 99999, t1: 100, t2: 100, t3: 150);
        Assert.False(clock.Synchronised);
    }

    [Fact]
    public void Unknown_sequence_is_dropped()
    {
        ServerClock clock = new();
        FireBurst(clock);
        clock.Complete(999, t0: 100, t1: 110, t2: 112, t3: 160);
        Assert.False(clock.Synchronised);
    }

    [Fact]
    public void Nonpositive_RTT_is_dropped()
    {
        ServerClock clock = new();
        List<(uint Seq, long T0)> probes = FireBurst(clock);
        // t3-t0 < t2-t1 → negative RTT.
        clock.Complete(probes[0].Seq, probes[0].T0, t1: probes[0].T0 + 120, t2: probes[0].T0 + 140, t3: probes[0].T0 + 5);
        Assert.False(clock.Synchronised);
    }

    // ---- offset arithmetic ----

    [Fact]
    public void Known_offset_is_recovered_from_a_symmetric_probe()
    {
        // offset = 42, one-way delay = 30.
        // t1 = t0 + d + offset, t3 = t2 - offset + d
        // RTT = 2d, offset = ((t1-t0)+(t2-t3))/2 = offset.

        ServerClock clock = new();
        List<(uint Seq, long T0)> probes = FireBurst(clock);

        Complete(clock, probes[0].Seq, probes[0].T0, offset: 42, delay: 30);

        Assert.Equal(42, clock.OffsetMs);
        Assert.Equal(60, clock.RttMedianMs);
    }

    [Fact]
    public void Three_probes_all_carrying_the_same_offset_converge_to_it()
    {
        ServerClock clock = new();
        List<(uint Seq, long T0)> probes = FireBurst(clock);

        Assert.True(probes.Count >= 3);

        // All three probes carry offset=25 with different delays.
        Complete(clock, probes[0].Seq, probes[0].T0, offset: 25, delay: 25);
        Complete(clock, probes[1].Seq, probes[1].T0, offset: 25, delay: 50);
        Complete(clock, probes[2].Seq, probes[2].T0, offset: 25, delay: 100);

        Assert.Equal(25, clock.OffsetMs);
    }

    // ---- poll pacing ----

    [Fact]
    public void Burst_probes_have_distinct_sequences()
    {
        ServerClock clock = new();
        List<(uint Seq, long T0)> probes = FireBurst(clock);

        Assert.Equal(8, probes.Count);
        Assert.Equal(8, probes.Select(p => p.Seq).Distinct().Count());
    }

    [Fact]
    public void Burst_fires_on_every_poll_call_and_then_stops()
    {
        ServerClock clock = new();

        // 8 burst probes fire, one per call.
        for (int i = 0; i < 8; i++)
        {
            Assert.NotNull(clock.Poll());
        }

        // Burst exhausted: Poll returns null until the background interval elapses, which it
        // hasn't in this test.
        Assert.Null(clock.Poll());
    }

    // ---- snapshot flush ----

    [Fact]
    public void First_offset_triggers_snapshot_flush()
    {
        ServerClock clock = new();
        List<(uint Seq, long T0)> probes = FireBurst(clock);

        Complete(clock, probes[0].Seq, probes[0].T0, offset: 10, delay: 20);

        Assert.True(clock.ConsumeSnapshotFlush());
        Assert.False(clock.ConsumeSnapshotFlush());
    }

    // ---- monotonicity ----

    [Fact]
    public void ServerTime_increases_monotonically()
    {
        ServerClock clock = new();
        List<(uint Seq, long T0)> probes = FireBurst(clock);
        Complete(clock, probes[0].Seq, probes[0].T0, offset: 0, delay: 25);

        long a = clock.ServerTimeMs;
        long b = clock.ServerTimeMs;
        Assert.True(b >= a);
    }

    [Fact]
    public void MonotonicNowMs_is_monotonic()
    {
        long a = ServerClock.MonotonicNowMs();
        long b = ServerClock.MonotonicNowMs();
        Assert.True(b >= a);
    }
}
