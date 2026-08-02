namespace BetaSharp.Network.Messages;

/// <summary>
///     What the server knows about its own health, for the client's debug overlay.
///     <para>
///         Singleplayer never needed this: client and server share a process, so the server writes
///         straight into the same <c>MetricRegistry</c> the overlay reads. Connected to a real
///         server the overlay had nothing to read and said so, which is why every field in it was
///         N/A on a remote session.
///     </para>
///     <para>
///         Sent once a second to every player. The rate is set by the reader, not the writer: the
///         overlay treats a metric older than a few seconds as absent, so anything slower makes the
///         panel blink between values and N/A rather than merely updating less often. At sixteen
///         bytes it is cheaper to send unconditionally than to negotiate who wants it.
///     </para>
/// </summary>
[WireMessage("betasharp:server_status")]
public sealed partial class ServerStatusMessage : Message
{
    /// <summary>
    ///     High, which is safe here for the reason the classification exists: nothing about this
    ///     depends on world data having arrived first, so overtaking a chunk cannot make it wrong.
    ///     Left at Normal it queues behind the chunk stream, and a join saturates that for long
    ///     enough to hold a one-second heartbeat past the staleness window — the panel would read
    ///     N/A during exactly the period somebody watching it wants to see.
    /// </summary>
    public override SendPriority Priority => SendPriority.High;

    /// <summary>Ticks per second over the last second. 20 is the target.</summary>
    [WireField]
    public float Tps { get; set; }

    /// <summary>Milliseconds the most recent tick took.</summary>
    [WireField]
    public float Mspt { get; set; }

    [WireField(Encoding = WireEncoding.VarInt)]
    public int EntityCount { get; set; }

    [WireField(Encoding = WireEncoding.VarInt)]
    public int PlayerCount { get; set; }
}
