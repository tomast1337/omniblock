namespace OmniBlock.Network.Messages;

public class ChunkStatusUpdateMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "chunk_status_update");
    public int X { get; set; }

    public int Z { get; set; }

    public bool Loaded { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        X = stream.ReadInt();
        Z = stream.ReadInt();
        Loaded = stream.ReadBoolean();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(X);
        stream.WriteInt(Z);
        stream.WriteBoolean(Loaded);
    }

    public override int Size() =>
        4
        + 4
        + 1;
}
