namespace BetaSharp.Network.Messages;

/// <summary>
///     A tracked value on an open screen — a furnace's burn time, a brewing stand's progress.
///     Replaces <c>ScreenHandlerPropertyUpdateS2CPacket</c>.
/// </summary>
[WireMessage("betasharp:screen_property")]
public sealed partial class ScreenHandlerPropertyMessage : Message
{
    [WireField]
    public sbyte SyncId { get; set; }

    [WireField]
    public short PropertyId { get; set; }

    [WireField]
    public short Value { get; set; }
}
