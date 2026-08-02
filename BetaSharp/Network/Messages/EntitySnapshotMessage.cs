using BetaSharp.Network.Snapshots;

namespace BetaSharp.Network.Messages;

/// <summary>
///     Every entity update one tracking pass produced for one player, as differences from an earlier
///     snapshot that player confirmed receiving.
///     <para>
///         Replaces the four position packets (<c>EntityPosition</c>, <c>EntityMoveRelative</c>,
///         <c>EntityRotate</c>, <c>EntityRotateAndMoveRelative</c>) for peers that speak the
///         protocol.
///     </para>
///     <para>
///         <b>Batched, and that is most of the win.</b> The packets it replaces are per-entity, and
///         each pays a four-byte entity ID and its own framing to say that something moved by three
///         bytes. Sorting by ID and encoding the gaps turns the ID into roughly one byte, the field
///         mask removes the coordinates that did not change rather than sending three bytes whenever
///         any of them did, and the envelope is paid once for the pass instead of once per entity.
///     </para>
///     <para>
///         <b>Self-contained relative to a named baseline, which is the point.</b> The packets this
///         replaces encode positions as deltas from whatever was last sent, so losing one corrupts
///         every position after it and the channel has to be reliable and ordered — one lost entity
///         update stalls all of them until it is retransmitted. A snapshot names the state it was
///         measured against, so a peer that missed one simply keeps acknowledging what it has and the
///         next snapshot is measured against that instead. That is the property that makes
///         sequenced delivery viable for entity data at all.
///     </para>
/// </summary>
public sealed class EntitySnapshotMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.BetaSharp, "entity_snapshot");

    public override ResourceLocation Key => Id;

    /// <summary>
    ///     Entity motion is the thing interpolation is measuring, so a snapshot delayed behind bulk
    ///     traffic is measured as network jitter and paid for in interpolation delay.
    /// </summary>
    public override SendPriority Priority => SendPriority.High;

    /// <summary>
    ///     Records a peer will accept in one snapshot. A view distance of 32 with mobs tracked to 160
    ///     blocks does not approach this; it is here so a malformed count cannot make this peer
    ///     allocate on a stranger's word.
    /// </summary>
    public const int MaxRecords = 8192;

    /// <summary>Per-recipient, starting at 1. Zero is reserved for "no baseline".</summary>
    public uint Sequence { get; set; }

    /// <summary>
    ///     The snapshot these records are differences from, or zero when every record is absolute.
    ///     A receiver that cannot reconstruct this sequence must drop the whole message — a delta
    ///     applied to the wrong baseline is silently wrong rather than detectably broken.
    /// </summary>
    public uint Baseline { get; set; }

    public List<EntityDelta> Deltas { get; set; } = [];

    /// <summary>Which of an entity's fields a record carries, and whether they are absolute.</summary>
    [Flags]
    public enum Field : byte
    {
        None = 0,
        X = 0x01,
        Y = 0x02,
        Z = 0x04,
        Yaw = 0x08,
        Pitch = 0x10,

        /// <summary>
        ///     The coordinates in this record are absolute rather than differences from the baseline
        ///     — an entity the baseline has never held, because it just came into view.
        ///     <para>
        ///         Per record rather than per message. A pass that brings one new entity into range
        ///         alongside two hundred that merely moved would otherwise have to send all of them
        ///         in full.
        ///     </para>
        /// </summary>
        Absolute = 0x20,

        Position = X | Y | Z,
        Rotation = Yaw | Pitch,
        All = Position | Rotation,
    }

    /// <summary>
    ///     One entity's changes.
    ///     <para>
    ///         <see cref="X" />, <see cref="Y" /> and <see cref="Z" /> are differences from the
    ///         baseline unless <see cref="Field.Absolute" /> is set, in which case they are the
    ///         values themselves. Rotation is always absolute: it is a byte either way, so a
    ///         difference would cost the same at best and two bytes when an entity spins.
    ///     </para>
    /// </summary>
    public readonly record struct EntityDelta(int EntityId, Field Mask, int X, int Y, int Z, byte Yaw, byte Pitch);

    public override void Read(Stream stream)
    {
        Sequence = (uint)stream.ReadVarInt();
        Baseline = (uint)stream.ReadVarInt();

        int count = stream.ReadVarInt();
        if (count < 0 || count > MaxRecords)
        {
            throw new InvalidDataException($"Entity snapshot declares {count} records.");
        }

        Deltas = new List<EntityDelta>(count);

        int previousId = 0;
        for (int i = 0; i < count; i++)
        {
            int entityId = previousId + stream.ReadZigZag();
            previousId = entityId;

            Field mask = (Field)stream.ReadByte();

            int x = (mask & Field.X) != 0 ? stream.ReadZigZag() : 0;
            int y = (mask & Field.Y) != 0 ? stream.ReadZigZag() : 0;
            int z = (mask & Field.Z) != 0 ? stream.ReadZigZag() : 0;
            byte yaw = (mask & Field.Yaw) != 0 ? (byte)stream.ReadByte() : (byte)0;
            byte pitch = (mask & Field.Pitch) != 0 ? (byte)stream.ReadByte() : (byte)0;

            Deltas.Add(new EntityDelta(entityId, mask, x, y, z, yaw, pitch));
        }
    }

    public override void Write(Stream stream)
    {
        stream.WriteVarInt((int)Sequence);
        stream.WriteVarInt((int)Baseline);
        stream.WriteVarInt(Deltas.Count);

        int previousId = 0;
        foreach (EntityDelta delta in Deltas)
        {
            // Gap from the previous record's ID rather than the ID itself. Entity IDs come from one
            // increasing counter, so a pass over sorted IDs is a run of small gaps — one byte each
            // against four, which at a few hundred entities is most of the message.
            stream.WriteZigZag(delta.EntityId - previousId);
            previousId = delta.EntityId;

            stream.WriteByte((byte)delta.Mask);

            if ((delta.Mask & Field.X) != 0)
            {
                stream.WriteZigZag(delta.X);
            }

            if ((delta.Mask & Field.Y) != 0)
            {
                stream.WriteZigZag(delta.Y);
            }

            if ((delta.Mask & Field.Z) != 0)
            {
                stream.WriteZigZag(delta.Z);
            }

            if ((delta.Mask & Field.Yaw) != 0)
            {
                stream.WriteByte(delta.Yaw);
            }

            if ((delta.Mask & Field.Pitch) != 0)
            {
                stream.WriteByte(delta.Pitch);
            }
        }
    }

    public override int Size()
    {
        int size = StreamExtensions.VarIntSize((int)Sequence)
            + StreamExtensions.VarIntSize((int)Baseline)
            + StreamExtensions.VarIntSize(Deltas.Count);

        int previousId = 0;
        foreach (EntityDelta delta in Deltas)
        {
            size += StreamExtensions.ZigZagSize(delta.EntityId - previousId) + 1;
            previousId = delta.EntityId;

            if ((delta.Mask & Field.X) != 0)
            {
                size += StreamExtensions.ZigZagSize(delta.X);
            }

            if ((delta.Mask & Field.Y) != 0)
            {
                size += StreamExtensions.ZigZagSize(delta.Y);
            }

            if ((delta.Mask & Field.Z) != 0)
            {
                size += StreamExtensions.ZigZagSize(delta.Z);
            }

            if ((delta.Mask & Field.Yaw) != 0)
            {
                size++;
            }

            if ((delta.Mask & Field.Pitch) != 0)
            {
                size++;
            }
        }

        return size;
    }

    /// <summary>
    ///     Encodes one entity against a baseline. Static so the encoder and its tests state the rule
    ///     once — which fields are omitted is the whole compression, and getting it wrong produces a
    ///     snapshot that decodes cleanly to the wrong place.
    /// </summary>
    /// <returns>
    ///     Null when the baseline already holds exactly this state, which is the common case for
    ///     everything standing still.
    /// </returns>
    public static EntityDelta? Encode(int entityId, in EntitySnapshotState state, SnapshotBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        if (!baseline.TryGet(entityId, out EntitySnapshotState previous))
        {
            return new EntityDelta(
                entityId, Field.All | Field.Absolute, state.X, state.Y, state.Z, state.Yaw, state.Pitch);
        }

        Field mask = Field.None;

        if (state.X != previous.X)
        {
            mask |= Field.X;
        }

        if (state.Y != previous.Y)
        {
            mask |= Field.Y;
        }

        if (state.Z != previous.Z)
        {
            mask |= Field.Z;
        }

        if (state.Yaw != previous.Yaw)
        {
            mask |= Field.Yaw;
        }

        if (state.Pitch != previous.Pitch)
        {
            mask |= Field.Pitch;
        }

        if (mask == Field.None)
        {
            return null;
        }

        return new EntityDelta(
            entityId,
            mask,
            state.X - previous.X,
            state.Y - previous.Y,
            state.Z - previous.Z,
            state.Yaw,
            state.Pitch);
    }

    /// <summary>
    ///     Reconstructs one entity's absolute state from a record and a baseline. The inverse of
    ///     <see cref="Encode" />; an absent field means the baseline's value survived unchanged.
    /// </summary>
    public static EntitySnapshotState Decode(in EntityDelta delta, SnapshotBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);

        if ((delta.Mask & Field.Absolute) != 0)
        {
            return new EntitySnapshotState(delta.X, delta.Y, delta.Z, delta.Yaw, delta.Pitch);
        }

        baseline.TryGet(delta.EntityId, out EntitySnapshotState previous);

        return new EntitySnapshotState(
            (delta.Mask & Field.X) != 0 ? previous.X + delta.X : previous.X,
            (delta.Mask & Field.Y) != 0 ? previous.Y + delta.Y : previous.Y,
            (delta.Mask & Field.Z) != 0 ? previous.Z + delta.Z : previous.Z,
            (delta.Mask & Field.Yaw) != 0 ? delta.Yaw : previous.Yaw,
            (delta.Mask & Field.Pitch) != 0 ? delta.Pitch : previous.Pitch);
    }
}
