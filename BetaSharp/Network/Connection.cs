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

    /// <summary>
    ///     Entity replication and timing, drained before <see cref="_bulkQueue" />. See
    ///     <see cref="PacketPriorities" /> for what qualifies and why the set is an allowlist.
    /// </summary>
    private readonly ConcurrentQueue<Packet> _priorityQueue = [];

    /// <summary>Everything else, in strict arrival order — chunks, block updates, inventory, chat.</summary>
    private readonly ConcurrentQueue<Packet> _bulkQueue = [];
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
            QueueFor(packet).Enqueue(packet);
        }
    }

    private ConcurrentQueue<Packet> QueueFor(Packet packet) =>
        PacketPriorities.Of(packet) == SendPriority.High ? _priorityQueue : _bulkQueue;

    /// <summary>
    ///     Next packet to write: everything queued as <see cref="SendPriority.High" />, then bulk.
    ///     <para>
    ///         Preemption is at packet granularity — a chunk already being written still runs to
    ///         completion, because the bytes are committed to the stream the moment the write
    ///         starts. Splitting the chunk packet itself is §4.2 and is what removes the remainder.
    ///     </para>
    /// </summary>
    internal bool TryDequeueNext(out Packet? packet) =>
        _priorityQueue.TryDequeue(out packet) || _bulkQueue.TryDequeue(out packet);

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

            while (TryDequeueNext(out Packet? packet))
            {
                WritePacket(packet!);
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
    public int SendQueueDepth => _priorityQueue.Count + _bulkQueue.Count;

    /// <summary>
    ///     Depth of the high-priority queue alone. Distinct from <see cref="SendQueueDepth" />
    ///     because the two mean different things: bulk depth rising is a chunk backlog and expected,
    ///     while this one rising means entity updates are queueing behind entity updates, which the
    ///     priority split cannot help with.
    /// </summary>
    public int PrioritySendQueueDepth => _priorityQueue.Count;

    /// <summary>
    ///     Packets read off the socket but not yet applied to the handler.
    ///     <para>
    ///         The read thread has no rate limit and the drain does, so this is where an overload
    ///         accumulates. It is <em>not</em> a backlog in the ordinary sense: every packet in here
    ///         is applied eventually, so a depth that keeps rising is latency growing without bound
    ///         rather than work being shed. A player rubber-banding to where the server put them
    ///         several seconds ago is this number, not a physics or interpolation fault.
    ///     </para>
    /// </summary>
    public int ReadQueueDepth => readQueue.Count;

    /// <summary>High-water mark of <see cref="ReadQueueDepth" />, sampled once per tick.</summary>
    public int PeakReadQueueDepth { get; private set; }

    /// <summary>
    ///     Depth at which the backlog is logged, once. Well above anything a normal tick produces —
    ///     a hundred tracked entities is a few hundred packets a tick — and well below the depths a
    ///     genuinely losing race reaches within seconds.
    /// </summary>
    private const int BacklogWarningDepth = 20_000;

    private bool _backlogWarned;

    /// <summary>
    ///     Packets applied to the handler. Against <see cref="PacketsRead" /> this is the drain rate
    ///     versus the arrival rate, and the two diverging is the whole diagnosis.
    /// </summary>
    public long PacketsProcessed { get; private set; }

    /// <summary>Applies one packet and counts it. The single drain point for every subclass.</summary>
    protected void ApplyPacket(Packet packet, NetHandler handler)
    {
        PacketsProcessed++;
        packet.Apply(handler);
    }

    public virtual void tick()
    {
        if (SendQueueDepth > 1048576)
        {
            disconnect("disconnect.overflow");
        }

        int depth = readQueue.Count;
        if (depth > PeakReadQueueDepth)
        {
            PeakReadQueueDepth = depth;
        }

        // The read side has no equivalent of the send queue's overflow disconnect, and unlike the
        // send side it cannot simply be paced: the packets are already off the socket. Warn once at
        // a depth that is unambiguously a losing race, so the log says what the overlay says.
        if (depth > BacklogWarningDepth && !_backlogWarned)
        {
            _backlogWarned = true;
            _logger.LogWarning(
                "Read backlog of {Depth} packets: arriving faster than they can be applied, so positions are being applied late. See docs/time-sync-and-interpolation.md.",
                depth);
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

    /// <summary>
    ///     Wall-clock budget for one tick's drain, out of a 50 ms tick.
    ///     <para>
    ///         Replaces a fixed cap of 100 packets per tick, which at 20 TPS was a hard ceiling of
    ///         2,000 packets per second regardless of how cheap they were. A few hundred tracked
    ///         entities exceed that on their own — a thousand mobs at <c>trackingFrequency</c> 3
    ///         produce roughly 6,700 movement packets per second — and past the ceiling the read
    ///         queue grows without bound, so the client applies positions from further and further
    ///         in the past. Measured at 84 seconds behind on a thousand-cow world, with the local
    ///         player rubber-banding to server corrections issued a minute earlier.
    ///     </para>
    ///     <para>
    ///         A budget rather than a larger count because the two limit different things. What
    ///         must not happen is the drain overrunning the tick; the number of packets that fits is
    ///         a consequence of how expensive they turn out to be, and entity movement packets are
    ///         far cheaper than a chunk. A count has to be sized for the worst packet and then
    ///         throttles the cheap ones for no reason.
    ///     </para>
    ///     <para>
    ///         Per connection, not per tick globally. That is fine in practice because the direction
    ///         that carries volume is server to client, where a client has exactly one connection.
    ///         A server with many players has many budgets, but each inbound stream is a handful of
    ///         movement and action packets per second and never approaches this.
    ///     </para>
    /// </summary>
    public const double DrainBudgetMs = 10.0;

    /// <summary>
    ///     Packets applied between budget checks. Reading the clock per packet would cost more than
    ///     applying one, and it also sets the floor on forward progress: the drain always applies at
    ///     least this many, so a single expensive packet cannot leave the queue permanently stuck.
    /// </summary>
    private const int BudgetCheckInterval = 64;

    /// <summary>
    ///     Ticks whose drain hit <see cref="DrainBudgetMs" /> and stopped early. Non-zero means
    ///     packets are arriving faster than they can be applied, which no other counter shows —
    ///     nothing is dropped, so the totals stay healthy while latency grows.
    /// </summary>
    public long DrainBudgetHits { get; private set; }

    protected virtual void processPackets()
    {
        if (netHandler == null)
        {
            throw new Exception("networkHandler is null");
        }

        long start = MonotonicClock.NowTicks();
        int sinceCheck = 0;

        while (readQueue.TryDequeue(out Packet? packet))
        {
            ApplyPacket(packet, netHandler);

            if (++sinceCheck < BudgetCheckInterval)
            {
                continue;
            }

            sinceCheck = 0;

            if (MonotonicClock.ElapsedMs(start, MonotonicClock.NowTicks()) >= DrainBudgetMs)
            {
                DrainBudgetHits++;
                break;
            }
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

    /// <summary>
    ///     Records when a message envelope arrived, on the read thread.
    ///     <para>
    ///         Unconditional and message-agnostic. This used to be a switch over the two concrete
    ///         time-sync packet types, which put knowledge of one feature's clock arithmetic inside
    ///         the transport; now the transport records when things arrive and the message layer
    ///         decides what that is worth. It costs nothing — the clock reading is one the read loop
    ///         already takes for the arrival histogram.
    ///     </para>
    /// </summary>
    private static void StampArrival(Packet packet, long timestampTicks)
    {
        if (packet is OmniMessagePacket envelope)
        {
            envelope.ReceivedAtMs = MonotonicClock.ToMs(timestampTicks);
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
                    StampArrival(packet, now);

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

                while (TryDequeueNext(out Packet? packet))
                {
                    WritePacket(packet!);
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

        // Stamped as late as possible — inside the write path, immediately before the bytes go to
        // the socket, rather than in the handler where a send-queue drain could add up to a chunk's
        // worth of delay. Which messages want this is the message layer's decision, declared by
        // Message.NeedsSendTimestamp; the transport only knows that the envelope reserved room.
        if (packet is OmniMessagePacket { CarriesSendTime: true, SentAtMs: 0 } envelope)
        {
            envelope.SentAtMs = MonotonicClock.NowMs();
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
