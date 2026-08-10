namespace OmniBlock.Network.Messages;

/// <summary>
///     Closes an open screen. Replaces <c>CloseScreenS2CPacket</c>, and travels both ways: the
///     client says it has closed one, the server says one is no longer valid.
/// </summary>
[WireMessage("omniblock:close_screen")]
public sealed partial class CloseScreenMessage : Message
{
    [WireField]
    public sbyte SyncId { get; set; }
}
