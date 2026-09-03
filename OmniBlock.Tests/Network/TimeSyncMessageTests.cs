using OmniBlock.Network;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;

namespace OmniBlock.Tests.Network;

/// <summary>
///     The time-sync trio as extensible-layer messages rather than as three <c>PacketId</c> slots.
///     <para>
///         These are the first real users of the message layer, so the tests here cover the layer
///         itself as much as the messages: negotiation between two independently built registries,
///         the envelope round trip, and the transport timestamps that used to be stamped by a
///         type switch inside <c>Connection</c>.
///     </para>
/// </summary>
public sealed class TimeSyncMessageTests
{
    private static (MessageRegistry Server, MessageRegistry Client) NegotiatedPair()
    {
        MessageRegistry server = new();
        DefaultMessages.RegisterAll(server, ContentRuntime.Current.Items);

        MessageRegistry client = new();
        DefaultMessages.RegisterAll(client, ContentRuntime.Current.Items);
        client.AdoptOrdering(server.NegotiateAsServer());

        return (server, client);
    }

    /// <summary>
    ///     Decodes an envelope the way <c>NetHandler.onOmniMessage</c> does, including the transfer
    ///     of the envelope's transport timestamps onto the message.
    /// </summary>
    private static Message? Decode(MessageRegistry registry, OmniMessagePacket envelope)
    {
        Message? message = registry.Create(envelope.MessageId);
        if (message is null)
        {
            return null;
        }

        message.TransportSentAtMs = envelope.SentAtMs;
        message.TransportReceivedAtMs = envelope.ReceivedAtMs;

        using MemoryStream payload = new(envelope.Payload, writable: false);
        message.Read(payload);
        return message;
    }

    /// <summary>Serialises an envelope and reads it back, as the socket would.</summary>
    private static OmniMessagePacket OverTheWire(OmniMessagePacket sent)
    {
        MemoryStream stream = new();
        sent.Write(stream);
        Assert.Equal(sent.Size(), stream.Length);

        stream.Position = 0;
        OmniMessagePacket received = new();
        received.Read(stream);
        return received;
    }

    /// <summary>
    ///     Both peers build their table from <see cref="DefaultMessages" />, so every key the server
    ///     advertises resolves on the client. A hole here is silent at runtime — the message is
    ///     simply dropped — which is why it is asserted rather than left to be noticed.
    /// </summary>
    [Fact]
    public void Both_peers_resolve_every_default_message()
    {
        (MessageRegistry server, MessageRegistry client) = NegotiatedPair();

        // The keys, not a count. A count has to be edited every time a message is added, which
        // trains you to edit it without looking — and the thing worth catching is one of these
        // quietly dropped from DefaultMessages, which a count would not distinguish from a swap.
        Assert.Contains(TimeSyncRequestMessage.Id, server.NegotiatedOrder);
        Assert.Contains(TimeSyncResponseMessage.Id, server.NegotiatedOrder);
        Assert.Contains(TickStampMessage.Id, server.NegotiatedOrder);
        Assert.Contains(ChunkDataMessage.Id, server.NegotiatedOrder);
        Assert.Contains(ChunkCacheOfferMessage.Id, server.NegotiatedOrder);
        Assert.Contains(ChunkUnchangedMessage.Id, server.NegotiatedOrder);

        foreach (ResourceLocation key in server.NegotiatedOrder)
        {
            Assert.NotNull(client.Create(client.GetId(key)));
        }
    }

    [Fact]
    public void A_request_survives_the_envelope_and_the_wire()
    {
        (MessageRegistry server, MessageRegistry client) = NegotiatedPair();

        OmniMessagePacket? sent = OmniMessagePacket.For(
            client, new TimeSyncRequestMessage { Sequence = 7, ClientSendTime = 1234 });

        TimeSyncRequestMessage received = Assert.IsType<TimeSyncRequestMessage>(
            Decode(server, OverTheWire(Assert.IsType<OmniMessagePacket>(sent))));

        Assert.Equal(7u, received.Sequence);
        Assert.Equal(1234, received.ClientSendTime);
    }

