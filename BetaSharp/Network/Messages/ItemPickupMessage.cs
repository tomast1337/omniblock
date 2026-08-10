namespace OmniBlock.Network.Messages;

/// <summary>
///     An item entity flying into whoever picked it up. Replaces
///     <c>ItemPickupAnimationS2CPacket</c>.
/// </summary>
[WireMessage("omniblock:item_pickup")]
public sealed partial class ItemPickupMessage : Message
{
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    [WireField]
    public int CollectorEntityId { get; set; }
}
