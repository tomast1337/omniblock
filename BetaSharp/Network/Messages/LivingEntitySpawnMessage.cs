namespace BetaSharp.Network.Messages;

/// <summary>
///     Spawns a mob, with its whole synchronised data set. Replaces
///     <c>LivingEntitySpawnS2CPacket</c>.
///     <para>
///         The packet declared 20 bytes and wrote 20 plus the data, which is every spawn it ever
///         sent. Length-prefixing the data replaces the 127 terminator for the same reason
///         <see cref="EntityDataMessage" /> did: a terminator makes the framing depend on the
///         payload not happening to contain it.
///     </para>
/// </summary>
[WireMessage("betasharp:living_entity_spawn")]
public sealed partial class LivingEntitySpawnMessage : Message
{
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    [WireField]
    public sbyte Type { get; set; }

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

    /// <summary>The full <c>DataSynchronizer</c> state, not a delta — this is the entity's first sight.</summary>
    [WireField(MaxLength = EntityDataMessage.MaxDataBytes)]
    public byte[] Data { get; set; } = [];
}
