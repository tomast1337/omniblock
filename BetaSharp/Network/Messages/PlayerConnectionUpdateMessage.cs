namespace OmniBlock.Network.Messages;

[WireMessage("beta:player_connection_update")]
public partial class PlayerConnectionUpdateMessage : Message
{
    public enum UpdateType : byte
    {
        Join = 0,
        Leave = 1
    }

    [WireField]
    public int EntityId { get; set; }

    [WireField]
    public UpdateType Type { get; set; }

    /// <summary>Player name, bounded to 16 characters — matches Beta 1.7.3's limit.</summary>
    [WireField(MaxLength = 16)]
    public string Name { get; set; } = "";
}
