using BetaSharp.Network;
using BetaSharp.Network.Packets;
using BetaSharp.Util;

namespace BetaSharp.Tests.Network;

/// <summary>
///     The drain that replaced <c>Connection</c>'s fixed cap of 100 packets per tick. At 20 TPS that
///     cap was a ceiling of 2,000 packets a second, which a few hundred tracked entities exceed on
///     their own; past it the read queue grows without bound and the client applies positions from
///     further and further in the past.
/// </summary>
public sealed class DrainBudgetTests
{
    private sealed class CountingHandler : NetHandler
    {
        public override bool isServerSide() => false;
    }

    /// <summary>
    ///     A packet that exists only to be applied. <see cref="BurnMs" /> spins rather than sleeps:
    ///     the budget is measured against the monotonic clock, and a sleep would hand the test's
    ///     timing to the scheduler.
    /// </summary>
    private sealed class CountingPacket(double burnMs) : Packet(PacketId.Handshake)
    {
        public static int Applied;

        private readonly double _burnMs = burnMs;

        public override void Read(Stream stream) { }

        public override void Write(Stream stream) { }

        public override int Size() => 0;

        public override void Apply(NetHandler handler)
        {
            Applied++;

            if (_burnMs <= 0)
            {
                return;
            }

            long start = MonotonicClock.NowTicks();
            while (MonotonicClock.ElapsedMs(start, MonotonicClock.NowTicks()) < _burnMs)
            {
                // Spin.
            }
        }
    }

    private sealed class DrainableConnection : Connection
    {
        public DrainableConnection(int packets, double burnMsEach)
        {
            setNetworkHandler(new CountingHandler());

            for (int i = 0; i < packets; i++)
            {
                readQueue.Enqueue(new CountingPacket(burnMsEach));
            }
        }

        public void Drain() => processPackets();
    }

    /// <summary>
    ///     The regression. Cheap packets cost far less than the budget, so all of them are applied
    ///     in one tick — 500 of them, well past the 100 the old cap allowed.
    /// </summary>
    [Fact]
    public void Cheap_packets_drain_in_one_tick_past_the_old_hundred_packet_cap()
    {
        CountingPacket.Applied = 0;
        DrainableConnection connection = new(500, burnMsEach: 0);

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

        // 1 ms each against a 10 ms budget, checked every 64 packets, so the drain stops on the
        // first check and 512 of the 576 stay queued.
        DrainableConnection connection = new(576, burnMsEach: 1.0);

        connection.Drain();

        Assert.Equal(1, connection.DrainBudgetHits);
        Assert.True(connection.ReadQueueDepth > 0, "the drain should have stopped before the queue emptied");
        Assert.True(
            CountingPacket.Applied < 576,
            $"expected the budget to bind, but all {CountingPacket.Applied} packets were applied");
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
        DrainableConnection connection = new(200, burnMsEach: 1.0);

        connection.Drain();

        Assert.Equal(64, CountingPacket.Applied);
    }
}
