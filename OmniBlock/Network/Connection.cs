using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;
using OmniBlock.Util;

namespace OmniBlock.Network;

/// <summary>
///     One peer's packet stream: the queue the game drains, the compatibility gate, and the
///     diagnostics. Owns no socket.
///     <para>
///         Transport lives in subclasses — <see cref="UdpConnection" /> over
///         <see cref="Transport.ITransport" />, <see cref="InternalConnection" /> over a direct
///         hand-off for singleplayer. Everything that is true regardless of how bytes move is here,
///         which is what let the transport be replaced without the game noticing.
///     </para>
/// </summary>
public class Connection
{
    /// <summary>
    ///     Depth at which the backlog is logged, once. Well above anything a normal tick produces —
    ///     a hundred tracked entities is a few hundred packets a tick — and well below the depths a
    ///     genuinely losing race reaches within seconds.
    /// </summary>
    private const int BacklogWarningDepth = 20_000;

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
    ///     Packets applied between budget checks. This is one deliberately: full chunk messages can
    ///     each be expensive enough to consume the budget by themselves. Checking only every 64
    ///     packets let loopback spend hundreds of milliseconds decoding terrain in one client tick.
    ///     The check occurs after applying a packet, so forward progress is still guaranteed.
    /// </summary>
    private const int BudgetCheckInterval = 1;

    private readonly IPEndPoint? _address;

    /// <summary>
    ///     What the drain budget is measured against.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Deliberately not <see cref="MonotonicClock" />, which every timestamp that crosses the
    ///         wire has to come from. This reading never leaves the process — it decides only whether
    ///         to stop draining and go back to the tick — so it is free to be something a test can
    ///         drive, and the wire's single time source is untouched.
    ///     </para>
    ///     <para>
    ///         The budget is the one thing here that a test cannot arrange by choosing its input:
    ///         "enough work to overrun 10 ms" depends on the machine, so a test asserting either side
    ///         of that line has to either burn real time or lose to whatever else the machine is
    ///         doing. With the clock injected it asserts on packets applied per millisecond charged,
    ///         which is the property, and takes no wall time at all.
    ///     </para>
    /// </remarks>
    private readonly TimeProvider _clock;

    private readonly ILogger<Connection> _logger = Log.Instance.For<Connection>();

    private bool _backlogWarned;

    private long _lastReadTimestamp;

    private int _timeout;

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
    public bool betaSharpClient;

    protected bool closed;
    protected bool disconnected;
    protected string disconnectedReason = "";
    protected object[]? disconnectReasonArgs;
    protected NetHandler? netHandler;

    protected bool open = true;
    protected ConcurrentQueue<Packet> readQueue = [];

    protected Connection(IPEndPoint? address = null, TimeProvider? clock = null)
    {
        _address = address;
        _clock = clock ?? TimeProvider.System;
    }

    public long BytesRead { get; protected set; }
    public long BytesWritten { get; protected set; }
    public int PacketsRead { get; protected set; }
    public int PacketsWritten { get; protected set; }

    /// <summary>
    ///     Gap between successive packet arrivals. On a client this is the head-of-line stall the
    ///     snapshot buffer will have to absorb, and it is what sizes the interpolation delay.
    /// </summary>
    public PacketArrivalHistogram ReadIntervals { get; } = new();

    /// <summary>
    ///     Whether packets reach the peer as objects rather than as bytes.
    ///     <para>
    ///         True only for loopback, where <see cref="InternalConnection" /> hands the packet
    ///         straight over. Callers use it to skip work whose entire purpose is to make bytes
    ///         smaller: on this connection there are no bytes, so compressing is pure cost. It
    ///         replaces the <c>ProcessForInternal</c> hook the legacy packets had, which asked the
    ///         same question after the work had already been done and could therefore only discard
    ///         the result.
    ///     </para>
    /// </summary>
    public virtual bool IsInternal => false;

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
    ///     Packets applied to the handler. Against <see cref="PacketsRead" /> this is the drain rate
    ///     versus the arrival rate, and the two diverging is the whole diagnosis.
    /// </summary>
    public long PacketsProcessed { get; private set; }

    /// <summary>
    ///     Ticks whose drain hit <see cref="DrainBudgetMs" /> and stopped early. Non-zero means
    ///     packets are arriving faster than they can be applied, which no other counter shows —
    ///     nothing is dropped, so the totals stay healthy while latency grows.
    /// </summary>
    public long DrainBudgetHits { get; private set; }

    /// <summary>
    ///     The peer's declared protocol revision, or 0 when it declared none.
    ///     <para>
    ///         Zero does not mean vanilla — <see cref="NotePeerCapability" /> can prove capability
    ///         from a packet that carries no version. It means "capable, revision unknown", and
    ///         <see cref="betaSharpClient" /> remains the answer to whether extended packets may be
    ///         sent.
    ///     </para>
    /// </summary>
    public int PeerProtocolVersion { get; private set; }

