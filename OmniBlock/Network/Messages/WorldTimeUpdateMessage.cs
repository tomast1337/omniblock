namespace OmniBlock.Network.Messages;

public class WorldTimeUpdateMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "world_time_update");
    public long Time { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream) => Time = stream.ReadLong();

    public override void Write(Stream stream) => stream.WriteLong(Time);

    public override int Size() => 8;
}
