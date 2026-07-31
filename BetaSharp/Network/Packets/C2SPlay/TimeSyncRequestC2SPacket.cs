namespace BetaSharp.Network.Packets.C2SPlay;

/// <summary>
///     Probes the network for clock offset and round-trip time. Client sends these in a burst
///     during login and then periodically; the server echoes T0 and stamps T1/T2 on the read and
///     write sides.
///     <para>
///         A <see cref="ExtendedProtocolPacket" /> so a non-OmniBlock client never receives one.
///         The server is stateless: it echoes every field it cannot derive and the client validates
///         the response against its own pending table.
///     </para>
///     <para>See <c>docs/time-sync-and-interpolation.md</c> §1.</para>
/// </summary>
public class TimeSyncRequestC2SPacket() : ExtendedProtocolPacket(PacketId.TimeSyncRequest)
{
    /// <summary>Client's monotonic clock when this was sent.</summary>
    public long ClientSendTime { get; private set; }

    /// <summary>
    ///     Rolling serial. For diagnostics and to guard against reordered responses.
    /// </summary>
    public uint Sequence { get; private set; }

    /// <summary>
    ///     Server's T1, stamped by <c>Connection.Reading</c> on the read thread before the packet
    ///     is queued. Not serialised — set on the received instance after deserialisation.
    ///     See <see cref="TimeSyncResponseS2CPacket.ClientRecvTime" /> for why this matters.
    /// </summary>
    public long ServerRecvTime { get; internal set; }

    public static TimeSyncRequestC2SPacket Get(uint sequence, long clientSendTime)
    {
        TimeSyncRequestC2SPacket p = Get<TimeSyncRequestC2SPacket>(PacketId.TimeSyncRequest);
        p.Sequence = sequence;
        p.ClientSendTime = clientSendTime;
        return p;
    }

    public override void Read(Stream stream)
    {
        Sequence = (uint)stream.ReadInt();
        ClientSendTime = stream.ReadLong();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt((int)Sequence);
        stream.WriteLong(ClientSendTime);
    }

    public override void Apply(NetHandler handler) => handler.onTimeSyncRequest(this);

    public override int Size() => sizeof(int) + sizeof(long);
}
