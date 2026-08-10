namespace OmniBlock.Network.Messages;

/// <summary>
///     A one-shot animation on an entity. Replaces <c>EntityAnimationPacket</c>.
/// </summary>
public sealed class EntityAnimationMessage : Message
{
    public enum EntityAnimation : byte
    {
        SwingHand = 1,
        Hurt = 2,
        WakeUp = 3,
        Spawn = 4
    }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "entity_animation");
    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    public byte AnimationId { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        AnimationId = (byte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteByte(AnimationId);
    }

    public override int Size() =>
        4
        + 1;
}
