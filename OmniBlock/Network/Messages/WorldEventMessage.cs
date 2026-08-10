namespace OmniBlock.Network.Messages;

[WireMessage("beta:world_event")]
public partial class WorldEventMessage : Message
{
    [WireField]
    public int EventId { get; set; }

    [WireField]
    public int X { get; set; }

    /// <summary>Y on the wire is a signed byte — world events target a block position.</summary>
    [WireField]
    public sbyte Y { get; set; }

    [WireField]
    public int Z { get; set; }

    [WireField]
    public int Data { get; set; }
}
