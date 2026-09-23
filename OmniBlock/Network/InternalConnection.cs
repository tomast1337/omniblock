using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Network;

public class InternalConnection : Connection
{
    // The integrated server can produce several bounded response batches while the render thread
    // is inside one expensive cold-LOD frame. Catch up those already-produced replies without
    // making the application queue unbounded. This isolated lane currently carries only terrain
    // LOD messages; actual tile ownership remains limited by the client's sixteen outstanding
    // requests. Ordinary chunks and gameplay stay in the normal lane above.
    internal const int MaximumBulkPacketsPerTick =
        TerrainLodScaleBudget.MaximumLoopbackBulkPacketsPerTick;

    private readonly ILogger<InternalConnection> _logger = Log.Instance.For<InternalConnection>();
    private readonly ConcurrentQueue<Packet> _bulkReadQueue = [];
    private int _peakBulkReadQueueDepth;

    public InternalConnection(NetHandler? netHandler, string name, TimeProvider? clock = null)
        : base(clock: clock)
    {
        this.netHandler = netHandler;
        Name = name;
    }

    public InternalConnection? RemoteConnection { get; set; }

    public string Name { get; set; }

    public override bool IsInternal => true;

    protected override int AdditionalReadQueueDepth => _bulkReadQueue.Count;

    public override int BulkReadQueueDepth => _bulkReadQueue.Count;

    public override int PeakBulkReadQueueDepth => Volatile.Read(ref _peakBulkReadQueueDepth);

    public void AssignRemote(InternalConnection remote) => RemoteConnection = remote;

    public override void sendPacket(Packet packet)
    {
        if (!closed)
        {
            var pSize = packet.Size();
            BytesWritten += pSize;
            PacketsWritten++;

            if (RemoteConnection != null && !RemoteConnection.closed)
            {
                RemoteConnection.ReceivePacket(packet);
            }
        }
    }

    /// <summary>
    ///     Hands the message over as an object. No ID is assigned and no bytes are produced, which
    ///     is the same deal loopback already gives every legacy packet — and the reason migrating
    ///     entity replication to messages does not make singleplayer pay for a wire it does not have.
    /// </summary>
    public override void sendMessage(MessageRegistry registry, Message message) =>
        sendPacket(OmniMessagePacket.Loopback(message));

    protected void ReceivePacket(Packet packet)
    {
        BytesRead += packet.Size();
        PacketsRead++;
        if (PacketPriorities.Of(packet) == SendPriority.Bulk)
        {
            _bulkReadQueue.Enqueue(packet);
            NoteBulkQueueDepth(_bulkReadQueue.Count);
        }
        else
            readQueue.Enqueue(packet);
    }

    private void NoteBulkQueueDepth(int depth)
    {
        var current = Volatile.Read(ref _peakBulkReadQueueDepth);
        while (depth > current)
        {
            var observed = Interlocked.CompareExchange(
                ref _peakBulkReadQueueDepth, depth, current);
            if (observed == current) return;
            current = observed;
        }
    }

    protected override void processPackets()
    {
        // Loopback has no transport channels, so preserve the same priority contract with two
        // application queues. Drain gameplay/control first, then admit only the amount of bulk
        // work its bounded consumer can accept in one tick. Testing only at tick entry starves
        // this lane under a continuous tick-stamp/entity stream even though the normal queue is
        // drained each tick.
        base.processPackets();
        // The normal lane has already received its complete wall-clock budget. Do not require it
        // to become empty: initial chunk streaming can keep that queue non-empty for many frames,
        // which used to starve the independent LOD lane despite its producer and consumer both
        // being bounded.
        if (_bulkReadQueue.IsEmpty) return;
        if (netHandler is null) throw new Exception("networkHandler is null");
        for (var admitted = 0; admitted < MaximumBulkPacketsPerTick &&
             _bulkReadQueue.TryDequeue(out var bulk); admitted++)
            ApplyPacket(bulk, netHandler);
    }

    public override void disconnect(string disconnectedReason, params object[] disconnectReasonArgs)
    {
        if (open)
        {
            open = false;
            disconnected = true;
            this.disconnectedReason = disconnectedReason;
            this.disconnectReasonArgs = disconnectReasonArgs;

            _logger.LogInformation($"[{Name}] Disconnected: {disconnectedReason}");

            if (RemoteConnection != null && RemoteConnection.open)
            {
                RemoteConnection.OnRemoteDisconnect(disconnectedReason, disconnectReasonArgs);
            }
        }
    }

    public void OnRemoteDisconnect(string reason, object[] args)
    {
        if (open)
        {
            open = false;
            disconnected = true;
            disconnectedReason = reason;
            disconnectReasonArgs = args;
            _logger.LogInformation($"[{Name}] Remote disconnected: {reason}");
        }
    }

    public override void disconnect() => disconnect("Disconnecting");

    /// <summary>
    ///     Loopback has no socket queue, but it does have the peer's application queue. Reporting
    ///     that depth gives chunk streaming the same consumer-side backpressure as a real
    ///     transport. Without it, distance 32 can enqueue thousands of decoded chunks while the
    ///     renderer is still meshing the first rings.
    /// </summary>
    public override int getWorldPacketBacklog() => RemoteConnection?.NormalReadQueueDepth ?? 0;

    // The isolated application lane is also the direct backpressure signal for its producer.
    public override int getBulkPacketBacklog() => RemoteConnection?._bulkReadQueue.Count ?? 0;

    public override IPEndPoint getAddress() => new(IPAddress.Parse("127.0.0.1"), 12345);
}
