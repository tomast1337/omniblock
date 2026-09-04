using System.Net;

namespace OmniBlock.Network.Transport;

/// <summary>
///     How a payload is delivered. The transport's entire vocabulary for reliability and ordering.
///     <para>
///         Per-payload rather than per-connection, because the two kinds of traffic this protocol
///         carries want opposite things. A movement update is worthless once superseded, so
///         retransmitting it costs latency to deliver something already stale; a block change is
///         worthless if lost. One connection has to serve both without one starving the other,
///         which a single stream cannot express.
///     </para>
/// </summary>
public enum DeliveryMode
{
    /// <summary>Fire and forget. Redundant input frames, cosmetic effects.</summary>
    Unreliable,

    /// <summary>Newest wins, older discarded on arrival. Entity snapshots, time of day.</summary>
    UnreliableSequenced,

    /// <summary>Retransmitted until acknowledged, delivered in any order. Chunk fragments.</summary>
    Reliable,

    /// <summary>Retransmitted and ordered within its channel. Inventory, chat, commands.</summary>
    ReliableOrdered
}

/// <summary>Why a connection ended, as far as the transport can tell.</summary>
public enum DisconnectReason
{
    /// <summary>Asked for locally.</summary>
    Local,

    /// <summary>The peer asked.</summary>
    Remote,

    /// <summary>No traffic within the keepalive window.</summary>
    Timeout,

    /// <summary>The handshake did not complete.</summary>
    ConnectionFailed
}

/// <summary>
///     What the transport knows about a live connection.
///     <para>
///         Deliberately only what is genuinely measured. Jitter, loss and estimated bandwidth all
///         belong here and none is present, because LiteNetLib exposes them per <em>manager</em>
///         rather than per peer, so filling them in would mean reporting a number that does not
///         describe this connection. A missing field is honest, a zero is a measurement. They
///         arrive when there is a per-peer source for them.
///     </para>
/// </summary>
/// <param name="RoundTripMs">Smoothed round trip, as the transport measures it.</param>
/// <param name="Mtu">Payload bytes that fit in one datagram without fragmentation.</param>
public readonly record struct ConnectionStats(int RoundTripMs, int Mtu);

/// <summary>
///     One received payload, with the channel it arrived on.
///     <para>
///         The buffer is right-sized and owned by the caller. Pooling it would be the obvious
///         optimisation and is deliberately not done yet: the lifetime contract that makes pooling
///         safe is only worth its bug surface once something is actually running on this and has
///         been profiled.
///     </para>
/// </summary>
/// <param name="Channel">The channel the sender used.</param>
/// <param name="Payload">The bytes, exactly as sent.</param>
/// <param name="ReceivedAtTicks">
///     <see cref="Util.MonotonicClock" /> reading taken as the datagram arrived, on the transport's
///     own thread.
///     <para>
///         Only the transport can take this honestly. A timestamp taken where the game drains its
///         inbox measures how long ago the tick started, not when the bytes landed, and the two
///         differ by up to a whole tick — which is the quantity clock synchronisation is trying to
///         measure in the first place.
///     </para>
/// </param>
public readonly record struct ReceivedDatagram(byte Channel, byte[] Payload, long ReceivedAtTicks);

/// <summary>
///     A live connection to one peer, in bytes. Knows nothing about packets, messages, chunks or
///     players — that separation is the point of the seam, and the reason the backing library can be
///     replaced without touching game code.
/// </summary>
public interface ITransportConnection : IDisposable
{
    /// <summary>False once the peer is gone, for whatever reason.</summary>
    bool IsConnected { get; }

    /// <summary>The remote address, for logging and for the server's connection list.</summary>
    IPEndPoint? RemoteEndPoint { get; }

    ConnectionStats Stats { get; }

    /// <summary>
    ///     Queues a payload. The span is copied before returning, so the caller may reuse its
    ///     buffer immediately.
    /// </summary>
    /// <param name="channel">
    ///     Independent ordering domain, below the transport's channel count. Ordering holds
    ///     within a channel and never across, which is what keeps a bulk transfer from delaying
    ///     traffic that has nothing to do with it.
    /// </param>
    void Send(byte channel, DeliveryMode mode, ReadOnlySpan<byte> payload);

    /// <summary>
    ///     Packets handed to the transport for <paramref name="channel" /> that it has not yet got
    ///     rid of.
    ///     <para>
    ///         The signal a sender paces against, and the reason it is on the interface rather than
    ///         inferred: it is the only direct measurement of whether we are ahead of the link.
    ///         Bandwidth estimates and round-trip inflation are both proxies for this number, and
    ///         both need tuning constants that this does not.
    ///     </para>
    ///     <para>
    ///         Zero from a transport that cannot answer, which reads as "not backed up" and so paces
    ///         nothing. That is the right default: a transport with no queue to report is either
    ///         loopback or is doing its own pacing.
    ///     </para>
    /// </summary>
    int PendingPackets(byte channel);

    /// <summary>
    ///     Takes the next received payload, or returns false when none is waiting. Never blocks:
    ///     the game thread polls this and must not be parked by a quiet network.
    /// </summary>
    bool TryReceive(out ReceivedDatagram datagram);

    void Close(DisconnectReason reason);
}

/// <summary>
///     Opens and accepts <see cref="ITransportConnection" />s. The seam the rest of the protocol is
///     written against.
///     <para>
///         The interface is the architectural commitment; the library behind it is not. Chunk
///         transfer wants selective repeat, which the current backing cannot express, so replacing
///         it is expected — and the point of the seam is that doing so touches nothing above it.
///     </para>
/// </summary>
public interface ITransport : IAsyncDisposable
{
    /// <summary>
    ///     Independent ordering domains available, so callers can validate before sending. Which
    ///     traffic belongs on which number is a session-layer decision and deliberately not made
    ///     here.
    /// </summary>
    byte ChannelCount { get; }

    /// <summary>Connects to a peer, completing once the handshake succeeds.</summary>
    ValueTask<ITransportConnection> ConnectAsync(IPEndPoint remote, CancellationToken cancellationToken);

    /// <summary>Yields peers as they connect. Completes when the transport is disposed.</summary>
    IAsyncEnumerable<ITransportConnection> AcceptAsync(CancellationToken cancellationToken);
}
