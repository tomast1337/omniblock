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

    /// <summary>
    ///     Block light in the low nibble, sky light in the high one, matching how a chunk stores
    ///     them.
    /// </summary>
    /// <remarks>
    ///     Carried because the server announces a position whenever its <em>light</em> changes, not
    ///     only when its block does. Without this the announcement for a light-only change is
    ///     identical to what the receiver already holds, so it is indistinguishable from a repeat
    ///     and the new value never arrives.
    /// </remarks>
    [WireField]
    public byte Light { get; set; }
}
