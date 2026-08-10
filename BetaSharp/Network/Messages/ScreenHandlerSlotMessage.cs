using OmniBlock.Items;

namespace OmniBlock.Network.Messages;

/// <summary>
///     One slot of an open screen. Replaces <c>ScreenHandlerSlotUpdateS2CPacket</c>, which declared
///     a constant 8 bytes for a payload of 5 or 8.
/// </summary>
[WireMessage("omniblock:screen_slot")]
public sealed partial class ScreenHandlerSlotMessage : Message
{
    /// <summary>-1 with slot -1 addresses the cursor stack rather than a screen.</summary>
    [WireField]
    public sbyte SyncId { get; set; }

    [WireField]
    public short Slot { get; set; }

    [WireField]
    public ItemStack? Stack { get; set; }
}
