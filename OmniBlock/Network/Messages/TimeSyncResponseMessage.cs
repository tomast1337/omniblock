using OmniBlock;

namespace OmniBlock.Network.Messages;

/// <summary>
///     Echoes a completed time-sync probe. The client adds T3 — its own arrival instant — and
///     derives round-trip time and clock offset from the four timestamps.
///     <para>
///         <b>Two of the four are not fields here.</b> T2, the server's send instant, has to be
///         taken as late as possible or the send-queue delay is counted as network time, so it is
///         stamped into the envelope inside the write path and arrives as
///         <see cref="Message.TransportSentAtMs" />. T3 is stamped on the read thread and arrives as
///         <see cref="Message.TransportReceivedAtMs" />. Both are properties of the transport rather
///         than of this message, and putting them in the payload would mean serialising a value
///         before the moment it is supposed to describe.
///     </para>
///     <para>
///         <c>T2 - T1</c> subtracts the server's internal handling delay from the measured
///         round trip. Taking both at the transport edge is what makes that subtraction honest.
///     </para>
/// </summary>
public sealed class TimeSyncResponseMessage : Message
{
    /// <summary>Latency measurement: a probe queued behind a chunk measures the queue, not the network.</summary>
    public override SendPriority Priority => SendPriority.High;

    /// <summary>Asks the transport to stamp T2 into the envelope on its way out.</summary>
    public override bool NeedsSendTimestamp => true;

    /// <summary>Echoed verbatim, for the client's own diagnostics.</summary>
    public uint Sequence { get; set; }

    /// <summary>Echoed verbatim so the client can match the probe — T0.</summary>
    public long ClientSendTime { get; set; }

    /// <summary>The server's arrival instant for the request — T1.</summary>
    public long ServerRecvTime { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "time_sync_response");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        Sequence = (uint)stream.ReadInt();
        ClientSendTime = stream.ReadLong();
        ServerRecvTime = stream.ReadLong();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt((int)Sequence);
        stream.WriteLong(ClientSendTime);
        stream.WriteLong(ServerRecvTime);
    }

    public override int Size()
    {
        return
            4
            + 8
            + 8;
    }
}
