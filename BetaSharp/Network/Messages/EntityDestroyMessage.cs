namespace BetaSharp.Network.Messages;

/// <summary>
///     An entity has left this client's view. Replaces <c>EntityDestroyS2CPacket</c>.
///     <para>
///         Both peers drop it from their snapshot baseline on this event, in the same order, which
///         is the one thing the delta encoding cannot detect for itself: a baseline that still holds
///         an entity the other side has forgotten decodes to a plausible wrong position rather than
///         to an error.
///     </para>
/// </summary>
[WireMessage("betasharp:entity_destroy")]
public sealed partial class EntityDestroyMessage : Message
{
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }
}
