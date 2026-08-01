using BetaSharp.Network;
using BetaSharp.Network.Messages;
using BetaSharp.Util;

namespace BetaSharp.Tests.Network;

public sealed class TickStampTests
{
    private static TickStampMessage RoundTrip(long serverTimeMs)
    {
        MemoryStream stream = new();
        new TickStampMessage { ServerTimeMs = serverTimeMs }.Write(stream);
        stream.Position = 0;

        TickStampMessage read = new();
        read.Read(stream);
        return read;
    }

    [Fact]
    public void Round_trips_a_timestamp()
    {
        Assert.Equal(1_234_567_890L, RoundTrip(1_234_567_890L).ServerTimeMs);
    }

    [Fact]
    public void Round_trips_a_timestamp_past_the_32_bit_range()
    {
        // MonotonicClock's epoch is arbitrary and its readings are not bounded by int range on a
        // long-lived process. Truncating to int would work for hours and then wrap.
        const long value = 5_000_000_000L;
        Assert.Equal(value, RoundTrip(value).ServerTimeMs);
    }

    [Fact]
    public void Reports_the_size_it_writes()
    {
        MemoryStream stream = new();
        TickStampMessage message = new() { ServerTimeMs = 42 };
        message.Write(stream);

        Assert.Equal(message.Size(), stream.Length);
    }

    /// <summary>
    ///     A stamp that arrives behind a chunk drags the interpolation timeline with it, so it goes
    ///     in the priority queue. Declared by the message rather than by the envelope's packet ID,
    ///     which every message shares.
    /// </summary>
    [Fact]
    public void Is_high_priority()
    {
        Assert.Equal(SendPriority.High, new TickStampMessage().Priority);
    }

    /// <summary>
    ///     The value is the simulation instant, taken before anything moved, not the moment the
    ///     bytes left. Asking the transport to stamp it would substitute the second for the first
    ///     and fold the gap between the two server loops into the timeline.
    /// </summary>
    [Fact]
    public void Does_not_ask_the_transport_for_a_send_timestamp()
    {
        Assert.False(new TickStampMessage().NeedsSendTimestamp);
    }
}

public sealed class MonotonicClockTests
{
    [Fact]
    public void NowMs_does_not_go_backwards()
    {
        long a = MonotonicClock.NowMs();
        long b = MonotonicClock.NowMs();
        Assert.True(b >= a);
    }

    [Fact]
    public void NowTicks_does_not_go_backwards()
    {
        long a = MonotonicClock.NowTicks();
        long b = MonotonicClock.NowTicks();
        Assert.True(b >= a);
    }

    [Fact]
    public void ToMs_and_ElapsedMs_agree_on_the_same_interval()
    {
        // The two conversions must not drift apart: ElapsedMs measures the intervals the histograms
        // record, ToMs produces the stamps that go on the wire, and clock sync compares them.
        long start = MonotonicClock.NowTicks();
        long end = start + System.Diagnostics.Stopwatch.Frequency; // exactly one second

        Assert.Equal(1000.0, MonotonicClock.ElapsedMs(start, end), 3);
        Assert.Equal(1000L, MonotonicClock.ToMs(end) - MonotonicClock.ToMs(start));
    }

    [Fact]
    public void ElapsedMs_of_a_zero_interval_is_zero()
    {
        long t = MonotonicClock.NowTicks();
        Assert.Equal(0.0, MonotonicClock.ElapsedMs(t, t));
    }
}
