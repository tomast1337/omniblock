namespace OmniBlock.Network.Messages;

[WireMessage("beta:world_time_update")]
public partial class WorldTimeUpdateMessage : Message
{
    [WireField]
    public long Time { get; set; }
}
