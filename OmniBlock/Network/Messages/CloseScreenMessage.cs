namespace OmniBlock.Network.Messages;

/// <summary>
///     Closes an open screen. Replaces <c>CloseScreenS2CPacket</c>, and travels both ways: the
///     client says it has closed one, the server says one is no longer valid.
/// </summary>
public sealed class CloseScreenMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "close_screen");
    public sbyte SyncId { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream) => SyncId = (sbyte)stream.ReadByte();

    public override void Write(Stream stream) => stream.WriteByte((byte)SyncId);

    public override int Size() => 1;
}
