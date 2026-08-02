namespace BetaSharp.Network.Messages;

/// <summary>
///     The changed entries of an entity's <c>DataSynchronizer</c>. Replaces
///     <c>EntityTrackerUpdateS2CPacket</c>.
///     <para>
///         <b>Length-prefixed rather than terminated.</b> The packet wrote the properties and then a
///         127 byte, and the reader consumed until it saw one. That works only while no property can
///         encode a 127 in a position the scanner will read as a terminator, which is a property of
///         the payload rather than of the framing — exactly the kind of coupling
///         <c>docs/network-rewrite.md</c> §6 is about. A length says the same thing and cannot be
///         confused by its own contents.
///     </para>
/// </summary>
[WireMessage("betasharp:entity_data")]
public sealed partial class EntityDataMessage : Message
{
    /// <summary>
    ///     Well above what 32 synchronised properties can produce, and finite, which is the part
    ///     that matters: the length decides an allocation.
    /// </summary>
    public const int MaxDataBytes = 4096;

    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    [WireField(MaxLength = MaxDataBytes)]
    public byte[] Data { get; set; } = [];
}
