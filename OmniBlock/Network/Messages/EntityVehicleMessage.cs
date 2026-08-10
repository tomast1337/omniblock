namespace OmniBlock.Network.Messages;

/// <summary>
///     What an entity is riding, or -1 for dismounting. Replaces
///     <c>EntityVehicleSetS2CPacket</c>.
/// </summary>
public sealed class EntityVehicleMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "entity_vehicle");
    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    /// <summary>-1 dismounts.</summary>
    public int VehicleEntityId { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        VehicleEntityId = stream.ReadInt();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteInt(VehicleEntityId);
    }

    public override int Size() =>
        4
        + 4;
}
