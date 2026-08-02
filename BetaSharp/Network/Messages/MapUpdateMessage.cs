namespace BetaSharp.Network.Messages;

[WireMessage("beta:map_update")]
public partial class MapUpdateMessage : Message
{
    [WireField]
    public short ItemRawId { get; set; }

    [WireField]
    public short MapId { get; set; }

    [WireField]
    public byte[] Data { get; set; } = [];
}
