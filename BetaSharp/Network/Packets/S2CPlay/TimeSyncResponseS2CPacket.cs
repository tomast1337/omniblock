namespace BetaSharp.Network.Packets.S2CPlay;

/// <summary>
///     Echoes a time-sync probe back to the client with the server's timestamps. The client
///     completes the measurement with T3 (its own arrival time) and derives RTT and clock offset.
///     <para>
///         <c>T2 - T1</c> subtracts the server's internal handling delay from the measured RTT.
///         Stamping T1 on the read path and T2 on the write path makes that subtraction honest.
///     </para>
///     <para>See <c>docs/time-sync-and-interpolation.md</c> §1.</para>
/// </summary>
public class TimeSyncResponseS2CPacket() : ExtendedProtocolPacket(PacketId.TimeSyncResponse)
{
    /// <summary>Echoed verbatim so the client can match the probe.</summary>
    public long ClientSendTime { get; private set; }

    /// <summary>Echoed verbatim; for the client's own diagnostics.</summary>
    public uint Sequence { get; private set; }

    /// <summary>Server monotonic ms, stamped when the request was read off the wire.</summary>
    public long ServerRecvTime { get; private set; }

    /// <summary>Server monotonic ms, stamped by <c>Connection.WritePacket</c> immediately before
    /// writing. <c>internal set</c> so that only <c>Connection</c> (same assembly) can set it;
    /// the handler leaves it zero and the write path fills it in.</summary>
    public long ServerSendTime { get; internal set; }

    /// <summary>
    ///     Client's T3, stamped by <c>Connection.Reading</c> on the read thread before the packet
    ///     is queued. Not serialised — it is set on the received instance after deserialisation, so
    ///     it never travels. The handler reads it rather than stamping its own T3, because the
    ///     handler runs on the game thread up to a tick later and would measure the tick phase
    ///     instead of the network.
    /// </summary>
    public long ClientRecvTime { get; internal set; }

    public static TimeSyncResponseS2CPacket Get(
        uint sequence, long clientSendTime, long serverRecvTime, long serverSendTime)
    {
        TimeSyncResponseS2CPacket p = Get<TimeSyncResponseS2CPacket>(PacketId.TimeSyncResponse);
        p.Sequence = sequence;
        p.ClientSendTime = clientSendTime;
        p.ServerRecvTime = serverRecvTime;
        p.ServerSendTime = serverSendTime;
        return p;
    }

    public override void Read(Stream stream)
    {
        Sequence = (uint)stream.ReadInt();
        ClientSendTime = stream.ReadLong();
        ServerRecvTime = stream.ReadLong();
        ServerSendTime = stream.ReadLong();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt((int)Sequence);
        stream.WriteLong(ClientSendTime);
        stream.WriteLong(ServerRecvTime);
        stream.WriteLong(ServerSendTime);
    }

    public override void Apply(NetHandler handler) => handler.onTimeSyncResponse(this);

    public override int Size() => sizeof(int) + 3 * sizeof(long);
}
