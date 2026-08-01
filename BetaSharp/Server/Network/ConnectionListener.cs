using System.Net;
using BetaSharp.Network;
using BetaSharp.Network.Transport;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Server.Network;

public class ConnectionListener
{
    /// <summary>
    ///     The UDP transport peers arrive on, or null for the singleplayer server, which accepts
    ///     only loopback connections handed to it directly.
    /// </summary>
    public LiteNetLibTransport? Transport { get; }

    private readonly CancellationTokenSource? _accepting;
    private readonly ILogger<ConnectionListener> _logger = Log.Instance.For<ConnectionListener>();

    public volatile bool open;
    private int _connectionCounter = 0;
    private readonly object _connectionCounterLock = new();
    private readonly object _pendingConnectionsLock = new();
    private readonly object _connectionsLock = new();
    private readonly List<ServerLoginNetworkHandler> _pendingConnections = [];
    private readonly List<ServerPlayNetworkHandler> _connections = [];
    public BetaSharpServer server;
    public int port;

    public ConnectionListener(BetaSharpServer server, IPAddress address, int port, bool dualStack = false)
    {
        this.server = server;

        // dualStack maps onto whether the transport binds IPv6 at all, which is the same choice the
        // stream listener expressed through Socket.DualMode.
        Transport = new LiteNetLibTransport(enableIPv6: dualStack);
        Transport.Listen(address, port);

        this.port = Transport.LocalPort;
        open = true;

        _accepting = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoopAsync(_accepting.Token));

        _logger.LogInformation("Listening for UDP connections on {Address}:{Port}", address, this.port);
    }

    public ConnectionListener(BetaSharpServer server)
    {
        this.server = server;
        Transport = null;
        port = 0;
        open = true;
        _accepting = null;
    }

    /// <summary>
    ///     Turns accepted transport peers into pending logins.
    ///     <para>
    ///         The per-address throttle the stream listener carried is gone with it. It existed
    ///         because a TCP accept is cheap for the attacker and expensive for the server; the
    ///         transport now refuses anything that fails the connection-key handshake before a peer
    ///         object exists at all, which covers the same ground at a lower layer. A real rate
    ///         limit belongs there too, not here.
    ///     </para>
    /// </summary>
    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (ITransportConnection peer in Transport!.AcceptAsync(cancellationToken))
            {
                UdpConnection connection = new(peer);
                ServerLoginNetworkHandler handler = new(server, connection);

                _logger.LogDebug(
                    "Connection # {Id} from {Peer}", GetNextConnectionCounter(), peer.RemoteEndPoint);

                AddPendingConnection(handler);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The accept loop stopped.");
        }
    }

    /// <summary>Stops accepting and releases the socket.</summary>
    public async Task StopAsync()
    {
        open = false;

        if (_accepting is not null)
        {
            await _accepting.CancelAsync();
        }

        if (Transport is not null)
        {
            await Transport.DisposeAsync();
        }
    }

    public int GetNextConnectionCounter()
    {
        lock (_connectionCounterLock)
        {
            return _connectionCounter++;
        }
    }

    public void AddConnection(ServerPlayNetworkHandler connection)
    {
        lock (_connectionsLock)
        {
            _connections.Add(connection);
        }
    }

    public void AddPendingConnection(ServerLoginNetworkHandler connection)
    {
        if (connection == null)
        {
            throw new ArgumentException("Got null pendingconnection!", nameof(connection));
        }
        else
        {
            lock (_pendingConnectionsLock)
            {
                _pendingConnections.Add(connection);
            }
        }
    }

    public void AddInternalConnection(InternalConnection connection)
    {
        ServerLoginNetworkHandler loginHandler = new(server, connection);
        lock (_pendingConnectionsLock)
        {
            _pendingConnections.Add(loginHandler);
        }
    }

    public void Tick()
    {
        lock (_pendingConnectionsLock)
        {
            for (int i = 0; i < _pendingConnections.Count; i++)
            {
                ServerLoginNetworkHandler connection = _pendingConnections[i];

                try
                {
                    connection.tick();
                }
                catch (Exception ex)
                {
                    connection.disconnect("Internal server error");
                    _logger.LogError($"Failed to handle packet: {ex}");
                }

                if (connection.closed)
                {
                    _pendingConnections.RemoveAt(i--);
                }

            }
        }

        lock (_connectionsLock)
        {
            for (int i = 0; i < _connections.Count; i++)
            {
                ServerPlayNetworkHandler connection = _connections[i];

                try
                {
                    connection.tick();
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to handle packet: {ex}");
                    connection.disconnect("Internal server error");
                }

                if (connection.disconnected)
                {
                    _connections.RemoveAt(i--);
                }

            }
        }
    }
}
