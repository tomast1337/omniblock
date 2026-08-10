using OmniBlock;
using OmniBlock.Util;

namespace OmniBlock.Network.Messages;

/// <summary>
///     Spawns a painting, which is placed by anchor and facing rather than by position.
///     Replaces <c>PaintingEntitySpawnS2CPacket</c>.
/// </summary>
public sealed class PaintingSpawnMessage : Message
{
    /// <summary>
    ///     Comfortably above <c>Painting.MaxArtTitleLength</c>, which cannot be named here because it
    ///     is computed rather than a constant. <c>PaintingSpawnBoundsTest</c> holds the two together.
    /// </summary>
    public const int MaxTitleBytes = 32;

    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    /// <summary>
    ///     Names the art, and is bounded here rather than trusted. The client looks the title up in
    ///     its own art registry, so an over-long one buys nothing but the allocation — which is
    ///     reason enough to refuse it before making it.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Block coordinates of the anchor, not fixed point — a painting hangs on a block face.</summary>
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public int Direction { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "painting_spawn");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        Title = stream.ReadString(32);
        X = stream.ReadInt();
        Y = stream.ReadInt();
        Z = stream.ReadInt();
        Direction = stream.ReadInt();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteString(Title);
        stream.WriteInt(X);
        stream.WriteInt(Y);
        stream.WriteInt(Z);
        stream.WriteInt(Direction);
    }

    public override int Size()
    {
        return
            4
            + (2 + ModifiedUtf8.GetByteCount(Title))
            + 4
            + 4
            + 4
            + 4;
    }
}
