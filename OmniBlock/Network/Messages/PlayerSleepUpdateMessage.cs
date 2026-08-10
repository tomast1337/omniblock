namespace OmniBlock.Network.Messages;

public class PlayerSleepUpdateMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "player_sleep_update");
    public int PlayerId { get; set; }

    /// <summary>0 when entering a bed; the exact meaning of other values is unknown.</summary>
    public sbyte Status { get; set; }

    public int X { get; set; }

    /// <summary>
    ///     The bed's Y coordinate. Stored as a signed byte on the wire because
    ///     Beta 1.7.3's world height was 128 blocks, so 0–127 fits in an sbyte.
    /// </summary>
    public sbyte Y { get; set; }

    public int Z { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        PlayerId = stream.ReadInt();
        Status = (sbyte)stream.ReadByte();
        X = stream.ReadInt();
        Y = (sbyte)stream.ReadByte();
        Z = stream.ReadInt();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(PlayerId);
        stream.WriteByte((byte)Status);
        stream.WriteInt(X);
        stream.WriteByte((byte)Y);
        stream.WriteInt(Z);
    }

    public override int Size() =>
        4
        + 1
        + 4
        + 1
        + 4;
}
