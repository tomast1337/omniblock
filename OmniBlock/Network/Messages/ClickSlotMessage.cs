using OmniBlock.Items;
using OmniBlock.Registries;

namespace OmniBlock.Network.Messages;

/// <summary>
///     A click inside an open screen. Replaces <c>ClickSlotC2SPacket</c>.
///     <para>
///         The stack the client thinks the click produced is what the server checks its own result
///         against; a mismatch is what <c>ScreenHandlerAcknowledgementPacket</c> answers. Nothing
///         here is trusted on its own.
///     </para>
/// </summary>
public sealed class ClickSlotMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "click_slot");
    private readonly IItemRuntimeView? _items;

    public ClickSlotMessage()
    {
    }

    internal ClickSlotMessage(IItemRuntimeView items) => _items = items;
    public sbyte SyncId { get; set; }

    public short Slot { get; set; }

    public sbyte Button { get; set; }

    /// <summary>Revision, matched by the acknowledgement the server sends back.</summary>
    public short ActionType { get; set; }

    public bool HoldingShift { get; set; }

    public ItemStack? Stack { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        SyncId = (sbyte)stream.ReadByte();
        Slot = stream.ReadShort();
        Button = (sbyte)stream.ReadByte();
        ActionType = stream.ReadShort();
        HoldingShift = stream.ReadBoolean();
        Stack = stream.ReadItemStack(_items ?? throw new InvalidOperationException("No item catalog was supplied for decoding."));
    }

    public override void Write(Stream stream)
    {
        stream.WriteByte((byte)SyncId);
        stream.WriteShort(Slot);
        stream.WriteByte((byte)Button);
        stream.WriteShort(ActionType);
        stream.WriteBoolean(HoldingShift);
        stream.WriteItemStack(Stack);
    }

    public override int Size() =>
        1
        + 2
        + 1
        + 2
        + 1
        + StreamExtensions.ItemStackSize(Stack);
}
