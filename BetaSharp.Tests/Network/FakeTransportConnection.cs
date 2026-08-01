using System.Net;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Transport;
using BetaSharp.Util;

namespace BetaSharp.Tests.Network;

/// <summary>
///     An <see cref="ITransportConnection" /> that records what was sent and replays what it is
///     told to receive.
///     <para>
///         The real transport is exercised over a real socket in
///         <see cref="LiteNetLibTransportTests" />. This exists for the layer above it, where the
///         questions are which channel a packet took and whether the compatibility gate held —
///         neither of which needs a network, and both of which are obscured by one.
///     </para>
/// </summary>
internal sealed class FakeTransportConnection : ITransportConnection
{
    public List<(byte Channel, DeliveryMode Mode, byte[] Payload)> Sent { get; } = [];

    private readonly Queue<ReceivedDatagram> _inbox = new();

    public bool IsConnected { get; set; } = true;

    public IPEndPoint? RemoteEndPoint { get; set; } = new(IPAddress.Loopback, 25565);

    public ConnectionStats Stats => new(0, 1200);

    public DisconnectReason? ClosedWith { get; private set; }

    public void Send(byte channel, DeliveryMode mode, ReadOnlySpan<byte> payload) =>
        Sent.Add((channel, mode, payload.ToArray()));

    public bool TryReceive(out ReceivedDatagram datagram) => _inbox.TryDequeue(out datagram);

    public void Close(DisconnectReason reason)
    {
        ClosedWith = reason;
        IsConnected = false;
    }

    public void Dispose() => Close(DisconnectReason.Local);

    /// <summary>Queues a packet as though the peer had sent it.</summary>
    public void Deliver(Packet packet, byte channel = 0)
    {
        MemoryStream stream = new();
        Packet.Write(packet, stream);

        _inbox.Enqueue(new ReceivedDatagram(channel, stream.ToArray(), MonotonicClock.NowTicks()));
    }

    /// <summary>Queues arbitrary bytes, for the malformed-datagram case.</summary>
    public void DeliverRaw(byte[] payload, byte channel = 0) =>
        _inbox.Enqueue(new ReceivedDatagram(channel, payload, MonotonicClock.NowTicks()));

    /// <summary>The packet ids sent, in order, decoded back off the wire.</summary>
    public IEnumerable<byte> SentPacketIds() => Sent.Select(s => s.Payload[0]);
}
