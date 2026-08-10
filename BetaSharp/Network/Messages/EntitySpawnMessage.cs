namespace OmniBlock.Network.Messages;

/// <summary>
///     Spawns a non-living entity — a minecart, an arrow, a fireball, a falling block.
///     Replaces <c>EntitySpawnS2CPacket</c>.
///     <para>
///         <b>The velocity fields are unconditional now.</b> The packet wrote them only when
///         <see cref="EntityData" /> was positive, which made the payload's length depend on one of
///         its own fields — and produced the worst size bug in the tree: <c>17 + 4 + EntityData > 0
///         ? 6 : 0</c> parses as <c>(21 + EntityData) &gt; 0 ? 6 : 0</c>, so <c>Size()</c> answered
///         6 for a packet of 21 or 27 bytes. Six bytes on a spawn is not worth a conditional; being
///         unable to write one wrongly is.
///     </para>
/// </summary>
[WireMessage("omniblock:entity_spawn")]
public sealed partial class EntitySpawnMessage : Message
{
    /// <summary>Spawns travel with the updates that follow them, or a move can overtake its own spawn.</summary>
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    /// <summary>The object-spawn id, which is not the entity type id — see <c>EntityDefinition</c>.</summary>
    [WireField]
    public sbyte EntityType { get; set; }

    /// <summary>Fixed point in sixteenths of a block.</summary>
    [WireField]
    public int X { get; set; }

    [WireField]
    public int Y { get; set; }

    [WireField]
    public int Z { get; set; }

    /// <summary>
    ///     Type-specific: an arrow's shooter, a falling block's block id. Zero where the type has
    ///     nothing to say, which is also what tells the client the velocity below is meaningless.
    /// </summary>
    [WireField]
    public int EntityData { get; set; }

    /// <summary>Blocks per tick times 8000. Only meaningful when <see cref="EntityData" /> is positive.</summary>
    [WireField]
    public short VelocityX { get; set; }

    [WireField]
    public short VelocityY { get; set; }

    [WireField]
    public short VelocityZ { get; set; }
}
