using OmniBlock.Util;

namespace OmniBlock.Network.Messages;

/// <summary>
///     Opens a screen on the client. Replaces <c>OpenScreenS2CPacket</c>, which declared
///     <c>3 + Name.Length</c> for a payload of five plus the name's encoded bytes — wrong by two,
///     and wrong again for any name that is not pure ASCII.
/// </summary>
public sealed class OpenScreenMessage : Message
{
    public enum KnownInventories : byte
    {
        Crafting = 1,
        Chest = 2,
        Furnace = 3,

        /// <summary>Also known as Dispenser.</summary>
        Trap = 4,
        Minecart = 5
    }

    /// <summary>
    ///     A screen title, bounded before the allocation. Titles are short labels; a peer naming a
    ///     long one gains nothing but the memory, which is reason enough to refuse it.
    /// </summary>
    public const int MaxNameBytes = 64;

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "open_screen");

    public sbyte SyncId { get; set; }

    /// <summary>Which screen to open — see <see cref="KnownInventories" />.</summary>
    public sbyte ScreenHandlerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public sbyte SlotsCount { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        SyncId = (sbyte)stream.ReadByte();
        ScreenHandlerId = (sbyte)stream.ReadByte();
        Name = stream.ReadString(64);
        SlotsCount = (sbyte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteByte((byte)SyncId);
        stream.WriteByte((byte)ScreenHandlerId);
        stream.WriteString(Name);
        stream.WriteByte((byte)SlotsCount);
    }

    public override int Size() =>
        1
        + 1
        + 2 + ModifiedUtf8.GetByteCount(Name)
        + 1;
}
