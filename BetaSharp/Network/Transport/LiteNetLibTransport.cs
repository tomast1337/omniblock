using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using LiteNetLib;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Network.Transport;

/// <summary>
///     <see cref="ITransport" /> over LiteNetLib.
///     <para>
///         Chosen to get the protocol onto UDP without first writing ack bitfields and a
///         retransmit timer, which are solved problems and not where this fork's difficulty lies.
///         The parts that are genuinely novel are chunk streaming and prediction, and every hour
///         spent on sequence numbers is an hour not spent on those.
///     </para>
///     <para>
///         <b>Expected to be replaced, and cheap to replace.</b> Chunk transfer wants selective
///         repeat over independent transfers, which this channel model cannot express: a lost
///         fragment head-of-line blocks its whole channel, so one stalled chunk delays every chunk
///         behind it.
///         When that becomes the limit, a different implementation of this same interface takes over
///         and nothing above the seam changes.
///     </para>
///     <para>
///         One instance is one socket. A client's serves a single peer; a server's serves all of
///         them.
///     </para>
/// </summary>
public sealed class LiteNetLibTransport : ITransport
{
    /// <summary>
    ///     Independent ordering domains. More than the base game needs, because a mod shipping a
    ///     bulk asset transfer should not have to squeeze it through the chat channel. LiteNetLib
    ///     keeps per-peer state per channel, so this is not free and not unbounded either.
    /// </summary>
    public const byte Channels = 16;

    /// <summary>
    ///     Rejects a peer that does not present this. Not a security control — it travels in the
    ///     clear and anyone can copy it. It exists so that an unrelated service sharing the port
    ///     range is refused during the handshake rather than after it has confused something
    ///     further up.
    /// </summary>
    public const string DefaultConnectionKey = "omniblock";

    private static readonly ILogger<LiteNetLibTransport> s_logger = Log.Instance.For<LiteNetLibTransport>();

    private readonly NetManager _manager;
    private readonly EventBasedNetListener _listener;
    private readonly string _connectionKey;

    /// <summary>Live connections by peer, so receive callbacks can find the right inbox.</summary>
    private readonly ConcurrentDictionary<NetPeer, LiteNetLibConnection> _connections = new();

    /// <summary>Inbound connections waiting to be accepted. Unbounded: refusing a peer that has
    ///     already completed a handshake is worse than holding it.</summary>
    private readonly Channel<ITransportConnection> _accepted =
        Channel.CreateUnbounded<ITransportConnection>(new UnboundedChannelOptions { SingleReader = true });

    /// <summary>Completes a pending <see cref="ConnectAsync" />; null on a server.</summary>
    private TaskCompletionSource<LiteNetLibConnection>? _pendingConnect;

    private bool _disposed;

    public byte ChannelCount => Channels;

    /// <summary>The port actually bound, which matters when 0 was requested.</summary>
    public int LocalPort => _manager.LocalPort;

