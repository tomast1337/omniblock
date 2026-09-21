using System.Net;
using Microsoft.Extensions.Logging;
using OmniBlock.Network.Packets;
using OmniBlock.Network.Transport;
using OmniBlock.Util;

namespace OmniBlock.Network;

/// <summary>
///     A <see cref="Connection" /> over <see cref="ITransportConnection" />. The UDP path, and the
///     one the game uses for multiplayer.
///     <para>
///         <b>Framing is the substantive change.</b> The stream transport had none: the reader took
///         an id byte and each packet then read exactly the fields it expected, which is why an
///         unknown id killed the connection — nothing knew where the next packet began. A datagram
///         carries its own length, so one datagram is one packet and a malformed one costs a
///         datagram instead of a session.
///     </para>
///     <para>
///         There is no send queue here. The two-queue preemption the stream transport needed is
///         replaced by channels, which are strictly better at the same job: preemption only let a
///         waiting packet go next, while independent channels mean a stalled chunk does not delay
///         entity updates at all. See <see cref="ChannelFor" />.
///     </para>
/// </summary>
public sealed class UdpConnection : Connection
{
    /// <summary>
    ///     Control, world data and events. Everything whose meaning depends on arriving in order
    ///     relative to the rest of it.
    ///     <para>
    ///         Chunks share this channel with block updates deliberately. A block update that
    ///         overtakes the chunk it edits is applied to a chunk the client does not have and is
    ///         silently lost, so the two must stay in one ordering domain — the same constraint that
    ///         kept them in one queue on the stream transport. Splitting them needs block updates
    ///         tagged with the chunk revision they apply to, so a client can tell an update meant
    ///         for a chunk it has from one meant for a chunk still in flight.
    ///     </para>
    /// </summary>
    public const byte OrderedChannel = 0;

    /// <summary>
    ///     Entity replication and timing. Nothing here depends on world data having arrived first —
    ///     a spawn ahead of its chunk is a case the client already handles, by parking the entity
    ///     until the chunk loads — so it is free to overtake, which is the whole point.
    /// </summary>
    public const byte StateChannel = 1;

    /// <summary>
    ///     Subordinate, independently ordered bulk data such as distant-terrain cache records.
    ///     Keeping it off <see cref="OrderedChannel" /> means a multi-megabyte LOD record cannot
    ///     stand in front of a chunk, block update, inventory action, or chat message. Admission
    ///     and bandwidth fairness remain server policy; the channel supplies only isolation.
    /// </summary>
    public const byte BulkChannel = 2;

    /// <summary>
    ///     Everything is <see cref="DeliveryMode.ReliableOrdered" />, which is a starting position
    ///     and not the end state.
    ///     <para>
    ///         The tempting move is to send entity updates <see cref="DeliveryMode.UnreliableSequenced" />,
    ///         since a superseded position is worthless. It would be wrong today: sequenced means
    ///         only the newest payload <em>on that channel</em> survives, so one entity's update
    ///         would discard another's, and spawns and destroys would be dropped outright. That mode
    ///         becomes correct once snapshots are per-entity delta-compressed against a client ack,
    ///         and not before.
    ///     </para>
    /// </summary>
    private const DeliveryMode Mode = DeliveryMode.ReliableOrdered;

    private static readonly ILogger<UdpConnection> s_logger = Log.Instance.For<UdpConnection>();

    /// <summary>Reused across sends; the transport copies before returning.</summary>
    private readonly MemoryStream _sendBuffer = new(1024);

    private readonly ITransportConnection _transport;

    public UdpConnection(ITransportConnection transport, NetHandler? handler = null)
        : base(transport.RemoteEndPoint)
    {
        _transport = transport;
        netHandler = handler;
    }

    public ConnectionStats Stats => _transport.Stats;

    public override IPEndPoint? getAddress() => _transport.RemoteEndPoint;

    /// <summary>
    ///     Which ordering domain a packet belongs to, reusing the priority classification the stream
    ///     transport already used for its two queues.
    ///     <para>
    ///         Deliberately the same rule rather than a new one. That allowlist was reasoned through
    ///         and tested against exactly this question — which packets may safely overtake world
    ///         data — and the answer does not change because the mechanism did. What changes is that
    ///         a channel enforces it properly: on the stream, a chunk already being written still
    ///         had to finish.
    ///     </para>
    /// </summary>
    public static byte ChannelFor(Packet packet) => PacketPriorities.Of(packet) switch
    {
        SendPriority.High => StateChannel,
        SendPriority.Bulk => BulkChannel,
        _ => OrderedChannel
    };

    public override void sendPacket(Packet packet)
    {
        if (packet is ExtendedProtocolPacket && !betaSharpClient)
        {
            return;
        }

        if (closed || !_transport.IsConnected)
        {
            return;
        }

        try
        {
            // As late as the architecture allows, and later than the stream transport managed: there
            // is no queue of our own between here and the socket, so this really is the send instant
            // rather than the instant a drain got round to it.
            if (packet is OmniMessagePacket { CarriesSendTime: true, SentAtMs: 0 } envelope)
            {
                envelope.SentAtMs = MonotonicClock.NowMs();
            }

            _sendBuffer.SetLength(0);
            Packet.Write(packet, _sendBuffer);

            _transport.Send(ChannelFor(packet), Mode, _sendBuffer.GetBuffer().AsSpan(0, (int)_sendBuffer.Length));

            BytesWritten += _sendBuffer.Length;
            PacketsWritten++;
        }
        catch (Exception exception)
        {
            disconnect(exception);
        }
    }

    /// <summary>
    ///     Moves everything the transport has received into the read queue, then does the ordinary
    ///     per-tick work.
    ///     <para>
    ///         Decoding here rather than on the transport thread is deliberate: it keeps packet
    ///         parsing off a thread the transport needs for acks and keepalives, and the arrival
    ///         timestamp that would otherwise be lost travels on the datagram instead.
    ///     </para>
    /// </summary>
    /// <summary>
    ///     Depth of the transport's queue for the channel world data travels on. Entity replication
    ///     and timing are deliberately excluded: they ride <see cref="StateChannel" /> and must never
    ///     be a reason to slow chunk streaming, nor chunk streaming a reason to slow them.
    /// </summary>
    public override int getWorldPacketBacklog() => _transport.PendingPackets(OrderedChannel);

    public override int getBulkPacketBacklog() => _transport.PendingPackets(BulkChannel);

    public override void tick()
    {
        Receive();

        if (!_transport.IsConnected && open)
        {
            disconnect("disconnect.closed");
        }

        base.tick();
    }

    private void Receive()
    {
        while (_transport.TryReceive(out var datagram))
        {
            try
            {
                using MemoryStream stream = new(datagram.Payload, false);

                var packet = Packet.Read(stream, netHandler?.isServerSide() ?? false);
                if (packet is null)
                {
                    continue;
                }

                AcceptIncoming(packet, datagram.ReceivedAtTicks);
            }
            catch (Exception exception)
            {
                // Contained to this datagram. The length came from the transport rather than from
                // the payload, so a malformed packet cannot desynchronise anything after it — which
                // is exactly what the stream transport could not promise.
                s_logger.LogWarning(exception, "Dropping a malformed datagram from {Peer}.", getAddress());
            }
        }
    }

    public override void disconnect(string disconnectedReason, params object[] disconnectReasonArgs)
    {
        var wasOpen = open;

        base.disconnect(disconnectedReason, disconnectReasonArgs);

        if (wasOpen)
        {
            _transport.Close(DisconnectReason.Local);
        }
    }

    public override void disconnect()
    {
        closed = true;
        disconnect("disconnect.closed");
    }
}
