using OmniBlock.Network;
using OmniBlock.Network.Transport;
using OmniBlock.Server;

namespace OmniBlock.Tests.Network;

/// <summary>
///     <see cref="ChunkSendPacer" />: how much chunk data one player is handed per tick.
///     <para>
///         The controller closes on the transport's own queue depth rather than on a bandwidth
///         estimate, so these are about the two bounds and about
///         what happens when a chunk is refused — not about throughput, which is the link's business.
///     </para>
/// </summary>
public sealed class ChunkSendPacerTests
{
    /// <summary>A typical compressed chunk, from the 200-chunk measurement.</summary>
    private const int ChunkBytes = 1966;

    [Fact]
    public void An_idle_transport_is_not_a_reason_to_hold_back()
    {
        ChunkSendPacer pacer = new();
        pacer.BeginTick();

        Assert.True(pacer.CanSend(pendingPackets: 0));
        Assert.False(pacer.Backpressured);
    }

    /// <summary>
    ///     The whole point: a transport that is behind stops chunk streaming, because everything
    ///     queued behind a chunk waits for it.
    /// </summary>
    [Fact]
    public void A_backed_up_transport_stops_chunks()
    {
        ChunkSendPacer pacer = new();
        pacer.BeginTick();

        Assert.False(pacer.CanSend(ChunkSendPacer.MaxPendingPackets));
        Assert.True(pacer.Backpressured);
    }

    /// <summary>
    ///     The bound that matters on the first tick of a join, when the queue has nothing to report
    ///     yet. Without it a view distance of 32 hands over 4,225 chunks before the depth signal
    ///     exists, and every later decision is moot.
    /// </summary>
    [Fact]
    public void One_tick_cannot_hand_over_everything_however_idle_the_link_looks()
    {
        ChunkSendPacer pacer = new();
        pacer.BeginTick();

        int sent = 0;
        while (pacer.CanSend(pendingPackets: 0))
        {
            pacer.Record(ChunkBytes);
            sent++;
        }

        Assert.True(sent > 0, "the pacer refused the very first chunk");
        Assert.True(
            sent * ChunkBytes <= ChunkSendPacer.MaxBytesPerTick + ChunkBytes,
            $"{sent} chunks is {sent * ChunkBytes} bytes against a {ChunkSendPacer.MaxBytesPerTick} cap");

        // Far below a full view distance, which is the property being bought.
        Assert.True(sent < 100, $"{sent} chunks in one tick is not pacing");
    }

    [Fact]
    public void The_byte_budget_refills_each_tick()
    {
        ChunkSendPacer pacer = new();

        pacer.BeginTick();
        pacer.Record(ChunkSendPacer.MaxBytesPerTick);
        Assert.False(pacer.CanSend(pendingPackets: 0));

        pacer.BeginTick();
        Assert.True(pacer.CanSend(pendingPackets: 0));
    }

    /// <summary>
    ///     A cached chunk costs eight bytes, not two thousand, so it must barely touch the budget.
    ///     Charging chunks by count rather than by size would make a fully-cached rejoin as slow as a
    ///     first visit for no reason.
    /// </summary>
    [Fact]
    public void Chunks_the_peer_already_had_barely_spend_the_budget()
    {
        ChunkSendPacer pacer = new();
        pacer.BeginTick();

        int sent = 0;
        while (pacer.CanSend(pendingPackets: 0) && sent < 100_000)
        {
            pacer.Record(8);   // ChunkUnchangedMessage
            sent++;
        }

        Assert.True(sent > 1000, $"only {sent} cached chunks per tick, which is throttling nothing");
    }

    [Fact]
    public void Bytes_sent_are_reported_for_the_tick_that_ended()
    {
        ChunkSendPacer pacer = new();

        pacer.BeginTick();
        pacer.Record(1000);
        pacer.Record(500);

        pacer.BeginTick();
        Assert.Equal(1500, pacer.BytesLastTick);
    }

    /// <summary>
    ///     The regression that made a pacer necessary. <c>getWorldPacketBacklog</c> returned zero
    ///     unconditionally from the UDP cutover onward — the stream transport's send queue had gone
    ///     and the accessor was stubbed rather than re-pointed — so every caller pacing against it
    ///     silently stopped pacing.
    /// </summary>
    [Fact]
    public void The_connection_reports_the_transports_real_queue_depth()
    {
        FakeTransportConnection transport = new() { Pending = 17 };
        UdpConnection connection = new(transport);

        Assert.Equal(17, connection.getWorldPacketBacklog());

        transport.Pending = 0;
        Assert.Equal(0, connection.getWorldPacketBacklog());
    }

    /// <summary>
    ///     Loopback has no queue to report and must not be paced: there is no wire to be ahead of.
    /// </summary>
    [Fact]
    public void Loopback_reports_no_backlog_and_is_never_paced()
    {
        InternalConnection connection = new(null, "test");

        Assert.Equal(0, connection.getWorldPacketBacklog());

        ChunkSendPacer pacer = new();
        pacer.BeginTick();
        Assert.True(pacer.CanSend(connection.getWorldPacketBacklog()));
    }
}
