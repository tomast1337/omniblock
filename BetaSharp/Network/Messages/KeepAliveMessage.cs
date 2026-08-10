namespace OmniBlock.Network.Messages;

[WireMessage("beta:keep_alive")]
public partial class KeepAliveMessage : Message
{
    public override SendPriority Priority => SendPriority.High;
}
