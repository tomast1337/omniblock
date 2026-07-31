using BetaSharp.Network;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Packets.C2SPlay;
using BetaSharp.Network.Packets.Play;
using BetaSharp.Network.Packets.S2CPlay;

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
    /// <summary>Uses the parameterless constructor, so no socket and no reader/writer threads.</summary>
    private sealed class TestConnection : Connection;

    [Fact]
    public void Extended_packets_are_dropped_while_the_peer_is_unknown()
    {
        TestConnection connection = new();
        connection.sendPacket(TimeSyncRequestC2SPacket.Get(0, 0));

        Assert.Equal(0, connection.SendQueueDepth);
    }

    [Fact]
    public void Extended_packets_are_sent_once_the_peer_is_known()
    {
        TestConnection connection = new() { betaSharpClient = true };
        connection.sendPacket(TimeSyncRequestC2SPacket.Get(0, 0));

        Assert.Equal(1, connection.SendQueueDepth);
    }

    [Fact]
    public void Ordinary_packets_are_never_gated()
    {
        // The gate must not touch the vanilla protocol: a plain packet goes out regardless.
        TestConnection connection = new();
        connection.sendPacket(KeepAlivePacket.Get());

        Assert.Equal(1, connection.SendQueueDepth);
    }

    // ---- the capability signal ----

    [Fact]
    public void Receiving_an_extended_packet_marks_the_peer_capable()
    {
        // The actual regression. A client that never sets this discards its own probes forever, and
        // the symptom is a clock stuck on "Synchronising..." with a healthy connection underneath.
        TestConnection connection = new();
        Assert.False(connection.betaSharpClient);

        connection.NotePeerCapability(TickStampS2CPacket.Get(0));

        Assert.True(connection.betaSharpClient);
    }

    [Fact]
    public void A_client_can_reply_after_the_server_sends_its_first_extended_packet()
    {
        // End to end in one direction: silent before the server identifies itself, sending after.
        TestConnection connection = new();

        connection.sendPacket(TimeSyncRequestC2SPacket.Get(0, 0));
        Assert.Equal(0, connection.SendQueueDepth);

        connection.NotePeerCapability(TickStampS2CPacket.Get(0));

        connection.sendPacket(TimeSyncRequestC2SPacket.Get(1, 0));
        Assert.Equal(1, connection.SendQueueDepth);
    }

    [Fact]
    public void Receiving_an_ordinary_packet_does_not_mark_the_peer_capable()
    {
        // A vanilla peer sends plenty of these. Inferring capability from them would defeat the
        // gate entirely and push unparseable ids at it.
        TestConnection connection = new();

        connection.NotePeerCapability(KeepAlivePacket.Get());

        Assert.False(connection.betaSharpClient);
    }

    [Fact]
    public void Both_time_sync_directions_are_gated_packets()
    {
        // If either stopped being an ExtendedProtocolPacket, a vanilla peer would receive an id it
        // cannot parse and drop the connection.
        Assert.IsAssignableFrom<ExtendedProtocolPacket>(TimeSyncRequestC2SPacket.Get(0, 0));
        Assert.IsAssignableFrom<ExtendedProtocolPacket>(TimeSyncResponseS2CPacket.Get(0, 0, 0, 0));
    }
}
