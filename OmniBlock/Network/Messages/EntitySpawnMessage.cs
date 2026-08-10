namespace OmniBlock.Network.Messages;

/// <summary>
///     Spawns a non-living entity — a minecart, an arrow, a fireball, a falling block.
///     Replaces <c>EntitySpawnS2CPacket</c>.
///     <para>
///         <b>The velocity fields are unconditional now.</b> The packet wrote them only when
///         <see cref="EntityData" /> was positive, which made the payload's length depend on one of
///         its own fields — and produced the worst size bug in the tree:
///         <c>
///             17 + 4 + EntityData > 0
///             ? 6 : 0
///         </c>
///         parses as <c>(21 + EntityData) &gt; 0 ? 6 : 0</c>, so <c>Size()</c> answered
///         6 for a packet of 21 or 27 bytes. Six bytes on a spawn is not worth a conditional; being
///         unable to write one wrongly is.
///     </para>
/// </summary>
public sealed class EntitySpawnMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "entity_spawn");

    /// <summary>Spawns travel with the updates that follow them, or a move can overtake its own spawn.</summary>
    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    /// <summary>The object-spawn id, which is not the entity type id — see <c>EntityDefinition</c>.</summary>
    public sbyte EntityType { get; set; }

    /// <summary>Fixed point in sixteenths of a block.</summary>
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    /// <summary>
    ///     Type-specific: an arrow's shooter, a falling block's block id. Zero where the type has
    ///     nothing to say, which is also what tells the client the velocity below is meaningless.
    /// </summary>
    public int EntityData { get; set; }

    /// <summary>Blocks per tick times 8000. Only meaningful when <see cref="EntityData" /> is positive.</summary>
    public short VelocityX { get; set; }

    public short VelocityY { get; set; }

    public short VelocityZ { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        EntityType = (sbyte)stream.ReadByte();
        X = stream.ReadInt();
        Y = stream.ReadInt();
        Z = stream.ReadInt();
        EntityData = stream.ReadInt();
        VelocityX = stream.ReadShort();
        VelocityY = stream.ReadShort();
        VelocityZ = stream.ReadShort();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteByte((byte)EntityType);
        stream.WriteInt(X);
        stream.WriteInt(Y);
        stream.WriteInt(Z);
        stream.WriteInt(EntityData);
        stream.WriteShort(VelocityX);
        stream.WriteShort(VelocityY);
        stream.WriteShort(VelocityZ);
    }

    public override int Size() =>
        4
        + 1
        + 4
        + 4
        + 4
        + 4
        + 2
        + 2
        + 2;
}
