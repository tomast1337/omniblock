namespace OmniBlock.Network.Messages;

[WireMessage("beta:game_state_change")]
public partial class GameStateChangeMessage : Message
{
    /// <summary>
    ///     Human-readable reason strings indexed by reason code, for the three reasons
    ///     Beta 1.7.3 knows. A null entry means the reason has no associated message.
    /// </summary>
    public static readonly string?[] Reasons = ["tile.bed.notValid", null, null];

    [WireField]
    public sbyte Reason { get; set; }
}
