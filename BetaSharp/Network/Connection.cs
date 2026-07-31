using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Packets.C2SPlay;
using BetaSharp.Network.Packets.S2CPlay;
using BetaSharp.Util;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Network;

public class Connection
{
    /// <summary>
    ///     True once the <em>peer</em> on this connection is known to speak the extended protocol.
    ///     Gates outgoing <see cref="ExtendedProtocolPacket" />s, so a vanilla peer never receives
    ///     an id it cannot parse.
    ///     <para>
    ///         Set two ways, because the two ends learn it differently. The server infers it from
    ///         the login signature before it has sent anything. The client has no such signal ahead
    ///         of time, so both ends also set it on receipt of any extended packet — receiving one
    ///         is proof the sender speaks the protocol.
    ///     </para>
    ///     <para>
    ///         This is a compatibility filter, not an authorisation check. A peer that sends an
    ///         extended packet has demonstrated the capability; there is nothing here to bypass.
    ///     </para>
    /// </summary>
    public bool betaSharpClient = false;

    private readonly ILogger<Connection> _logger = Log.Instance.For<Connection>();
    private readonly IPEndPoint? _address;

    protected bool open = true;
    protected ConcurrentQueue<Packet> readQueue = [];
    protected NetHandler? netHandler;
    protected bool closed;
    protected bool disconnected;
    protected string disconnectedReason = "";
    protected object[]? disconnectReasonArgs;

    public long BytesRead { get; protected set; }
    public long BytesWritten { get; protected set; }
    public int PacketsRead { get; protected set; }
    public int PacketsWritten { get; protected set; }

    /// <summary>
    ///     Gap between successive packet arrivals. On a client this is the head-of-line stall the
    ///     snapshot buffer will have to absorb, which is what sizes the interpolation delay — see
    ///     <c>docs/time-sync-and-interpolation.md</c> §3.4.
    /// </summary>
    public PacketArrivalHistogram ReadIntervals { get; } = new();

    /// <summary>
    ///     Time spent inside a single blocking write. This is the stall's <em>cause</em> rather than
    ///     its symptom: the send queue is drained strictly in order, so a large packet occupies the
    ///     socket for its whole duration and everything behind it waits.
    /// </summary>
    public PacketArrivalHistogram WriteDurations { get; } = new();

    /// <summary>Largest single packet written, in bytes. Expected to be a chunk.</summary>
    public int LargestPacketWritten { get; private set; }

    private long _lastReadTimestamp;

    private int _timeout;
    private readonly ConcurrentQueue<Packet> _sendQueue = [];
    private Socket? _socket;
    private NetworkStream? _networkStream;

    public Connection(Socket socket, string address, NetHandler netHandler)
    {
        _socket = socket;
        _address = (IPEndPoint?)socket.RemoteEndPoint;
        this.netHandler = netHandler;

        socket.ReceiveTimeout = 30000;

        _networkStream = new NetworkStream(socket);

        Task.Factory.StartNew(Reading, TaskCreationOptions.LongRunning);
        Task.Factory.StartNew(Writing, TaskCreationOptions.LongRunning);
    }

    protected Connection()
    {
        _address = null;
    }

    public void setNetworkHandler(NetHandler netHandler)
    {
        this.netHandler = netHandler;
    }

    public virtual void sendPacket(Packet packet)
    {
        if (packet is ExtendedProtocolPacket && !betaSharpClient) return;

        if (!closed)
        {
            _sendQueue.Enqueue(packet);
        }
    }

    private void disconnect(Exception e)
    {
        _logger.LogError(e, e.Message);
        disconnect("disconnect.genericReason", "Internal exception: " + e);
    }

    public virtual void disconnect(string disconnectedReason, params object[] disconnectReasonArgs)
    {
        if (open)
        {
            disconnected = true;
            this.disconnectedReason = disconnectedReason;
            this.disconnectReasonArgs = disconnectReasonArgs;
            open = false;

            foreach (var packet in _sendQueue)
            {
                WritePacket(packet);
            }

            try
            {
                _networkStream?.Close();
                _networkStream = null;

                _socket?.Close();
                _socket = null;
            }
            catch (Exception)
            {
                // Ignore.
            }
        }
    }

    /// <summary>
    ///     Packets queued for the writer but not yet on the socket. A rising depth is the send side
    ///     of the same stall <see cref="WriteDurations" /> measures, and is what the priority queue
    ///     in <c>docs/time-sync-and-interpolation.md</c> §4.1 would reorder.
    /// </summary>
    public int SendQueueDepth => _sendQueue.Count;

