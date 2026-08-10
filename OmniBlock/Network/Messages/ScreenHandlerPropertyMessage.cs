using OmniBlock;

namespace OmniBlock.Network.Messages;

/// <summary>
///     A tracked value on an open screen — a furnace's burn time, a brewing stand's progress.
///     Replaces <c>ScreenHandlerPropertyUpdateS2CPacket</c>.
/// </summary>
public sealed class ScreenHandlerPropertyMessage : Message
{
    public sbyte SyncId { get; set; }

    public short PropertyId { get; set; }

    public short Value { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "screen_property");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        SyncId = (sbyte)stream.ReadByte();
        PropertyId = stream.ReadShort();
        Value = stream.ReadShort();
    }

    public override void Write(Stream stream)
    {
        stream.WriteByte((byte)SyncId);
        stream.WriteShort(PropertyId);
        stream.WriteShort(Value);
    }

    public override int Size()
    {
        return
            1
            + 2
            + 2;
    }
}
