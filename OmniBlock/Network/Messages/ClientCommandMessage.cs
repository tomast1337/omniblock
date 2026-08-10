namespace OmniBlock.Network.Messages;

/// <summary>
///     A state change the client is asking for: respawn, open inventory, leave a bed.
///     Replaces <c>ClientCommandC2SPacket</c>.
/// </summary>
[WireMessage("omniblock:client_command")]
public sealed partial class ClientCommandMessage : Message
{
    [WireField]
    public int EntityId { get; set; }

    /// <summary>1 respawns, 2 opens the inventory, 3 leaves a bed.</summary>
    [WireField]
    public sbyte Mode { get; set; }
}
