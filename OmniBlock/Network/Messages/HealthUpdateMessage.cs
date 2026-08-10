namespace OmniBlock.Network.Messages;

public class HealthUpdateMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "health_update");

    /// <summary>Health in half-hearts, as a short on the wire — matches Beta 1.7.3's range.</summary>
    public short HealthMp { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream) => HealthMp = stream.ReadShort();

    public override void Write(Stream stream) => stream.WriteShort(HealthMp);

    public override int Size() => 2;
}
