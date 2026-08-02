namespace BetaSharp.Network.Messages;

/// <summary>
///     A tracked entity's absolute position, for when a relative delta cannot express the move.
///     Replaces <c>EntityPositionS2CPacket</c>.
///     <para>
///         The packet declared 34 bytes for a payload of 18. Harmless under a framing with no
///         length; the generated size is measured.
///     </para>
/// </summary>
[WireMessage("betasharp:entity_teleport")]
public sealed partial class EntityTeleportMessage : Message
{
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    /// <summary>Fixed point in sixteenths of a block.</summary>
    [WireField]
    public int X { get; set; }

    [WireField]
    public int Y { get; set; }

    [WireField]
    public int Z { get; set; }

    [WireField]
    public sbyte Yaw { get; set; }

    [WireField]
    public sbyte Pitch { get; set; }
}
