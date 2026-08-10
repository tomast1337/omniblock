namespace OmniBlock.Network.Messages;

[WireMessage("beta:disconnect")]
public partial class DisconnectMessage : Message
{
    [WireField(MaxLength = 100)]
    public string Reason { get; set; } = "";
}
