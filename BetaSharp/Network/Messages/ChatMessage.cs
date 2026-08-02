namespace BetaSharp.Network.Messages;

[WireMessage("beta:chat_message")]
public partial class ChatMessage : Message
{
    [WireField(MaxLength = 119)]
    public string Text { get; set; } = "";
}
