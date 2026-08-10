namespace OmniBlock.Network.Messages;

/// <summary>
///     A tracked entity's absolute position, for when a relative delta cannot express the move.
///     Replaces <c>EntityPositionS2CPacket</c>.
///     <para>
///         The packet declared 34 bytes for a payload of 18. Harmless under a framing with no
///         length; the generated size is measured.
///     </para>
/// </summary>
public sealed class EntityTeleportMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "entity_teleport");
    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    /// <summary>Fixed point in sixteenths of a block.</summary>
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public sbyte Yaw { get; set; }

    public sbyte Pitch { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        X = stream.ReadInt();
        Y = stream.ReadInt();
        Z = stream.ReadInt();
        Yaw = (sbyte)stream.ReadByte();
        Pitch = (sbyte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteInt(X);
        stream.WriteInt(Y);
        stream.WriteInt(Z);
        stream.WriteByte((byte)Yaw);
        stream.WriteByte((byte)Pitch);
    }

    public override int Size() =>
        4
        + 4
        + 4
        + 4
        + 1
        + 1;
}