    public virtual void tick()
    {
        if (_sendQueue.Count > 1048576)
        {
            disconnect("disconnect.overflow");
        }

        if (readQueue.IsEmpty)
        {
            if (_timeout++ == 1200)
            {
                disconnect("disconnect.timeout");
            }
        }
        else
        {
            _timeout = 0;
        }

        processPackets();

        if (disconnected && readQueue.IsEmpty)
        {
            netHandler?.onDisconnected(disconnectedReason, disconnectReasonArgs);
        }
    }

    protected virtual void processPackets()
    {
        if (netHandler == null)
        {
            throw new Exception("networkHandler is null");
        }

        int maxPacketsPerTick = 100;

        while (readQueue.TryDequeue(out Packet? packet) && maxPacketsPerTick-- >= 0)
        {
            packet.Apply(netHandler);
        }
    }

    public virtual IPEndPoint? getAddress()
    {
        return _address;
    }

    public virtual void disconnect()
    {
        closed = true;
        disconnect(new Exception("disconnect.closed"));
    }

    public int getWorldPacketBacklog()
    {
        return 0;
    }

    /// <summary>
    ///     Stamps T1 (server receiving request) or T3 (client receiving response) on the read
    ///     thread, before the packet is queued for the game thread's drain. Called from
    ///     <see cref="Reading" />.
    /// </summary>
    /// <summary>
    ///     Marks the peer as speaking the extended protocol once it sends something only such a
    ///     peer could send.
    ///     <para>
    ///         This is the client's only capability signal. The server infers it from the login
    ///         signature before it has sent anything, but nothing travels back the other way, so
    ///         without this the client's <see cref="betaSharpClient" /> stays false for the life of
    ///         the connection and <see cref="sendPacket" /> silently discards every extended packet
    ///         it tries to send — including the time-sync probes the clock needs to converge.
    ///     </para>
    /// </summary>
    internal void NotePeerCapability(Packet packet)
    {
        if (packet is ExtendedProtocolPacket)
        {
            betaSharpClient = true;
        }
    }

    private static void StampTimeSyncTimestamp(Packet packet, long timestampTicks)
    {
        long ms = MonotonicClock.ToMs(timestampTicks);

        switch (packet)
        {
            case TimeSyncRequestC2SPacket request:
                request.ServerRecvTime = ms;
                break;

            case TimeSyncResponseS2CPacket response:
                response.ClientRecvTime = ms;
                break;
        }
    }

    private void Reading()
    {
        while (open && !closed)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(_networkStream);
                ArgumentNullException.ThrowIfNull(netHandler);

                Packet? packet = Packet.Read(_networkStream, netHandler.isServerSide());

                if (packet is not null)
                {
                    // Stamped here rather than where the packet is drained in tick(): draining
                    // happens on the game thread up to a full tick later, which would measure the
                    // tick phase instead of the network. T1 and T3 in particular must be on the
                    // read path for clock sync to be honest.
                    long now = MonotonicClock.NowTicks();
                    if (_lastReadTimestamp != 0)
                    {
                        ReadIntervals.Record(MonotonicClock.ElapsedMs(_lastReadTimestamp, now));
                    }

                    _lastReadTimestamp = now;

                    NotePeerCapability(packet);
                    StampTimeSyncTimestamp(packet, now);

                    BytesRead += packet.Size();
                    PacketsRead++;
                    readQueue.Enqueue(packet);
                }
                else
                {
                    disconnect("disconnect.endOfStream");
                    break;
                }
            }
            catch (Exception exception)
            {
                disconnect(exception);
                break;
            }
        }
    }

    private async Task Writing()
    {
        while (open && !closed)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(_networkStream);

                while (_sendQueue.TryDequeue(out var packet))
                {
                    WritePacket(packet);
                }

                await Task.Delay(1);
            }
            catch (Exception exception)
            {
                if (!disconnected)
                {
                    disconnect(exception);
                }
                break;
            }
        }
    }

    private void WritePacket(Packet packet)
    {
        ArgumentNullException.ThrowIfNull(_networkStream);

        // T2: the server's timestamp for the time-sync response, stamped as late as possible —
        // inside the write path, immediately before the bytes go to the socket, rather than in the
        // handler where a send-queue drain could add up to a chunk's worth of delay.
        if (packet is TimeSyncResponseS2CPacket response && response.ServerSendTime == 0)
        {
            response.ServerSendTime = MonotonicClock.NowMs();
        }

        long start = MonotonicClock.NowTicks();

        Packet.Write(packet, _networkStream);

        int size = packet.Size();
        BytesWritten += size;
        PacketsWritten++;

        _networkStream.Flush();

        WriteDurations.Record(MonotonicClock.ElapsedMs(start, MonotonicClock.NowTicks()));

        if (size > LargestPacketWritten)
        {
            LargestPacketWritten = size;
        }
    }
}
