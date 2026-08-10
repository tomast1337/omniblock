using OmniBlock.Network;
using OmniBlock.Network.Packets;

namespace OmniBlock.Tests.Network;

/// <summary>
///     The drain that replaced <c>Connection</c>'s fixed cap of 100 packets per tick. At 20 TPS that
///     cap was a ceiling of 2,000 packets a second, which a few hundred tracked entities exceed on
///     their own; past it the read queue grows without bound and the client applies positions from
///     further and further in the past.
/// </summary>
/// <remarks>
///     Every case here drives the clock rather than reading one. Applying a packet charges the
///     connection a stated number of milliseconds, so what the budget sees is decided by the test
///     and not by how busy the machine is. The version that spun to burn real time asserted the same
///     things and could fail on a loaded machine while the code was correct — the cheap-packet case
///     in particular only held while 500 no-op packets fitted inside 10 ms of wall clock.
/// </remarks>
public sealed class DrainBudgetTests
{
    private sealed class CountingHandler : NetHandler
    {
        public override bool isServerSide() => false;
    }

    /// <summary>A clock that moves only when something says how far.</summary>
    /// <remarks>
    ///     Counts microseconds rather than milliseconds so that a cost below 1 ms is a cost and not a
    ///     rounding down to nothing — the cases that have to stay under the budget charge fractions
    ///     of a millisecond, and at millisecond resolution they would charge zero and pass for the
    ///     wrong reason.
    /// </remarks>
    private sealed class ManualClock : TimeProvider
    {
        private long _microseconds;

        public override long TimestampFrequency => 1_000_000;

        public override long GetTimestamp() => _microseconds;

        public void Advance(double milliseconds) => _microseconds += (long)(milliseconds * 1000.0);
    }

    /// <summary>A packet that exists to be counted, and to charge the clock for having been applied.</summary>
    private sealed class CountingPacket(ManualClock clock, double costMs) : Packet(PacketId.Handshake)
    {
        public static int Applied;

        public override void Read(Stream stream) { }

        public override void Write(Stream stream) { }

        public override int Size() => 0;

        public override void Apply(NetHandler handler)
        {
            Applied++;
            clock.Advance(costMs);
        }
    }

    private sealed class DrainableConnection : Connection
    {
        public DrainableConnection(ManualClock clock, int packets, double costMsEach) : base(clock: clock)
        {
            setNetworkHandler(new CountingHandler());

            for (int i = 0; i < packets; i++)
            {
                readQueue.Enqueue(new CountingPacket(clock, costMsEach));
            }
        }

        public void Drain() => processPackets();
    }

    /// <summary>
    ///     The regression. Free packets never reach the budget, so all of them are applied in one
    ///     tick — 500 of them, well past the 100 the old cap allowed.
    /// </summary>
    [Fact]
    public void Cheap_packets_drain_in_one_tick_past_the_old_hundred_packet_cap()
    {
        CountingPacket.Applied = 0;
        ManualClock clock = new();
        DrainableConnection connection = new(clock, 500, costMsEach: 0);

        connection.Drain();

        Assert.Equal(500, CountingPacket.Applied);
        Assert.Equal(500, connection.PacketsProcessed);
        Assert.Equal(0, connection.ReadQueueDepth);
        Assert.Equal(0, connection.DrainBudgetHits);
    }

    /// <summary>
    ///     The other half: the budget still bounds the tick. Enough expensive packets to overrun it
    ///     stop the drain early and leave the rest queued, rather than running the tick long.
    /// </summary>
    [Fact]
    public void Expensive_packets_stop_at_the_budget_and_leave_the_rest_queued()
    {
        CountingPacket.Applied = 0;
        ManualClock clock = new();

        // 1 ms each against a 10 ms budget, checked every 64 packets, so the drain stops on the
        // first check and 512 of the 576 stay queued.
        DrainableConnection connection = new(clock, 576, costMsEach: 1.0);

        connection.Drain();

        Assert.Equal(1, connection.DrainBudgetHits);
        Assert.Equal(64, CountingPacket.Applied);
        Assert.Equal(512, connection.ReadQueueDepth);
    }

    /// <summary>
    ///     Cheap-but-not-free packets drain in full, across more than one budget check. The case
    ///     above stops on its first check with the same packet count, so the two differ only in what
    ///     a packet costs — which is what makes this the line the budget is supposed to sit on
    ///     rather than a restatement of the count.
    /// </summary>
    [Fact]
    public void Packets_that_fit_inside_the_budget_all_drain()
    {
        CountingPacket.Applied = 0;
        ManualClock clock = new();

        // 0.05 ms each over two intervals of 64 reaches 6.4 ms, short of the 10 ms budget at both
        // checks, so nothing stops the drain and nothing is charged as a hit.
        DrainableConnection connection = new(clock, 128, costMsEach: 0.05);

        connection.Drain();

        Assert.Equal(128, CountingPacket.Applied);
        Assert.Equal(0, connection.ReadQueueDepth);
        Assert.Equal(0, connection.DrainBudgetHits);
    }

    /// <summary>
    ///     Forward progress is guaranteed even when the budget is already blown on entry, because
    ///     the check runs every 64 packets rather than before each one. Without that floor an
    ///     expensive enough packet stream could leave the queue permanently stuck.
    /// </summary>
    [Fact]
    public void The_drain_always_makes_progress_even_when_every_packet_overruns_the_budget()
    {
        CountingPacket.Applied = 0;
        ManualClock clock = new();
        DrainableConnection connection = new(clock, 200, costMsEach: 1000.0);

        connection.Drain();

        Assert.Equal(64, CountingPacket.Applied);
    }
}
