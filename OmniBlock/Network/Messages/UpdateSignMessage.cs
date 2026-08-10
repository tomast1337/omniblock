using OmniBlock;
using OmniBlock.Util;

namespace OmniBlock.Network.Messages;

/// <summary>
///     The four lines of a sign. Replaces <c>UpdateSignPacket</c>, and travels both ways: the client
///     submits an edit, the server hands the text out with the chunk.
///     <para>
///         <b>Four fields rather than an array.</b> A sign has exactly four lines — that is the
///         domain's shape, not an encoding choice — and the packet's writer proved the point by
///         wrapping each line in a <c>try</c>/<c>catch</c> to survive an array that was not four
///         long. Four named fields cannot be the wrong length.
///     </para>
///     <para>
///         Its <c>Size()</c> returned the sum of the four line lengths and nothing else, omitting
///         the position, the four length prefixes and the UTF-16 doubling — every term but one.
///     </para>
/// </summary>
public sealed class UpdateSignMessage : Message
{
    /// <summary>What a sign renders before it starts clipping, and the bound the reader applies.</summary>
    public const int MaxLineBytes = 15;

    public int X { get; set; }

    public short Y { get; set; }

    public int Z { get; set; }

    public string Line0 { get; set; } = string.Empty;

    public string Line1 { get; set; } = string.Empty;

    public string Line2 { get; set; } = string.Empty;

    public string Line3 { get; set; } = string.Empty;

    /// <summary>
    ///     The four lines as an array, for the call sites that hold them that way. Not itself part
    ///     of the wire payload: it is a view over the four fields that are, and serialising it as
    ///     well would put every line on the wire twice.
    /// </summary>
    public string[] Lines
    {
        get => [Line0, Line1, Line2, Line3];

        set
        {
            ArgumentNullException.ThrowIfNull(value);

            // Short arrays are padded rather than rejected. The packet swallowed an
            // IndexOutOfRangeException per line to achieve the same thing, which worked and said
            // nothing about why.
            Line0 = value.Length > 0 ? value[0] : string.Empty;
            Line1 = value.Length > 1 ? value[1] : string.Empty;
            Line2 = value.Length > 2 ? value[2] : string.Empty;
            Line3 = value.Length > 3 ? value[3] : string.Empty;
        }
    }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "update_sign");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        X = stream.ReadInt();
        Y = stream.ReadShort();
        Z = stream.ReadInt();
        Line0 = stream.ReadString(15);
        Line1 = stream.ReadString(15);
        Line2 = stream.ReadString(15);
        Line3 = stream.ReadString(15);
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(X);
        stream.WriteShort(Y);
        stream.WriteInt(Z);
        stream.WriteString(Line0);
        stream.WriteString(Line1);
        stream.WriteString(Line2);
        stream.WriteString(Line3);
    }

    public override int Size()
    {
        return
            4
            + 2
            + 4
            + (2 + ModifiedUtf8.GetByteCount(Line0))
            + (2 + ModifiedUtf8.GetByteCount(Line1))
            + (2 + ModifiedUtf8.GetByteCount(Line2))
            + (2 + ModifiedUtf8.GetByteCount(Line3));
    }
}
