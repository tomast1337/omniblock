using System.Net;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;
using Microsoft.Extensions.Logging;

namespace OmniBlock.Network;

public class InternalConnection : Connection
{
    public InternalConnection RemoteConnection { get; set; }

    public string Name { get; set; }

    private readonly ILogger<InternalConnection> _logger = Log.Instance.For<InternalConnection>();

    public InternalConnection(NetHandler? netHandler, string name)
    {
        this.netHandler = netHandler;
        Name = name;
    }

    public override bool IsInternal => true;

    public void AssignRemote(InternalConnection remote)
    {
        RemoteConnection = remote;
    }

    public override void sendPacket(Packet packet)
    {
        if (!closed)
        {
            int pSize = packet.Size();
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

    protected override void processPackets()
    {
        if (netHandler == null)
        {
            throw new Exception($"InternalConnection is not initialized");
        }

        // No cap here, deliberately: loopback hands packets over directly, so a queue depth is a
        // scheduling artefact rather than a transport backlog and there is nothing to pace against.
        while (readQueue.TryDequeue(out var packet))
        {
            ApplyPacket(packet, netHandler);
        }
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

    public override void disconnect()
    {
        disconnect("Disconnecting");
    }

    public override void tick()
    {
        processPackets();
        if (disconnected && readQueue.IsEmpty)
        {
            netHandler?.onDisconnected(disconnectedReason, disconnectReasonArgs);
        }
    }

    public override IPEndPoint getAddress()
    {
        return new IPEndPoint(IPAddress.Parse("127.0.0.1"), 12345);
    }
}
