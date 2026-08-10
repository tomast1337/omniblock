using OmniBlock;

namespace OmniBlock.Network.Messages;

public class FinishConfigurationMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "finish_configuration");

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
