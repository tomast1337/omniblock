namespace BetaSharp.Network.Messages;

/// <summary>
///     Whether the server agreed with what a click produced. Replaces
///     <c>ScreenHandlerAcknowledgementPacket</c>.
///     <para>
///         Travels both ways: the server answers a click, and the client confirms having applied a
///         correction. The revision is what ties the two to a particular click rather than to the
///         screen as a whole.
///     </para>
/// </summary>
[WireMessage("betasharp:screen_ack")]
public sealed partial class ScreenHandlerAckMessage : Message
{
    [WireField]
    public sbyte SyncId { get; set; }

    /// <summary>The click's revision, matching <c>ClickSlotMessage.ActionType</c>.</summary>
    [WireField]
    public short ActionType { get; set; }

    [WireField]
    public bool Accepted { get; set; }
}
