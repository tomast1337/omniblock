using OmniBlock;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;

namespace OmniBlock.Tests.Network;

/// <summary>
///     The message layer's foundation: length-prefixed envelopes and registry-negotiated IDs.
///     <para>
///         The properties under test are the two the legacy <c>PacketId : byte</c> framing cannot
///         provide — that an unknown message is survivable, and that two peers derive the same ID
///         table without either one picking a number.
///     </para>
/// </summary>
public sealed class MessageLayerTests
{
    private static readonly ResourceLocation s_alpha = ResourceLocation.Parse("omniblock:alpha");
    private static readonly ResourceLocation s_beta = ResourceLocation.Parse("omniblock:beta");
    private static readonly ResourceLocation s_modded = ResourceLocation.Parse("somemod:custom");

    // ---------------------------------------------------------------------------------------
    // VarInt
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void VarInt_round_trips(int value)
    {
        MemoryStream stream = new();
        stream.WriteVarInt(value);
        stream.Position = 0;

        Assert.Equal(value, stream.ReadVarInt());
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(127, 1)]
    [InlineData(128, 2)]
    [InlineData(16383, 2)]
    [InlineData(16384, 3)]
    [InlineData(int.MaxValue, 5)]
    [InlineData(-1, 5)]
    public void VarIntSize_matches_bytes_written(int value, int expected)
    {
        MemoryStream stream = new();
        stream.WriteVarInt(value);

        Assert.Equal(expected, stream.Length);
        Assert.Equal(expected, StreamExtensions.VarIntSize(value));
    }

    [Fact]
    public void VarInt_rejects_an_overlong_encoding()
    {
        // A peer sending continuation bytes forever would otherwise spin the reader.
        MemoryStream stream = new([0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01]);

        Assert.Throws<InvalidDataException>(() => stream.ReadVarInt());
    }

    [Fact]
    public void VarInt_rejects_a_truncated_encoding()
    {
        MemoryStream stream = new([0x80]);

        Assert.Throws<EndOfStreamException>(() => stream.ReadVarInt());
    }

    // ---------------------------------------------------------------------------------------
    // Negotiation
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Registration_order_does_not_affect_the_negotiated_table()
    {
        // The property that makes mods safe: two machines loading the same content in a different
        // order must still agree on every ID.
        MessageRegistry first = new();
        first.Register(s_beta, 1, () => new StubMessage(s_beta));
        first.Register(s_modded, 1, () => new StubMessage(s_modded));
        first.Register(s_alpha, 1, () => new StubMessage(s_alpha));

        MessageRegistry second = new();
        second.Register(s_modded, 1, () => new StubMessage(s_modded));
        second.Register(s_alpha, 1, () => new StubMessage(s_alpha));
        second.Register(s_beta, 1, () => new StubMessage(s_beta));

        Assert.Equal(first.NegotiateAsServer(), second.NegotiateAsServer());
    }

    [Fact]
    public void Client_adopts_the_server_ordering()
    {
        MessageRegistry server = new();
        server.Register(s_alpha, 1, () => new StubMessage(s_alpha));
        server.Register(s_beta, 1, () => new StubMessage(s_beta));

        MessageRegistry client = new();
        client.Register(s_beta, 1, () => new StubMessage(s_beta));
        client.Register(s_alpha, 1, () => new StubMessage(s_alpha));

        client.AdoptOrdering(server.NegotiateAsServer());

        Assert.Equal(server.GetId(s_alpha), client.GetId(s_alpha));
        Assert.Equal(server.GetId(s_beta), client.GetId(s_beta));
    }

    [Fact]
    public void A_key_the_client_does_not_know_leaves_a_hole_without_shifting_other_ids()
    {
        // The alignment property. A client missing one mod must still decode every other message,
        // which means the unknown key keeps its slot rather than collapsing the table.
        MessageRegistry server = new();
        server.Register(s_alpha, 1, () => new StubMessage(s_alpha));
        server.Register(s_modded, 1, () => new StubMessage(s_modded));
        server.Register(s_beta, 1, () => new StubMessage(s_beta));

        MessageRegistry client = new();
        client.Register(s_alpha, 1, () => new StubMessage(s_alpha));
        client.Register(s_beta, 1, () => new StubMessage(s_beta));

        client.AdoptOrdering(server.NegotiateAsServer());

        Assert.Equal(server.GetId(s_beta), client.GetId(s_beta));
        Assert.Null(client.Create(server.GetId(s_modded)));
        Assert.NotNull(client.Create(server.GetId(s_beta)));
    }

    [Fact]
    public void Registering_a_duplicate_key_throws()
    {
        MessageRegistry registry = new();
        registry.Register(s_alpha, 1, () => new StubMessage(s_alpha));

        Assert.Throws<InvalidOperationException>(
            () => registry.Register(s_alpha, 1, () => new StubMessage(s_alpha)));
    }

    [Fact]
    public void Reading_the_negotiated_order_before_negotiating_throws()
    {
        // The server advertises NegotiatedOrder during configuration. Reading it early would send a
        // table that a later registration could contradict, so it fails loudly instead.
        MessageRegistry registry = new();
        registry.Register(s_alpha, 1, () => new StubMessage(s_alpha));

        Assert.Throws<InvalidOperationException>(() => registry.NegotiatedOrder);

        registry.NegotiateAsServer();

        Assert.Equal([s_alpha], registry.NegotiatedOrder);
    }

    [Fact]
    public void An_empty_table_negotiates_cleanly()
    {
        // A server with no messages registered at all is a legitimate configuration. The wiring has
        // to work end to end rather than only becoming correct once content arrives.
        MessageRegistry server = new();
        MessageRegistry client = new();

        client.AdoptOrdering(server.NegotiateAsServer());

        Assert.True(client.Negotiated);
        Assert.Equal(0, client.Count);
        Assert.Null(client.Create(0));
    }

