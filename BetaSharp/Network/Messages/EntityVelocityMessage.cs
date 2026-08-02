namespace BetaSharp.Network.Messages;

/// <summary>
///     A velocity the client should adopt outright — knockback, an explosion, a boat's shove.
///     Replaces <c>EntityVelocityUpdateS2CPacket</c>.
///     <para>
///         Clamping to ±3.9 blocks per tick stays with the sender rather than moving here: it is
///         what makes the value fit in a short, and a message that silently truncated instead would
///         turn a fast knockback into a slow one in the opposite direction.
///     </para>
/// </summary>
[WireMessage("betasharp:entity_velocity")]
public sealed partial class EntityVelocityMessage : Message
{
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    /// <summary>Blocks per tick times 8000.</summary>
    [WireField]
    public short MotionX { get; set; }

    [WireField]
    public short MotionY { get; set; }

    [WireField]
    public short MotionZ { get; set; }
}
