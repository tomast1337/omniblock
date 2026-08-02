namespace BetaSharp.Network.Messages;

[WireMessage("beta:block_update")]
public partial class BlockUpdateMessage : Message
{
    [WireField]
    public int X { get; set; }

    /// <summary>Y coordinate on the wire is a single byte — Beta 1.7.3's world height is 128 blocks.</summary>
    [WireField]
    public sbyte Y { get; set; }

    [WireField]
    public int Z { get; set; }

    [WireField]
    public byte BlockRawId { get; set; }

    [WireField]
    public byte BlockMetadata { get; set; }
}
