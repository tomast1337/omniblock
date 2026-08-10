namespace OmniBlock.Network.Messages;

/// <summary>
///     Which hotbar slot the player has selected. Replaces <c>UpdateSelectedSlotC2SPacket</c>.
/// </summary>
public sealed class SelectedSlotMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "selected_slot");
    public short Slot { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream) => Slot = stream.ReadShort();

    public override void Write(Stream stream) => stream.WriteShort(Slot);

    public override int Size() => 2;
}
