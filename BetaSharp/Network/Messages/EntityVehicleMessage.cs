namespace OmniBlock.Network.Messages;

/// <summary>
///     What an entity is riding, or -1 for dismounting. Replaces
///     <c>EntityVehicleSetS2CPacket</c>.
/// </summary>
[WireMessage("omniblock:entity_vehicle")]
public sealed partial class EntityVehicleMessage : Message
{
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    /// <summary>-1 dismounts.</summary>
    [WireField]
    public int VehicleEntityId { get; set; }
}