    public void setNetworkHandler(NetHandler netHandler) => this.netHandler = netHandler;

    /// <summary>
    ///     Sends a packet, subject to the compatibility gate. Subclasses supply the transport.
    /// </summary>
    public virtual void sendPacket(Packet packet)
    {
    }

    /// <summary>
    ///     Sends an extensible-layer message, or drops it when the peer never advertised the key.
    ///     <para>
    ///         Here rather than at each handler because the answer differs by transport, and only
    ///         the transport knows: a real connection has to serialise, and loopback must not. That
    ///         distinction was invisible while the layer carried nine low-rate messages and stops
    ///         being invisible the moment entity replication travels through it.
    ///     </para>
    /// </summary>
    public virtual void sendMessage(MessageRegistry registry, Message message)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var envelope = OmniMessagePacket.For(registry, message);
        if (envelope is not null)
        {
            sendPacket(envelope);
        }
    }

    protected void disconnect(Exception e)
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
        }
    }

    /// <summary>Applies one packet and counts it. The single drain point for every subclass.</summary>
    protected void ApplyPacket(Packet packet, NetHandler handler)
    {
        PacketsProcessed++;
        packet.Apply(handler);
    }

    public virtual void tick()
    {
        var depth = readQueue.Count;
        if (depth > PeakReadQueueDepth)
        {
            PeakReadQueueDepth = depth;
        }

        // Warn once at a depth that is unambiguously a losing race, so the log says what the overlay
        // says. There is nothing to shed here — the bytes are already received.
        if (depth > BacklogWarningDepth && !_backlogWarned)
        {
            _backlogWarned = true;
            _logger.LogWarning(
                "Read backlog of {Depth} packets: arriving faster than they can be applied, so positions are being applied late.",
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

    protected virtual void processPackets()
    {
        if (netHandler == null)
        {
            throw new Exception("networkHandler is null");
        }

        var start = _clock.GetTimestamp();
        var sinceCheck = 0;

        while (readQueue.TryDequeue(out var packet))
        {
            ApplyPacket(packet, netHandler);

            if (++sinceCheck < BudgetCheckInterval)
            {
                continue;
            }

            sinceCheck = 0;

            if (_clock.GetElapsedTime(start).TotalMilliseconds >= DrainBudgetMs)
            {
                DrainBudgetHits++;
                break;
            }
        }
    }

    public virtual IPEndPoint? getAddress() => _address;

    public virtual void disconnect()
    {
        closed = true;
        disconnect(new Exception("disconnect.closed"));
    }

    /// <summary>
    ///     Packets queued in the transport for world data — chunks and block updates.
    ///     <para>
    ///         <b>Returned 0 unconditionally between the UDP cutover and this.</b> The stream
    ///         transport answered it from its own send queue; that queue went away with the socket
    ///         and the accessor was stubbed rather than re-pointed, so every caller pacing against it
    ///         silently stopped pacing. The visible consequence was chunk streaming: its throttle is
    ///         expressed against this number, so a join handed the entire view distance to the
    ///         transport in a single tick.
    ///     </para>
    /// </summary>
    public virtual int getWorldPacketBacklog() => 0;

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
    ///     Records an explicit declaration: the peer speaks the protocol, and says which revision.
    ///     Both ends call this — the server decoding the login field, the client reading the
    ///     server's registry sync — so the version is known in both directions rather than inferred
    ///     on one side only.
    /// </summary>
    public void NotePeerProtocol(int version)
    {
        betaSharpClient = true;
        PeerProtocolVersion = version;
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

    /// <summary>
    ///     Books in one received packet: arrival interval, capability, envelope stamp, counters,
    ///     queue. Every transport funnels through here so the bookkeeping cannot drift between them.
    /// </summary>
    /// <param name="arrivalTicks">
    ///     When the bytes landed, taken by the transport on whatever thread received them — never
    ///     here. This runs on the game thread up to a tick later, and a stamp taken at that point
    ///     measures the tick phase rather than the network.
    /// </param>
    protected void AcceptIncoming(Packet packet, long arrivalTicks)
    {
        if (_lastReadTimestamp != 0)
        {
            ReadIntervals.Record(MonotonicClock.ElapsedMs(_lastReadTimestamp, arrivalTicks));
        }

        _lastReadTimestamp = arrivalTicks;

        NotePeerCapability(packet);
        StampArrival(packet, arrivalTicks);

        BytesRead += packet.Size();
        PacketsRead++;
        readQueue.Enqueue(packet);
    }
}
