using OmniBlock;

namespace OmniBlock.Network.Messages;

public class WorldEventMessage : Message
{
    public int EventId { get; set; }

    public int X { get; set; }

    /// <summary>Y on the wire is a signed byte — world events target a block position.</summary>
    public sbyte Y { get; set; }

    public int Z { get; set; }

    public int Data { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "world_event");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EventId = stream.ReadInt();
        X = stream.ReadInt();
        Y = (sbyte)stream.ReadByte();
        Z = stream.ReadInt();
        Data = stream.ReadInt();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EventId);
        stream.WriteInt(X);
        stream.WriteByte((byte)Y);
        stream.WriteInt(Z);
        stream.WriteInt(Data);
    }

    public override int Size()
    {
        return
            4
            + 4
            + 1
            + 4
            + 4;
    }
}