    [Fact]
    public void Registering_after_negotiation_throws()
    {
        // The peer already holds the table; a late registration would desynchronise it silently.
        MessageRegistry registry = new();
        registry.Register(s_alpha, 1, () => new StubMessage(s_alpha));
        registry.NegotiateAsServer();

        Assert.Throws<InvalidOperationException>(
            () => registry.Register(s_beta, 1, () => new StubMessage(s_beta)));
    }

    // ---------------------------------------------------------------------------------------
    // Envelope
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Envelope_round_trips_through_the_packet_framing()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        OmniMessagePacket sent = OmniMessagePacket.Get(7, payload);

        MemoryStream stream = new();
        Packet.Write(sent, stream);
        stream.Position = 0;

        Packet? received = Packet.Read(stream, server: true);

        OmniMessagePacket envelope = Assert.IsType<OmniMessagePacket>(received);
        Assert.Equal(7, envelope.MessageId);
        Assert.Equal(payload, envelope.Payload);
    }

    [Fact]
    public void Size_matches_the_bytes_actually_written()
    {
        // Size() and Write() disagreeing is the classic hand-rolled-serialiser bug, and here it
        // would corrupt byte accounting rather than the stream, making it hard to notice.
        OmniMessagePacket packet = OmniMessagePacket.Get(300, new byte[1000]);

        MemoryStream stream = new();
        packet.Write(stream);

        Assert.Equal(stream.Length, packet.Size());
    }

    [Fact]
    public void An_unknown_message_leaves_the_stream_aligned_for_the_next_packet()
    {
        // The whole point of length-prefixing. Under the legacy framing an unrecognised ID means the
        // reader cannot find the next packet boundary and the connection is dead.
        MemoryStream stream = new();
        Packet.Write(OmniMessagePacket.Get(9999, [0xDE, 0xAD, 0xBE, 0xEF]), stream);
        Packet.Write(OmniMessagePacket.Get(1, [0x42]), stream);
        stream.Position = 0;

        OmniMessagePacket unknown = Assert.IsType<OmniMessagePacket>(Packet.Read(stream, server: true));
        Assert.Equal(9999, unknown.MessageId);

        OmniMessagePacket next = Assert.IsType<OmniMessagePacket>(Packet.Read(stream, server: true));
        Assert.Equal(1, next.MessageId);
        Assert.Equal([0x42], next.Payload);
    }

    [Fact]
    public void An_oversized_declared_payload_is_refused_without_allocating_it()
    {
        MemoryStream stream = new();
        stream.WriteVarInt(1);
        stream.WriteByte(0); // flags: no send timestamp
        stream.WriteVarInt(OmniMessagePacket.MaxPayloadBytes + 1);
        stream.Position = 0;

        Assert.Throws<InvalidDataException>(() => new OmniMessagePacket().Read(stream));
    }

    // ---------------------------------------------------------------------------------------
    // Registry sync packet
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Registry_sync_round_trips_and_preserves_order()
    {
        MessageRegistry server = new();
        server.Register(s_beta, 1, () => new StubMessage(s_beta));
        server.Register(s_modded, 1, () => new StubMessage(s_modded));
        server.Register(s_alpha, 1, () => new StubMessage(s_alpha));

        MessageRegistrySyncS2CPacket sent = MessageRegistrySyncS2CPacket.Get(server.NegotiateAsServer());

        MemoryStream stream = new();
        Packet.Write(sent, stream);
        stream.Position = 0;

        Packet? received = Packet.Read(stream, server: false);

        MessageRegistrySyncS2CPacket sync = Assert.IsType<MessageRegistrySyncS2CPacket>(received);
        Assert.Equal(sent.Keys, sync.Keys);
    }

    [Fact]
    public void Registry_sync_size_matches_the_bytes_written()
    {
        MessageRegistrySyncS2CPacket packet = MessageRegistrySyncS2CPacket.Get([s_alpha, s_beta, s_modded]);

        MemoryStream stream = new();
        packet.Write(stream);

        Assert.Equal(stream.Length, packet.Size());
    }

    [Fact]
    public void A_client_can_decode_a_message_the_server_sent_end_to_end()
    {
        MessageRegistry server = new();
        server.Register(s_alpha, 1, () => new StubMessage(s_alpha));
        server.Register(s_beta, 1, () => new StubMessage(s_beta));

        MessageRegistry client = new();
        client.Register(s_alpha, 1, () => new StubMessage(s_alpha));
        client.Register(s_beta, 1, () => new StubMessage(s_beta));
        client.AdoptOrdering(server.NegotiateAsServer());

        StubMessage outgoing = new(s_beta) { Value = 0xBEEF };
        MemoryStream body = new();
        outgoing.Write(body);

        OmniMessagePacket envelope = OmniMessagePacket.Get(server.GetId(s_beta), body.ToArray());

        MemoryStream wire = new();
        Packet.Write(envelope, wire);
        wire.Position = 0;

        OmniMessagePacket received = Assert.IsType<OmniMessagePacket>(Packet.Read(wire, server: false));
        Message? decoded = client.Create(received.MessageId);

        StubMessage stub = Assert.IsType<StubMessage>(decoded);
        stub.Read(new MemoryStream(received.Payload));

        Assert.Equal(s_beta, stub.Key);
        Assert.Equal(0xBEEF, stub.Value);
    }

    private sealed class StubMessage(ResourceLocation key) : Message
    {
        public override ResourceLocation Key => key;

        public int Value { get; set; }

        public override void Read(Stream stream) => Value = stream.ReadInt();

        public override void Write(Stream stream) => stream.WriteInt(Value);

        public override int Size() => sizeof(int);
    }
}
