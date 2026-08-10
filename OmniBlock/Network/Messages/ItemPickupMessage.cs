namespace OmniBlock.Network.Messages;

/// <summary>
///     An item entity flying into whoever picked it up. Replaces
///     <c>ItemPickupAnimationS2CPacket</c>.
/// </summary>
public sealed class ItemPickupMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "item_pickup");
    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    public int CollectorEntityId { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        CollectorEntityId = stream.ReadInt();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteInt(CollectorEntityId);
    }

    public override int Size() =>
        4
        + 4;
}
