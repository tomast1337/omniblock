using OmniBlock;

namespace OmniBlock.Network.Messages;

/// <summary>
///     One position's block and metadata.
/// </summary>
/// <remarks>
///     Carries no light. It used to, so that a light-only change had something to travel in, and
///     that was the wrong shape twice over: a light byte read for one cell cannot express the pass
///     that lights a whole chunk, and every construction of this message that forgot to set the
///     field wrote a real, destructive zero into the receiver. Light travels as whole sections on
///     <see cref="LightSectionsMessage" />, where there is no per-message field to leave unset.
/// </remarks>
public class BlockUpdateMessage : Message
{
    public int X { get; set; }

    /// <summary>Y coordinate on the wire is a single byte — Beta 1.7.3's world height is 128 blocks.</summary>
    public sbyte Y { get; set; }

    public int Z { get; set; }

    public byte BlockRawId { get; set; }

    public byte BlockMetadata { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "block_update");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        X = stream.ReadInt();
        Y = (sbyte)stream.ReadByte();
        Z = stream.ReadInt();
        BlockRawId = (byte)stream.ReadByte();
        BlockMetadata = (byte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(X);
        stream.WriteByte((byte)Y);
        stream.WriteInt(Z);
        stream.WriteByte(BlockRawId);
        stream.WriteByte(BlockMetadata);
    }

    public override int Size()
    {
        return
            4
            + 1
            + 4
            + 1
            + 1;
    }
}
