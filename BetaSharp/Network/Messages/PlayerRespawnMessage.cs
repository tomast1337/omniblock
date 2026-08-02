namespace BetaSharp.Network.Messages;

[WireMessage("beta:player_respawn")]
public partial class PlayerRespawnMessage : Message
{
    /// <summary>The dimension the player respawns into. In Beta 1.7.3 this is
    /// always 0 (the overworld); the Nether existed but respawning into it was
    /// not implemented.</summary>
    [WireField]
    public sbyte DimensionId { get; set; }
}
