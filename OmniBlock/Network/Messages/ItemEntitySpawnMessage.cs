namespace OmniBlock.Network.Messages;

/// <summary>
///     Spawns a dropped item. Replaces <c>ItemEntitySpawnS2CPacket</c>.
/// </summary>
public sealed class ItemEntitySpawnMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "item_entity_spawn");
    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    public short ItemRawId { get; set; }

    public sbyte ItemCount { get; set; }

    public short ItemDamage { get; set; }

    /// <summary>Fixed point in sixteenths of a block.</summary>
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    /// <summary>Blocks per tick times 128 — a coarser scale than the other spawns, and the one the drop toss uses.</summary>
    public sbyte VelocityX { get; set; }

    public sbyte VelocityY { get; set; }

    public sbyte VelocityZ { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        ItemRawId = stream.ReadShort();
        ItemCount = (sbyte)stream.ReadByte();
        ItemDamage = stream.ReadShort();
        X = stream.ReadInt();
        Y = stream.ReadInt();
        Z = stream.ReadInt();
        VelocityX = (sbyte)stream.ReadByte();
        VelocityY = (sbyte)stream.ReadByte();
        VelocityZ = (sbyte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteShort(ItemRawId);
        stream.WriteByte((byte)ItemCount);
        stream.WriteShort(ItemDamage);
        stream.WriteInt(X);
        stream.WriteInt(Y);
        stream.WriteInt(Z);
        stream.WriteByte((byte)VelocityX);
        stream.WriteByte((byte)VelocityY);
        stream.WriteByte((byte)VelocityZ);
    }

    public override int Size() =>
        4
        + 2
        + 1
        + 2
        + 4
        + 4
        + 4
        + 1
        + 1
        + 1;
}
