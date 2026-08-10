namespace OmniBlock.Network.Messages;

/// <summary>
///     Spawns a dropped item. Replaces <c>ItemEntitySpawnS2CPacket</c>.
/// </summary>
[WireMessage("omniblock:item_entity_spawn")]
public sealed partial class ItemEntitySpawnMessage : Message
{
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    [WireField]
    public short ItemRawId { get; set; }

    [WireField]
    public sbyte ItemCount { get; set; }

    [WireField]
    public short ItemDamage { get; set; }

    /// <summary>Fixed point in sixteenths of a block.</summary>
    [WireField]
    public int X { get; set; }

    [WireField]
    public int Y { get; set; }

    [WireField]
    public int Z { get; set; }

    /// <summary>Blocks per tick times 128 — a coarser scale than the other spawns, and the one the drop toss uses.</summary>
    [WireField]
    public sbyte VelocityX { get; set; }

    [WireField]
    public sbyte VelocityY { get; set; }

    [WireField]
    public sbyte VelocityZ { get; set; }
}
