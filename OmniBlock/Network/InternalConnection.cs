using System.Net;
using Microsoft.Extensions.Logging;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;

namespace OmniBlock.Network;

public class InternalConnection : Connection
{
    private readonly ILogger<InternalConnection> _logger = Log.Instance.For<InternalConnection>();

    public InternalConnection(NetHandler? netHandler, string name)
    {
        this.netHandler = netHandler;
        Name = name;
    }

    public InternalConnection RemoteConnection { get; set; }

    public string Name { get; set; }

    public override bool IsInternal => true;

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
        readQueue.Enqueue(packet);
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
    public override int getWorldPacketBacklog() => RemoteConnection?.ReadQueueDepth ?? 0;

    public override IPEndPoint getAddress() => new(IPAddress.Parse("127.0.0.1"), 12345);
}
