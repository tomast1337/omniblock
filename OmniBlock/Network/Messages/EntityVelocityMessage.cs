namespace OmniBlock.Network.Messages;

/// <summary>
///     A velocity the client should adopt outright — knockback, an explosion, a boat's shove.
///     Replaces <c>EntityVelocityUpdateS2CPacket</c>.
///     <para>
///         Clamping to ±3.9 blocks per tick stays with the sender rather than moving here: it is
///         what makes the value fit in a short, and a message that silently truncated instead would
///         turn a fast knockback into a slow one in the opposite direction.
///     </para>
/// </summary>
public sealed class EntityVelocityMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "entity_velocity");
    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    /// <summary>Blocks per tick times 8000.</summary>
    public short MotionX { get; set; }

    public short MotionY { get; set; }

    public short MotionZ { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        MotionX = stream.ReadShort();
        MotionY = stream.ReadShort();
        MotionZ = stream.ReadShort();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteShort(MotionX);
        stream.WriteShort(MotionY);
        stream.WriteShort(MotionZ);
    }

    public override int Size() =>
        4
        + 2
        + 2
        + 2;
}
