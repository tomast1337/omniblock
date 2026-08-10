namespace OmniBlock.Network.Messages;

/// <summary>
///     A player's action against a block, or a drop.
///     <para>
///         Replaces <c>PlayerActionC2SPacket</c>. The payload is unchanged byte for byte; what
///         changes is that it no longer occupies one of the 256 slots in <c>PacketId</c>, where a
///         mod adding an action would have to pick a number and hope.
///     </para>
///     <para>
///         <b>Y is a byte and X and Z are not.</b> That asymmetry is the world's: it is 128 blocks
///         tall and unbounded horizontally. It was already the encoding; naming the field a byte
///         merely stops the reader having to notice that <c>stream.ReadByte()</c> into an
///         <c>int</c> meant the range was never the field's type.
///     </para>
/// </summary>
public sealed class PlayerActionMessage : Message
{
    public enum Actions : byte
    {
        BlockClick = 0,
        BlockBroken = 2,
        DropSelectedItem = 4
    }

    /// <summary>
    ///     The named actions. Not exhaustive — 1 and 3 are also sent — which is why the field is a
    ///     byte rather than this enum: the wire carries values this type has no name for, and
    ///     decoding into an enum that cannot represent them would be a lie the compiler believes.
    /// </summary>
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "player_action");

    public byte Action { get; set; }

    public int X { get; set; }

    public byte Y { get; set; }

    public int Z { get; set; }

    public byte Direction { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        Action = (byte)stream.ReadByte();
        X = stream.ReadInt();
        Y = (byte)stream.ReadByte();
        Z = stream.ReadInt();
        Direction = (byte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteByte(Action);
        stream.WriteInt(X);
        stream.WriteByte(Y);
        stream.WriteInt(Z);
        stream.WriteByte(Direction);
    }

    public override int Size() =>
        1
        + 4
        + 1
        + 4
        + 1;
}
