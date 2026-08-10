namespace OmniBlock.Network.Messages;

/// <summary>
///     Probes the network for clock offset and round-trip time. The client sends these in a burst
///     during login and then periodically; the server echoes what it cannot derive.
///     <para>
///         T1 — the server's arrival instant — is not a field here. It arrives on the envelope as
///         <see cref="Message.TransportReceivedAtMs" />, stamped on the read thread before the
///         packet is queued, because the handler runs on the game thread up to a tick later and
///         would measure the tick phase rather than the network.
///     </para>
/// </summary>
public sealed class TimeSyncRequestMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "time_sync_request");

    /// <summary>Latency measurement: a probe queued behind a chunk measures the queue, not the network.</summary>
    public override SendPriority Priority => SendPriority.High;

    /// <summary>Rolling serial, for diagnostics and to guard against reordered responses.</summary>
    public uint Sequence { get; set; }

    /// <summary>Client's monotonic clock when this was sent — T0.</summary>
    public long ClientSendTime { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

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

    public override int Size() =>
        4
        + 8;
}
