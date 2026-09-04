using OmniBlock.Network;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;

namespace OmniBlock.Tests.Network;

/// <summary>
///     How a peer declares that it speaks the OmniBlock protocol.
///     <para>
///         This replaced a bare magic constant compared for equality, which said only "capable" and
///         said it in a field that means something else with no note that it did. The properties
///         worth pinning are that a vanilla peer is still connectable, and that the declaration now
///         carries a revision in both directions.
///     </para>
/// </summary>
public sealed class ProtocolHandshakeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(42)]
    [InlineData(int.MaxValue)]
    public void A_declaration_round_trips_its_version(int version)
    {
        Assert.True(ProtocolHandshake.TryDecode(ProtocolHandshake.Encode(version), out var decoded));
        Assert.Equal(version, decoded);
    }

    /// <summary>
    ///     The compatibility property, and the reason the declaration hides in a login field at all.
    ///     A vanilla client sends zero here; misreading that as a declaration would push packet IDs
    ///     at it that it cannot parse, and the unframed protocol has no way to recover.
    /// </summary>
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(0x627368617270L)] // the constant this replaced
    public void A_peer_that_declares_nothing_is_not_mistaken_for_a_capable_one(long seed)
    {
        Assert.False(ProtocolHandshake.TryDecode(seed, out var version));
        Assert.Equal(0, version);
    }

    /// <summary>
    ///     The declaration survives the login packet's own serialisation, which is the path it
    ///     actually takes. Encoding it correctly and then truncating it on the wire would look
    ///     identical to a vanilla client at the far end.
    /// </summary>
    [Fact]
    public void The_declaration_survives_the_login_packet()
    {
        MemoryStream stream = new();
        Packet.Write(
            LoginHelloPacket.Get("someone", 14, ProtocolHandshake.Encode(ProtocolHandshake.Version), 0),
            stream);
        stream.Position = 0;

        var received = Assert.IsType<LoginHelloPacket>(Packet.Read(stream, true));

        Assert.True(ProtocolHandshake.TryDecode(received.WorldSeed, out var version));
        Assert.Equal(ProtocolHandshake.Version, version);
    }

    [Fact]
    public void A_declaration_marks_the_connection_capable_and_records_the_version()
    {
        var connection = TestConnection();
        Assert.False(connection.betaSharpClient);
        Assert.Equal(0, connection.PeerProtocolVersion);

        connection.NotePeerProtocol(7);

        Assert.True(connection.betaSharpClient);
        Assert.Equal(7, connection.PeerProtocolVersion);
    }

    /// <summary>
    ///     Capability inferred from receiving an extended packet carries no version, so zero has to
    ///     mean "capable, revision unknown" rather than "not capable". Reading it as the latter would
    ///     undo the gate.
    /// </summary>
    [Fact]
    public void Inferred_capability_leaves_the_version_unknown_without_clearing_capability()
    {
        var connection = TestConnection();

        connection.NotePeerCapability(OmniMessagePacket.Get(0, []));

        Assert.True(connection.betaSharpClient);
        Assert.Equal(0, connection.PeerProtocolVersion);
    }

    /// <summary>
    ///     The server's half of the loop. It rides on the registry sync because that is the first
    ///     extended packet the server sends, so it is the earliest the client can be told.
    /// </summary>
    [Fact]
    public void The_registry_sync_carries_the_servers_version()
    {
        MessageRegistry server = new();
        DefaultMessages.RegisterAll(server, ContentRuntime.Current.Items);

        MemoryStream stream = new();
        Packet.Write(MessageRegistrySyncS2CPacket.Get(server.NegotiateAsServer(), ContentRuntime.Current.Manifest), stream);
        stream.Position = 0;

        var received =
            Assert.IsType<MessageRegistrySyncS2CPacket>(Packet.Read(stream, false));

        Assert.Equal(ProtocolHandshake.Version, received.ProtocolVersion);
        Assert.Equal(server.NegotiatedOrder, received.Keys);
        Assert.Equal(ContentRuntime.Current.Manifest.Fingerprint, received.CatalogFingerprint);
    }

    /// <summary>A connection over a transport that goes nowhere.</summary>
    private static UdpConnection TestConnection() => new(new FakeTransportConnection());
}
