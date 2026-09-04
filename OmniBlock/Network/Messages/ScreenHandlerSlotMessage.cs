using OmniBlock.Items;
using OmniBlock.Registries;

namespace OmniBlock.Network.Messages;

/// <summary>
///     One slot of an open screen. Replaces <c>ScreenHandlerSlotUpdateS2CPacket</c>, which declared
///     a constant 8 bytes for a payload of 5 or 8.
/// </summary>
public sealed class ScreenHandlerSlotMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "screen_slot");
    private readonly IItemRuntimeView? _items;

    public ScreenHandlerSlotMessage()
    {
    }

    internal ScreenHandlerSlotMessage(IItemRuntimeView items) => _items = items;

    /// <summary>-1 with slot -1 addresses the cursor stack rather than a screen.</summary>
    public sbyte SyncId { get; set; }

    public short Slot { get; set; }

    public ItemStack? Stack { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        SyncId = (sbyte)stream.ReadByte();
        Slot = stream.ReadShort();
        Stack = stream.ReadItemStack(_items ?? throw new InvalidOperationException("No item catalog was supplied for decoding."));
    }

    public override void Write(Stream stream)
    {
        stream.WriteByte((byte)SyncId);
        stream.WriteShort(Slot);
        stream.WriteItemStack(Stack);
    }

    public override int Size() =>
        1
        + 2
        + StreamExtensions.ItemStackSize(Stack);
}
