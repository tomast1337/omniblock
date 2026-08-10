namespace OmniBlock.Network.Messages;

/// <summary>
///     An entity has left this client's view. Replaces <c>EntityDestroyS2CPacket</c>.
///     <para>
///         Both peers drop it from their snapshot baseline on this event, in the same order, which
///         is the one thing the delta encoding cannot detect for itself: a baseline that still holds
///         an entity the other side has forgotten decodes to a plausible wrong position rather than
///         to an error.
///     </para>
/// </summary>
public sealed class EntityDestroyMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "entity_destroy");
    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream) => EntityId = stream.ReadInt();

    public override void Write(Stream stream) => stream.WriteInt(EntityId);

    public override int Size() => 4;
}
