namespace BetaSharp.Network.Messages;

[WireMessage("beta:increase_stat")]
public partial class IncreaseStatMessage : Message
{
    [WireField]
    public int StatId { get; set; }

    /// <summary>The amount written is a signed byte; the stat itself is interpreted
    /// by the client from its registry entry.</summary>
    [WireField]
    public sbyte Amount { get; set; }
}
