using OmniBlock;

namespace OmniBlock.Network.Messages;

public class KeepAliveMessage : Message
{
    public override SendPriority Priority => SendPriority.High;

    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "keep_alive");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
    }

    public override void Write(Stream stream)
    {
    }

    public override int Size()
    {
        return 0;
    }
}