    /// <summary>
    ///     T2 is not a payload field. It has to be taken inside the write path, after the payload is
    ///     already serialised, so it rides on the envelope and arrives as
    ///     <see cref="Message.TransportSentAtMs" />.
    /// </summary>
    [Fact]
    public void A_response_carries_its_send_timestamp_on_the_envelope()
    {
        (MessageRegistry server, MessageRegistry client) = NegotiatedPair();

        OmniMessagePacket sent = Assert.IsType<OmniMessagePacket>(OmniMessagePacket.For(
            server, new TimeSyncResponseMessage { Sequence = 3, ClientSendTime = 10, ServerRecvTime = 20 }));

        Assert.True(sent.CarriesSendTime);
        Assert.Equal(0, sent.SentAtMs);

        // What Connection.WritePacket does immediately before the bytes reach the socket.
        sent.SentAtMs = 30;

        OmniMessagePacket received = OverTheWire(sent);

        // And what Connection.Reading does on the read thread, before queueing.
        received.ReceivedAtMs = 40;

        TimeSyncResponseMessage message = Assert.IsType<TimeSyncResponseMessage>(Decode(client, received));

        Assert.Equal(3u, message.Sequence);
        Assert.Equal(10, message.ClientSendTime);
        Assert.Equal(20, message.ServerRecvTime);
        Assert.Equal(30, message.TransportSentAtMs);
        Assert.Equal(40, message.TransportReceivedAtMs);
    }

    /// <summary>
    ///     A message that did not ask for a send timestamp pays one flag byte, not nine. The flag
    ///     exists so the common case is not charged for the rare one.
    /// </summary>
    [Fact]
    public void An_envelope_without_a_send_timestamp_is_eight_bytes_smaller()
    {
        (MessageRegistry server, _) = NegotiatedPair();

        OmniMessagePacket stamp = Assert.IsType<OmniMessagePacket>(
            OmniMessagePacket.For(server, new TickStampMessage { ServerTimeMs = 99 }));

        Assert.False(stamp.CarriesSendTime);

        OmniMessagePacket timed = Assert.IsType<OmniMessagePacket>(
            OmniMessagePacket.For(server, new TimeSyncResponseMessage()));

        // Both wrap the same eight-byte-plus payload difference aside, the gap is the timestamp.
        Assert.Equal(sizeof(long), timed.Size() - stamp.Size() - (timed.Payload.Length - stamp.Payload.Length));
    }

    [Fact]
    public void A_tick_stamp_survives_the_envelope_and_the_wire()
    {
        (MessageRegistry server, MessageRegistry client) = NegotiatedPair();

        OmniMessagePacket sent = Assert.IsType<OmniMessagePacket>(
            OmniMessagePacket.For(server, new TickStampMessage { ServerTimeMs = 5_000_000_000L }));

        TickStampMessage received = Assert.IsType<TickStampMessage>(Decode(client, OverTheWire(sent)));

        Assert.Equal(5_000_000_000L, received.ServerTimeMs);
    }

    /// <summary>
    ///     A peer that does not advertise a key gets nothing rather than a wrong ID. This is the
    ///     per-type degradation the layer exists for — one feature goes quiet instead of the
    ///     connection failing.
    /// </summary>
    [Fact]
    public void A_message_the_peer_never_advertised_is_not_sent()
    {
        MessageRegistry client = new();
        DefaultMessages.RegisterAll(client, ContentRuntime.Current.Items);

        // A server that advertises only one of the three.
        MessageRegistry server = new();
        server.Register(TickStampMessage.Id, 1, () => new TickStampMessage());
        client.AdoptOrdering(server.NegotiateAsServer());

        Assert.Null(OmniMessagePacket.For(client, new TimeSyncRequestMessage()));
        Assert.NotNull(OmniMessagePacket.For(client, new TickStampMessage()));
    }

    /// <summary>
    ///     The IDs the three messages used to occupy are gone from the enum, which is the point of
    ///     the migration: adding a message must not spend a slot.
    /// </summary>
    [Fact]
    public void The_migrated_packet_ids_are_no_longer_registered()
    {
        Assert.False(Packet.Registry.TryGet(242, out _));
        Assert.False(Packet.Registry.TryGet(243, out _));
        Assert.False(Packet.Registry.TryGet(244, out _));
    }
}
