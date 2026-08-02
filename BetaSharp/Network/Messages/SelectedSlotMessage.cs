namespace BetaSharp.Network.Messages;

/// <summary>
///     Which hotbar slot the player has selected. Replaces <c>UpdateSelectedSlotC2SPacket</c>.
/// </summary>
[WireMessage("betasharp:selected_slot")]
public sealed partial class SelectedSlotMessage : Message
{
    [WireField]
    public short Slot { get; set; }
}
