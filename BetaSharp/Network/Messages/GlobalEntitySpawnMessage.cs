namespace BetaSharp.Network.Messages;

/// <summary>
///     Spawns an entity every player in the dimension sees, wherever they are — lightning.
///     Replaces <c>GlobalEntitySpawnS2CPacket</c>.
/// </summary>
[WireMessage("betasharp:global_entity_spawn")]
public sealed partial class GlobalEntitySpawnMessage : Message
{
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    [WireField]
    public byte Type { get; set; }

    /// <summary>Fixed point in sixteenths of a block.</summary>
    [WireField]
    public int X { get; set; }

    [WireField]
    public int Y { get; set; }

    [WireField]
    public int Z { get; set; }
}
