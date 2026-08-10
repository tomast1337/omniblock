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
[WireMessage("omniblock:player_action")]
public sealed partial class PlayerActionMessage : Message
{
    [WireField]
    public byte Action { get; set; }

    [WireField]
    public int X { get; set; }

    [WireField]
    public byte Y { get; set; }

    [WireField]
    public int Z { get; set; }

    [WireField]
    public byte Direction { get; set; }

    /// <summary>
    ///     The named actions. Not exhaustive — 1 and 3 are also sent — which is why the field is a
    ///     byte rather than this enum: the wire carries values this type has no name for, and
    ///     decoding into an enum that cannot represent them would be a lie the compiler believes.
    /// </summary>
    public enum Actions : byte
    {
        BlockClick = 0,
        BlockBroken = 2,
        DropSelectedItem = 4,
    }
}
