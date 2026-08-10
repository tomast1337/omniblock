namespace OmniBlock.Network.Messages;

public class MapUpdateMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "map_update");
    public short ItemRawId { get; set; }

    public short MapId { get; set; }

    public byte[] Data { get; set; } = [];

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        ItemRawId = stream.ReadShort();
        MapId = stream.ReadShort();
        Data = stream.ReadByteArray();
    }

    public override void Write(Stream stream)
    {
        stream.WriteShort(ItemRawId);
        stream.WriteShort(MapId);
        stream.WriteByteArray(Data);
    }

    public override int Size() =>
        2
        + 2
        + StreamExtensions.ByteArraySize(Data);
}
