namespace BetaSharp.Network.Messages;

[WireMessage("beta:player_sleep_update")]
public partial class PlayerSleepUpdateMessage : Message
{
    [WireField]
    public int PlayerId { get; set; }

    /// <summary>0 when entering a bed; the exact meaning of other values is unknown.</summary>
    [WireField]
    public sbyte Status { get; set; }

    [WireField]
    public int X { get; set; }

    /// <summary>The bed's Y coordinate. Stored as a signed byte on the wire because
    /// Beta 1.7.3's world height was 128 blocks, so 0–127 fits in an sbyte.</summary>
    [WireField]
    public sbyte Y { get; set; }

    [WireField]
    public int Z { get; set; }
}
