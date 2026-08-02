using BetaSharp.Items;

namespace BetaSharp.Network.Messages;

/// <summary>
///     A click inside an open screen. Replaces <c>ClickSlotC2SPacket</c>.
///     <para>
///         The stack the client thinks the click produced is what the server checks its own result
///         against; a mismatch is what <c>ScreenHandlerAcknowledgementPacket</c> answers. Nothing
///         here is trusted on its own.
///     </para>
/// </summary>
[WireMessage("betasharp:click_slot")]
public sealed partial class ClickSlotMessage : Message
{
    [WireField]
    public sbyte SyncId { get; set; }

    [WireField]
    public short Slot { get; set; }

    [WireField]
    public sbyte Button { get; set; }

    /// <summary>Revision, matched by the acknowledgement the server sends back.</summary>
    [WireField]
    public short ActionType { get; set; }

    [WireField]
    public bool HoldingShift { get; set; }

    [WireField]
    public ItemStack? Stack { get; set; }
}
