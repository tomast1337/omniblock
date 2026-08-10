namespace OmniBlock.Network.Messages;

/// <summary>
///     Spawns a mob, with its whole synchronised data set. Replaces
///     <c>LivingEntitySpawnS2CPacket</c>.
///     <para>
///         The packet declared 20 bytes and wrote 20 plus the data, which is every spawn it ever
///         sent. Length-prefixing the data replaces the 127 terminator for the same reason
///         <see cref="EntityDataMessage" /> did: a terminator makes the framing depend on the
///         payload not happening to contain it.
///     </para>
/// </summary>
public sealed class LivingEntitySpawnMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "living_entity_spawn");
    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    public sbyte Type { get; set; }

    /// <summary>Fixed point in sixteenths of a block.</summary>
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public sbyte Yaw { get; set; }

    public sbyte Pitch { get; set; }

    /// <summary>The full <c>DataSynchronizer</c> state, not a delta — this is the entity's first sight.</summary>
    public byte[] Data { get; set; } = [];

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        Type = (sbyte)stream.ReadByte();
        X = stream.ReadInt();
        Y = stream.ReadInt();
        Z = stream.ReadInt();
        Yaw = (sbyte)stream.ReadByte();
        Pitch = (sbyte)stream.ReadByte();
        Data = stream.ReadByteArray(4096);
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteByte((byte)Type);
        stream.WriteInt(X);
        stream.WriteInt(Y);
        stream.WriteInt(Z);
        stream.WriteByte((byte)Yaw);
        stream.WriteByte((byte)Pitch);
        stream.WriteByteArray(Data);
    }

    public override int Size() =>
        4
        + 1
        + 4
        + 4
        + 4
        + 1
        + 1
        + StreamExtensions.ByteArraySize(Data);
}