    public LiteNetLibTransport(string connectionKey = DefaultConnectionKey, bool enableIPv6 = true)
    {
        _connectionKey = connectionKey;
        _listener = new EventBasedNetListener();

        _manager = new NetManager(_listener)
        {
            // Events fire on LiteNetLib's own receive thread straight into the per-connection
            // queues, rather than waiting for someone to call PollEvents. This mirrors the read
            // thread the existing Connection already runs, and it keeps arrival timestamps honest:
            // a stamp taken after a game-thread poll measures the tick phase, not the network.
            UnsyncedEvents = true,

            // The reader is recycled the moment the callback returns, so its bytes are copied out
            // there. Nothing downstream may hold a reference to it.
            AutoRecycle = true,

            ChannelsCount = Channels,
            IPv6Enabled = enableIPv6,

            // A raw chunk is 81,920 bytes against a ~1,200 byte payload MTU, so a single reliable
            // send is around seventy fragments. The default ceiling is well above that; it is
            // pinned here because the number is load-bearing and silently truncating a chunk would
            // present as corrupt terrain rather than as a transport error.
            MaxFragmentsCount = 1024,

            // Matches the 30 s receive timeout the socket transport used. The server can stall
            // longer than a default keepalive window during world generation, and dropping players
            // for that would be a regression.
            DisconnectTimeout = 30_000,

            // How often the manager flushes queued sends. The default of 15 ms means a packet waits
            // 7.5 ms on average before leaving, in each direction, so it lands on a round trip
            // twice: measured at 14 ms of round trip on loopback, where the network contributes
            // nothing. That is pure pacing, and against a 50 ms tick it is most of a third of one.
            //
            // Receive is unaffected — events fire on the receive thread — and the server's own
            // handling time is already subtracted out of the clock estimate, so this was the entire
            // difference between 1 ms of round trip on the stream transport and 14 ms here.
            //
            // Five buys most of it back for one extra wakeup every 10 ms on a thread that does
            // nothing when there is nothing queued. Going lower has sharply diminishing returns:
            // 1 ms would save four more milliseconds for five times the wakeups.
            UpdateTime = 5,
        };

        _listener.ConnectionRequestEvent += request => request.AcceptIfKey(_connectionKey);
        _listener.PeerConnectedEvent += OnPeerConnected;
        _listener.PeerDisconnectedEvent += OnPeerDisconnected;
        _listener.NetworkReceiveEvent += OnNetworkReceive;
        _listener.NetworkErrorEvent += (endPoint, error) =>
            s_logger.LogDebug("Transport error from {EndPoint}: {Error}", endPoint, error);
    }

    /// <summary>Binds every interface and begins listening. Port 0 takes an ephemeral one; read it
    ///     back from <see cref="LocalPort" />.</summary>
    public void Listen(int port) => Start(port);

    /// <summary>
    ///     Binds one address. Kept distinct from <see cref="Listen(int)" /> because the difference
    ///     matters operationally: a server told to bind loopback and silently given every interface
    ///     is exposed to a network its operator meant to exclude.
    /// </summary>
    public void Listen(IPAddress address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);

        bool bound = address.AddressFamily == AddressFamily.InterNetworkV6
            ? _manager.Start(IPAddress.Any, address, port)
            : _manager.Start(address, IPAddress.IPv6Any, port);

