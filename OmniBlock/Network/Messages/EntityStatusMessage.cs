namespace OmniBlock.Network.Messages;

/// <summary>
///     A one-off visual or audible event on an entity — hurt, death, a wolf shaking off water.
///     Replaces <c>EntityStatusS2CPacket</c>.
/// </summary>
public sealed class EntityStatusMessage : Message
{
    public enum EntityState : byte
    {
        Hurt = 2,
        Death = 3,
        WolfSmokeFx = 6,
        WolfHeartsFx = 7,
        WolfShaking = 8
    }

    /// <summary>
    ///     The named statuses. Sparse, and deliberately not the field's type: the wire carries
    ///     values this enum has no name for, and decoding into it would be a lie the compiler
    ///     believes.
    /// </summary>
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "entity_status");

    public int EntityId { get; set; }

    public sbyte Status { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        Status = (sbyte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteByte((byte)Status);
    }

    public override int Size() =>
        4
        + 1;
}
