using OmniBlock;

namespace OmniBlock.Network.Messages;

/// <summary>
///     The changed entries of an entity's <c>DataSynchronizer</c>. Replaces
///     <c>EntityTrackerUpdateS2CPacket</c>.
///     <para>
///         <b>Length-prefixed rather than terminated.</b> The packet wrote the properties and then a
///         127 byte, and the reader consumed until it saw one. That works only while no property can
///         encode a 127 in a position the scanner will read as a terminator, which is a property of
///         the payload rather than of the framing. A length says the same thing and cannot be
///         confused by its own contents.
///     </para>
/// </summary>
public sealed class EntityDataMessage : Message
{
    /// <summary>
    ///     Well above what 32 synchronised properties can produce, and finite, which is the part
    ///     that matters: the length decides an allocation.
    /// </summary>
    public const int MaxDataBytes = 4096;

    public override SendPriority Priority => SendPriority.High;

    public int EntityId { get; set; }

    public byte[] Data { get; set; } = [];

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "entity_data");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        Data = stream.ReadByteArray(4096);
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteByteArray(Data);
    }

    public override int Size()
    {
        return
            4
            + StreamExtensions.ByteArraySize(Data);
    }
}
