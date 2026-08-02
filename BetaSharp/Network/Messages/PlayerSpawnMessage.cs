namespace BetaSharp.Network.Messages;

/// <summary>
///     Spawns another player. Replaces <c>PlayerSpawnS2CPacket</c>.
///     <para>
///         The packet declared 28 bytes for a payload that is 22 plus twice the name's length — 54
///         at the sixteen-character limit, so it was wrong on every spawn it ever sent.
///     </para>
/// </summary>
[WireMessage("betasharp:player_spawn")]
public sealed partial class PlayerSpawnMessage : Message
{
    /// <summary>The account name's limit, and the bound the reader applies before allocating.</summary>
    public const int MaxNameBytes = 16;

    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    [WireField(MaxLength = MaxNameBytes)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Fixed point in sixteenths of a block.</summary>
    [WireField]
    public int X { get; set; }

    [WireField]
    public int Y { get; set; }

    [WireField]
    public int Z { get; set; }

    [WireField]
    public sbyte Yaw { get; set; }

    [WireField]
    public sbyte Pitch { get; set; }

    /// <summary>The item in hand, for rendering only. 0 is an empty hand.</summary>
    [WireField]
    public short CurrentItem { get; set; }
}
