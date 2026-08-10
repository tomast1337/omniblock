namespace OmniBlock.Network.Messages;

[WireMessage("beta:chunk_status_update")]
public partial class ChunkStatusUpdateMessage : Message
{
    [WireField]
    public int X { get; set; }

    [WireField]
    public int Z { get; set; }

    [WireField]
    public bool Loaded { get; set; }
}
