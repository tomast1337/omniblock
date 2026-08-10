using OmniBlock;

namespace OmniBlock.Network.Messages;

public class IncreaseStatMessage : Message
{
    public int StatId { get; set; }

    /// <summary>The amount written is a signed byte; the stat itself is interpreted
    /// by the client from its registry entry.</summary>
    public sbyte Amount { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "increase_stat");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        StatId = stream.ReadInt();
        Amount = (sbyte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(StatId);
        stream.WriteByte((byte)Amount);
    }

    public override int Size()
    {
        return
            4
            + 1;
    }
}
