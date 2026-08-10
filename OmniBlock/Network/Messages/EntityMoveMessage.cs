using OmniBlock;

namespace OmniBlock.Network.Messages;

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
public sealed class EntityMoveMessage : Message
{
    /// <summary>
    ///     Entity replication, which is what the priority split was built for: this must not queue
    ///     behind a chunk.
    /// </summary>
    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    /// <summary>Which of the two groups below carry meaning. Zero is a bare "still here".</summary>
    public Field Mask { get; set; }

    /// <summary>Sixteenths of a block, the units the tracker already worked in.</summary>
    public sbyte DeltaX { get; set; }

    public sbyte DeltaY { get; set; }

    public sbyte DeltaZ { get; set; }

    /// <summary>A full turn in 256 steps, absolute rather than relative.</summary>
    public sbyte Yaw { get; set; }

    public sbyte Pitch { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "entity_move");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        Mask = (Field)((byte)stream.ReadByte());
        DeltaX = (sbyte)stream.ReadByte();
        DeltaY = (sbyte)stream.ReadByte();
        DeltaZ = (sbyte)stream.ReadByte();
        Yaw = (sbyte)stream.ReadByte();
        Pitch = (sbyte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteByte(((byte)Mask));
        stream.WriteByte((byte)DeltaX);
        stream.WriteByte((byte)DeltaY);
        stream.WriteByte((byte)DeltaZ);
        stream.WriteByte((byte)Yaw);
        stream.WriteByte((byte)Pitch);
    }

    public override int Size()
    {
        return
            4
            + 1
            + 1
            + 1
            + 1
            + 1
            + 1;
    }

    [Flags]
    public enum Field : byte
    {
        None = 0,
        Moved = 1,
        Rotated = 2,
    }
}
