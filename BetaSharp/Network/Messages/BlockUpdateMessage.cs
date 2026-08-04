namespace BetaSharp.Network.Messages;

/// <summary>
///     One position's block and metadata.
/// </summary>
/// <remarks>
///     Carries no light. It used to, so that a light-only change had something to travel in, and
///     that was the wrong shape twice over: a light byte read for one cell cannot express the pass
///     that lights a whole chunk, and every construction of this message that forgot to set the
///     field wrote a real, destructive zero into the receiver. Light travels as whole sections on
///     <see cref="LightSectionsMessage" />, where there is no per-message field to leave unset.
/// </remarks>
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
