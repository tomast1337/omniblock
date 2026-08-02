namespace BetaSharp.Network.Messages;

[WireMessage("beta:player_game_mode_update")]
public partial class PlayerGameModeUpdateMessage : Message
{
    [WireField]
    public string GameModeNamespace { get; set; } = "";

    [WireField]
    public string GameModeName { get; set; } = "";
}
