namespace BetaSharp.Network.Messages;

/// <summary>
///     A tracked entity's step, as a relative delta.
///     <para>
///         Replaces four packets — <c>EntityS2CPacket</c>, <c>EntityMoveRelativeS2CPacket</c>,
///         <c>EntityRotateS2CPacket</c> and <c>EntityRotateAndMoveRelativeS2CPacket</c>. They were
///         four because the byte-ID framing charges for a field whether or not it means anything,
///         so the only way to omit the rotation was to have a message shape without it. A mask byte
///         says the same thing in one type, and the sender's four-way branch becomes two flags.
///     </para>
///     <para>
///         <b>Fixed width rather than mask-conditional fields.</b> Omitting the absent deltas would
///         save four bytes; it would also make the payload's length depend on its own contents,
///         which is the shape <see cref="EntitySnapshotMessage" /> exists to do properly across a
///         whole tick's worth of entities. This one is the per-entity path, and it now carries only
///         singleplayer, where the message is handed over as an object and its width costs nothing
///         at all.
///     </para>
/// </summary>
[WireMessage("betasharp:entity_move")]
public sealed partial class EntityMoveMessage : Message
{
    /// <summary>
    ///     Entity replication, which is what the priority split was built for: this must not queue
    ///     behind a chunk.
    /// </summary>
    public override SendPriority Priority => SendPriority.High;

    [WireField]
    public int EntityId { get; set; }

    /// <summary>Which of the two groups below carry meaning. Zero is a bare "still here".</summary>
    [WireField]
    public Field Mask { get; set; }

    /// <summary>Sixteenths of a block, the units the tracker already worked in.</summary>
    [WireField]
    public sbyte DeltaX { get; set; }

    [WireField]
    public sbyte DeltaY { get; set; }

    [WireField]
    public sbyte DeltaZ { get; set; }

    /// <summary>A full turn in 256 steps, absolute rather than relative.</summary>
    [WireField]
    public sbyte Yaw { get; set; }

    [WireField]
    public sbyte Pitch { get; set; }

    [Flags]
    public enum Field : byte
    {
        None = 0,
        Moved = 1,
        Rotated = 2,
    }
}
