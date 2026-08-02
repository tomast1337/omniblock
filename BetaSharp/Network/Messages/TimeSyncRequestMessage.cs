namespace BetaSharp.Network.Messages;

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
[WireMessage("betasharp:time_sync_request")]
public sealed partial class TimeSyncRequestMessage : Message
{
    /// <summary>Latency measurement: a probe queued behind a chunk measures the queue, not the network.</summary>
    public override SendPriority Priority => SendPriority.High;

    /// <summary>Rolling serial, for diagnostics and to guard against reordered responses.</summary>
    [WireField]
    public uint Sequence { get; set; }

    /// <summary>Client's monotonic clock when this was sent — T0.</summary>
    [WireField]
    public long ClientSendTime { get; set; }
}
