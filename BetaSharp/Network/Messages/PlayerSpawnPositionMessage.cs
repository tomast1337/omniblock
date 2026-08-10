namespace OmniBlock.Network.Messages;

[WireMessage("beta:player_spawn_position")]
public partial class PlayerSpawnPositionMessage : Message
{
    [WireField]
    public int X { get; set; }

    [WireField]
    public int Y { get; set; }

    [WireField]
    public int Z { get; set; }
}
