namespace OmniBlock.Network.Messages;

/// <summary>
///     Spawns a painting, which is placed by anchor and facing rather than by position.
///     Replaces <c>PaintingEntitySpawnS2CPacket</c>.
/// </summary>
[WireMessage("omniblock:painting_spawn")]
public sealed partial class PaintingSpawnMessage : Message
{
    /// <summary>
    ///     Comfortably above <c>Painting.MaxArtTitleLength</c>, which cannot be named here because it
    ///     is computed rather than a constant. <c>PaintingSpawnBoundsTest</c> holds the two together.
    /// </summary>
    public const int MaxTitleBytes = 32;

    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    /// <summary>
    ///     Names the art, and is bounded here rather than trusted. The client looks the title up in
    ///     its own art registry, so an over-long one buys nothing but the allocation — which is
    ///     reason enough to refuse it before making it.
    /// </summary>
    [WireField(MaxLength = MaxTitleBytes)]
    public string Title { get; set; } = string.Empty;

    /// <summary>Block coordinates of the anchor, not fixed point — a painting hangs on a block face.</summary>
    [WireField]
    public int X { get; set; }

    [WireField]
    public int Y { get; set; }

    [WireField]
    public int Z { get; set; }

    [WireField]
    public int Direction { get; set; }
}
