using BetaSharp.Network.Packets;
using BetaSharp.Network.Packets.S2CPlay;
using BetaSharp.Util;

namespace BetaSharp.Tests.Network;

public sealed class TickStampTests
{
    private static TickStampS2CPacket RoundTrip(long serverTimeMs)
    {
        MemoryStream stream = new();
        TickStampS2CPacket.Get(serverTimeMs).Write(stream);
        stream.Position = 0;

        TickStampS2CPacket read = new();
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
        TickStampS2CPacket packet = TickStampS2CPacket.Get(42);
        packet.Write(stream);

        Assert.Equal(packet.Size(), stream.Length);
    }

    [Fact]
    public void Is_an_extended_protocol_packet()
    {
        // Gates it behind the OmniBlock client check, so a vanilla client never receives one.
        Assert.IsAssignableFrom<ExtendedProtocolPacket>(TickStampS2CPacket.Get(0));
    }

    [Fact]
    public void Is_registered_under_its_id()
    {
        Assert.Equal((byte)PacketId.TickStamp, TickStampS2CPacket.Get(0).Id);
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
