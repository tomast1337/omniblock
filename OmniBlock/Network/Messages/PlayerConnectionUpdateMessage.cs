using OmniBlock;
using OmniBlock.Util;

namespace OmniBlock.Network.Messages;

public class PlayerConnectionUpdateMessage : Message
{
    public enum UpdateType : byte
    {
        Join = 0,
        Leave = 1
    }

    public int EntityId { get; set; }

    public UpdateType Type { get; set; }

    /// <summary>Player name, bounded to 16 characters — matches Beta 1.7.3's limit.</summary>
    public string Name { get; set; } = "";

    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "player_connection_update");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        Type = (UpdateType)((byte)stream.ReadByte());
        Name = stream.ReadString(16);
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteByte(((byte)Type));
        stream.WriteString(Name);
    }

    public override int Size()
    {
        return
            4
            + 1
            + (2 + ModifiedUtf8.GetByteCount(Name));
    }
}
