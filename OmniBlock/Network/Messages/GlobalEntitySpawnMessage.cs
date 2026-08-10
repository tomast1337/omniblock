namespace OmniBlock.Network.Messages;

/// <summary>
///     Spawns an entity every player in the dimension sees, wherever they are — lightning.
///     Replaces <c>GlobalEntitySpawnS2CPacket</c>.
/// </summary>
public sealed class GlobalEntitySpawnMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "global_entity_spawn");
    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    public byte Type { get; set; }

    /// <summary>Fixed point in sixteenths of a block.</summary>
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        Type = (byte)stream.ReadByte();
        X = stream.ReadInt();
        Y = stream.ReadInt();
        Z = stream.ReadInt();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteByte(Type);
        stream.WriteInt(X);
        stream.WriteInt(Y);
        stream.WriteInt(Z);
    }

    public override int Size() =>
        4
        + 1
        + 4
        + 4
        + 4;
}
