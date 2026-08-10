using OmniBlock.Util;

namespace OmniBlock.Network.Messages;

public class DisconnectMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "disconnect");
    public string Reason { get; set; } = "";

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream) => Reason = stream.ReadString(100);

    public override void Write(Stream stream) => stream.WriteString(Reason);

    public override int Size() => 2 + ModifiedUtf8.GetByteCount(Reason);
}
