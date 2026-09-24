using System.Net;
using OmniBlock.Network;
using OmniBlock.Network.Packets;
using OmniBlock.Network.Transport;
using OmniBlock.Util;

namespace OmniBlock.Tests.Network;

/// <summary>
///     <see cref="UdpConnection" />: packets in and out of datagrams.
///     <para>
///         The substantive change from the stream transport is framing. There was none before — the
///         reader took an id byte and each packet then read exactly the fields it expected, which is
///         why an unknown id killed the connection: nothing knew where the next packet began. A
///         datagram carries its own length, so a bad one costs a datagram instead of a session, and
///         that is what these pin.
///     </para>
/// </summary>
public sealed class UdpConnectionTests
{
    /// <summary>
    ///     A packet whose <c>Apply</c> actually reaches the handler. Most do not — KeepAlive's is an
    ///     empty override — so a received-packet assertion has to be made with one that does, or it
    ///     passes and fails for reasons unrelated to the transport.
    /// </summary>
    private static OmniMessagePacket Observable(int id = 0) => OmniMessagePacket.Get(id, [1, 2, 3]);

    private static (UdpConnection Connection, FakeTransportConnection Transport, RecordingHandler Handler) Fixture()
    {
        FakeTransportConnection transport = new();
        RecordingHandler handler = new();
        UdpConnection connection = new(transport, handler)
        {
            betaSharpClient = true
        };

        return (connection, transport, handler);
    }

    [Fact]
    public void A_sent_packet_becomes_one_datagram()
    {
        var (connection, transport, _) = Fixture();

        connection.sendPacket(Packet.Get(PacketId.Handshake));

        // One datagram, and its first byte is the packet id: the datagram boundary is the framing.
        Assert.Single(transport.Sent);
        Assert.Equal((byte)PacketId.Handshake, transport.Sent[0].Payload[0]);
    }

    [Fact]
    public void A_received_datagram_becomes_one_applied_packet()
    {
        var (connection, transport, handler) = Fixture();

        transport.Deliver(Observable());
        connection.tick();

        Assert.Single(handler.Applied);
        Assert.Equal((byte)PacketId.OmniMessage, handler.Applied[0].Id);
    }

    [Fact]
    public void Several_datagrams_are_applied_in_the_order_they_arrived()
    {
        var (connection, transport, handler) = Fixture();

        for (var i = 0; i < 5; i++)
        {
            transport.Deliver(Observable(i));
        }

        connection.tick();

        Assert.Equal(
            [0, 1, 2, 3, 4],
            handler.Applied.Cast<OmniMessagePacket>().Select(p => p.MessageId));
    }

    /// <summary>
    ///     The property the stream transport could not offer. There, a packet that mis-declared its
    ///     own length left the reader at an unknown offset and the connection was finished; here the
    ///     length came from the transport, so the damage stops at the datagram.
    /// </summary>
    [Fact]
    public void A_malformed_datagram_costs_a_datagram_rather_than_the_connection()
    {
        var (connection, transport, handler) = Fixture();

        transport.Deliver(Observable(1));
        transport.DeliverRaw([0xFF, 0xFF, 0xFF, 0xFF]); // no such packet id
        transport.Deliver(Observable(2));

        connection.tick();

        // Both good datagrams applied, and the one between them cost only itself.
        Assert.Equal([1, 2], handler.Applied.Cast<OmniMessagePacket>().Select(p => p.MessageId));
    }

    /// <summary>
    ///     Arrival time comes from the datagram, taken on the transport's thread. Taking it here
    ///     instead would measure how long ago the tick started — up to a full tick of error in the
    ///     one number clock synchronisation exists to get right.
    /// </summary>
    [Fact]
    public void The_transports_arrival_stamp_reaches_the_message_envelope()
    {
        var (connection, transport, handler) = Fixture();

        var before = MonotonicClock.NowMs();
        transport.Deliver(Observable());
        connection.tick();

        var envelope = Assert.IsType<OmniMessagePacket>(Assert.Single(handler.Applied));

        Assert.True(envelope.ReceivedAtMs >= before, "the envelope was not stamped on arrival");
        Assert.True(envelope.ReceivedAtMs <= MonotonicClock.NowMs());
    }

