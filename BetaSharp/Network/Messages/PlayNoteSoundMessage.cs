namespace BetaSharp.Network.Messages;

[WireMessage("beta:play_note_sound")]
public partial class PlayNoteSoundMessage : Message
{
    [WireField]
    public int X { get; set; }

    /// <summary>Y travels as a short on the wire — note block positions need the extra range.</summary>
    [WireField]
    public short Y { get; set; }

    [WireField]
    public int Z { get; set; }

    [WireField]
    public byte Instrument { get; set; }

    [WireField]
    public byte Pitch { get; set; }
}
