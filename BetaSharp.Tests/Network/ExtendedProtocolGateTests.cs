using BetaSharp.Network;
using BetaSharp.Network.Messages;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Packets.Play;

namespace BetaSharp.Tests.Network;

/// <summary>
///     The gate in <c>Connection.sendPacket</c> that keeps extended packets away from a vanilla
///     peer. It shipped silently one-directional: only the server ever set
///     <c>betaSharpClient</c>, so the client discarded every extended packet it tried to send and
///     the clock could never synchronise. Nothing in the suite exercised the send path, so nothing
///     caught it.
/// </summary>
public sealed class ExtendedProtocolGateTests
{
    /// <summary>An extended packet, standing in for whatever the peer happens to send first.</summary>
    private static OmniMessagePacket Extended(int messageId = 0) => OmniMessagePacket.Get(messageId, []);

    private sealed class Fixture
    {
        public FakeTransportConnection Transport { get; } = new();

        public UdpConnection Connection { get; }

        public int SentCount => Transport.Sent.Count;

        public Fixture() => Connection = new UdpConnection(Transport);
    }

    [Fact]
    public void Extended_packets_are_dropped_while_the_peer_is_unknown()
    {
        Fixture fixture = new();
        fixture.Connection.sendPacket(Extended());

        Assert.Equal(0, fixture.SentCount);
    }

    [Fact]
    public void Extended_packets_are_sent_once_the_peer_is_known()
    {
        Fixture fixture = new();
        fixture.Connection.betaSharpClient = true;
        fixture.Connection.sendPacket(Extended());

        Assert.Equal(1, fixture.SentCount);
    }

    [Fact]
    public void Ordinary_packets_are_never_gated()
    {
        // The gate must not touch the vanilla protocol: a plain packet goes out regardless.
        Fixture fixture = new();
        fixture.Connection.sendPacket(KeepAlivePacket.Get());

        Assert.Equal(1, fixture.SentCount);
    }

    // ---- the capability signal ----

    [Fact]
    public void Receiving_an_extended_packet_marks_the_peer_capable()
    {
        // The actual regression. A client that never sets this discards its own probes forever, and
        // the symptom is a clock stuck on "Synchronising..." with a healthy connection underneath.
        Fixture fixture = new();
        Assert.False(fixture.Connection.betaSharpClient);

        fixture.Connection.NotePeerCapability(Extended());

        Assert.True(fixture.Connection.betaSharpClient);
    }

    [Fact]
    public void A_client_can_reply_after_the_server_sends_its_first_extended_packet()
    {
        // End to end in one direction: silent before the server identifies itself, sending after.
        Fixture fixture = new();

        fixture.Connection.sendPacket(Extended());
        Assert.Equal(0, fixture.SentCount);

        fixture.Connection.NotePeerCapability(Extended());

        fixture.Connection.sendPacket(Extended(1));
        Assert.Equal(1, fixture.SentCount);
    }

    [Fact]
    public void Receiving_an_ordinary_packet_does_not_mark_the_peer_capable()
    {
        // A vanilla peer sends plenty of these. Inferring capability from them would defeat the
        // gate entirely and push unparseable ids at it.
        Fixture fixture = new();

        fixture.Connection.NotePeerCapability(KeepAlivePacket.Get());

        Assert.False(fixture.Connection.betaSharpClient);
    }

    [Fact]
    public void The_message_envelope_is_a_gated_packet()
    {
        // Every extensible-layer message travels inside this one packet, so this single assertion
        // covers all of them. If it stopped being an ExtendedProtocolPacket, a vanilla peer would
        // receive an id it cannot parse and drop the connection.
        Assert.IsAssignableFrom<ExtendedProtocolPacket>(Extended());
    }
}
