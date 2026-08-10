using OmniBlock.Util;

namespace OmniBlock.Network.Messages;

public class ChatMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "chat_message");
    public string Text { get; set; } = "";

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream) => Text = stream.ReadString(119);

    public override void Write(Stream stream) => stream.WriteString(Text);

    public override int Size() => 2 + ModifiedUtf8.GetByteCount(Text);
}