    /// <summary>
    ///     Stamped inside the send path, later than the stream transport could manage: there is no
    ///     queue of our own between here and the socket, so this really is the send instant.
    /// </summary>
    [Fact]
    public void A_message_that_asks_for_a_send_stamp_gets_one_on_the_way_out()
    {
        var (connection, transport, _) = Fixture();

        var envelope = OmniMessagePacket.Get(0, [], true);
        Assert.Equal(0, envelope.SentAtMs);

        var before = MonotonicClock.NowMs();
        connection.sendPacket(envelope);

        Assert.True(envelope.SentAtMs >= before);
        Assert.Single(transport.Sent);
    }

    [Fact]
    public void A_chunk_sized_packet_goes_out_as_a_single_datagram()
    {
        // Fragmentation is the transport's problem, not this layer's. One packet stays one payload.
        var (connection, transport, _) = Fixture();

        connection.sendPacket(OmniMessagePacket.Get(0, new byte[81_920]));

        Assert.Single(transport.Sent);
        Assert.True(transport.Sent[0].Payload.Length > 81_920);
    }

    [Fact]
    public void Losing_the_transport_disconnects_the_connection()
    {
        var (connection, transport, _) = Fixture();

        transport.IsConnected = false;
        connection.tick();

        connection.sendPacket(Packet.Get(PacketId.Handshake));
        Assert.Empty(transport.Sent);
    }

    [Fact]
    public void Disconnecting_closes_the_transport()
    {
        var (connection, transport, _) = Fixture();

        connection.disconnect("done");

        Assert.Equal(DisconnectReason.Local, transport.ClosedWith);
    }

    [Fact]
    public void Remote_connection_still_times_out_without_packets()
    {
        var (connection, _, handler) = Fixture();

        for (var tick = 0; tick < 1_205; tick++) connection.tick();

        Assert.Equal("disconnect.timeout", handler.DisconnectReason);
    }

    /// <summary>
    ///     Two <see cref="UdpConnection" />s over a real loopback socket. The tests above use a fake
    ///     transport because the questions are about framing and channels; this one exists because
    ///     none of them would catch a packet that serialises fine and then does not survive an actual
    ///     datagram — which is the whole of what the cutover changed.
    /// </summary>
    [Fact]
    public async Task Packets_survive_a_real_datagram_round_trip()
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));

        await using LiteNetLibTransport serverTransport = new();
        serverTransport.Listen(0);

        await using LiteNetLibTransport clientTransport = new();
        clientTransport.StartClient();

        var accepting = FirstAcceptedAsync(serverTransport, cancellation.Token);

        var clientPeer = await clientTransport.ConnectAsync(
            new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort), cancellation.Token);

        RecordingHandler serverHandler = new();
        UdpConnection serverSide = new(await accepting, serverHandler)
        {
            betaSharpClient = true
        };
        UdpConnection clientSide = new(clientPeer, new RecordingHandler())
        {
            betaSharpClient = true
        };

        // A chunk-sized payload alongside small ones: fragmentation and reassembly are the part a
        // fake transport cannot exercise at all.
        clientSide.sendPacket(Observable(1));
        clientSide.sendPacket(OmniMessagePacket.Get(2, new byte[81_920]));
        clientSide.sendPacket(Observable(3));

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (serverHandler.Applied.Count < 3 && DateTime.UtcNow < deadline)
        {
            serverSide.tick();
            await Task.Delay(5, cancellation.Token);
        }

        Assert.Equal([1, 2, 3], serverHandler.Applied.Cast<OmniMessagePacket>().Select(p => p.MessageId));
        Assert.Equal(81_920, serverHandler.Applied.Cast<OmniMessagePacket>().ElementAt(1).Payload.Length);
    }

    private static async Task<ITransportConnection> FirstAcceptedAsync(
        ITransport transport, CancellationToken cancellationToken)
    {
        await foreach (var connection in transport.AcceptAsync(cancellationToken))
        {
            return connection;
        }

        throw new InvalidOperationException("The transport was disposed before a peer connected.");
    }

    private sealed class RecordingHandler : NetHandler
    {
        public List<Packet> Applied { get; } = [];
        public string? DisconnectReason { get; private set; }

        public override bool isServerSide() => true;

        public override void handle(Packet packet) => Applied.Add(packet);

        public override void onOmniMessage(OmniMessagePacket packet) => Applied.Add(packet);

        public override void onDisconnected(string reason, object[]? details) => DisconnectReason = reason;
    }
}
