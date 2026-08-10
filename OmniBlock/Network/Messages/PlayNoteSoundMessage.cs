using OmniBlock;

namespace OmniBlock.Network.Messages;

public class PlayNoteSoundMessage : Message
{
    public int X { get; set; }

    /// <summary>Y travels as a short on the wire — note block positions need the extra range.</summary>
    public short Y { get; set; }

    public int Z { get; set; }

    public byte Instrument { get; set; }

    public byte Pitch { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "play_note_sound");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        X = stream.ReadInt();
        Y = stream.ReadShort();
        Z = stream.ReadInt();
        Instrument = (byte)stream.ReadByte();
        Pitch = (byte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(X);
        stream.WriteShort(Y);
        stream.WriteInt(Z);
        stream.WriteByte(Instrument);
        stream.WriteByte(Pitch);
    }

    public override int Size()
    {
        return
            4
            + 2
            + 4
            + 1
            + 1;
    }
}
