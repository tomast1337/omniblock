using OmniBlock.Items;

namespace OmniBlock.Network.Messages;

/// <summary>
///     One slot of an open screen. Replaces <c>ScreenHandlerSlotUpdateS2CPacket</c>, which declared
///     a constant 8 bytes for a payload of 5 or 8.
/// </summary>
public sealed class ScreenHandlerSlotMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "screen_slot");

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
        Stack = stream.ReadItemStack();
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