        if (!bound)
        {
            throw new IOException($"Could not bind a UDP socket on {address}:{port}.");
        }
    }

    /// <summary>Binds an ephemeral port for outbound use.</summary>
    public void StartClient() => Start(0);

    private void Start(int port)
    {
        if (!_manager.Start(port))
        {
            throw new IOException($"Could not bind a UDP socket on port {port}.");
        }
    }

    public async ValueTask<ITransportConnection> ConnectAsync(
        IPEndPoint remote, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(remote);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_manager.IsRunning)
        {
            StartClient();
        }

        TaskCompletionSource<LiteNetLibConnection> pending =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingConnect = pending;

        // LiteNetLib returns a peer immediately, but it is not usable until the handshake lands and
        // PeerConnectedEvent fires. Returning the peer here would hand back a connection whose first
        // sends are silently dropped.
        _manager.Connect(remote, _connectionKey);

        await using CancellationTokenRegistration registration = cancellationToken.Register(
            () => pending.TrySetCanceled(cancellationToken));

        return await pending.Task.ConfigureAwait(false);
    }

    public IAsyncEnumerable<ITransportConnection> AcceptAsync(CancellationToken cancellationToken) =>
        _accepted.Reader.ReadAllAsync(cancellationToken);

    private void OnPeerConnected(NetPeer peer)
    {
        LiteNetLibConnection connection = new(peer, Channels);
        _connections[peer] = connection;

        // An outbound connect completes its awaiter; an inbound one joins the accept queue. The
        // event is the same either way, so which of the two it is has to come from local state.
        TaskCompletionSource<LiteNetLibConnection>? pending =
            Interlocked.Exchange(ref _pendingConnect, null);

        if (pending is not null)
        {
            pending.TrySetResult(connection);
            return;
        }

        _accepted.Writer.TryWrite(connection);
    }

    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        if (_connections.TryRemove(peer, out LiteNetLibConnection? connection))
        {
            connection.MarkDisconnected();
        }

        // A failed outbound handshake surfaces here rather than as a connect event, so an awaiter
        // that would otherwise hang forever is failed explicitly.
        TaskCompletionSource<LiteNetLibConnection>? pending =
            Interlocked.Exchange(ref _pendingConnect, null);

        pending?.TrySetException(
            new IOException($"Connection to {peer.Address} failed: {info.Reason}."));
    }

    private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
    {
        if (_connections.TryGetValue(peer, out LiteNetLibConnection? connection))
        {
            // Copied here, on the callback, because AutoRecycle reclaims the reader the moment this
            // returns. The arrival stamp is taken here for the same reason it cannot be taken later:
            // this is the only point that knows when the bytes actually landed.
            connection.Enqueue(new ReceivedDatagram(
                channel, reader.GetRemainingBytes(), Util.MonotonicClock.NowTicks()));
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;

        _accepted.Writer.TryComplete();
        Interlocked.Exchange(ref _pendingConnect, null)
            ?.TrySetException(new ObjectDisposedException(nameof(LiteNetLibTransport)));

        _manager.Stop();
        _connections.Clear();

        return ValueTask.CompletedTask;
    }

    /// <summary>One peer. Receives on LiteNetLib's thread, is drained on the game's.</summary>
    private sealed class LiteNetLibConnection(NetPeer peer, byte channelCount) : ITransportConnection
    {
        private readonly ConcurrentQueue<ReceivedDatagram> _inbox = new();
        private volatile bool _connected = true;

        public bool IsConnected => _connected && peer.ConnectionState == ConnectionState.Connected;

        public IPEndPoint? RemoteEndPoint => new(peer.Address, peer.Port);

        public ConnectionStats Stats => new(peer.RoundTripTime, peer.Mtu);

        public void Send(byte channel, DeliveryMode mode, ReadOnlySpan<byte> payload)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(channel, channelCount);

            if (!_connected)
            {
                return;
            }

            peer.Send(payload, channel, ToDeliveryMethod(mode));
        }

        public bool TryReceive(out ReceivedDatagram datagram) => _inbox.TryDequeue(out datagram);

        /// <summary>
        ///     Depth of the peer's outgoing reliable-ordered queue for one channel. Both delivery
        ///     modes we map onto a channel are asked for, since a caller pacing against a channel
        ///     cares about everything queued on it and not about how it was addressed.
        /// </summary>
        public int PendingPackets(byte channel)
        {
            if (!_connected || channel >= channelCount)
            {
                return 0;
            }

            return peer.GetPacketsCountInReliableQueue(channel, ordered: true)
                + peer.GetPacketsCountInReliableQueue(channel, ordered: false);
        }

        public void Close(DisconnectReason reason)
        {
            if (_connected)
            {
                _connected = false;
                peer.Disconnect();
            }
        }

        internal void Enqueue(ReceivedDatagram datagram) => _inbox.Enqueue(datagram);

        internal void MarkDisconnected() => _connected = false;

        public void Dispose() => Close(DisconnectReason.Local);

        /// <summary>
        ///     LiteNetLib's <c>ReliableSequenced</c> has no counterpart here on purpose: it is
        ///     reliable delivery of only the newest, which is a combination nothing in this protocol
        ///     wants. If a payload is worth retransmitting it is worth delivering, and if it can be
        ///     superseded it is not worth retransmitting.
        /// </summary>
        private static DeliveryMethod ToDeliveryMethod(DeliveryMode mode) => mode switch
        {
            DeliveryMode.Unreliable => DeliveryMethod.Unreliable,
            DeliveryMode.UnreliableSequenced => DeliveryMethod.Sequenced,
            DeliveryMode.Reliable => DeliveryMethod.ReliableUnordered,
            DeliveryMode.ReliableOrdered => DeliveryMethod.ReliableOrdered,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown delivery mode."),
        };
    }
}
