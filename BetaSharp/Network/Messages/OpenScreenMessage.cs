namespace BetaSharp.Network.Messages;

/// <summary>
///     Opens a screen on the client. Replaces <c>OpenScreenS2CPacket</c>, which declared
///     <c>3 + Name.Length</c> for a payload of five plus the name's encoded bytes — wrong by two,
///     and wrong again for any name that is not pure ASCII.
/// </summary>
[WireMessage("betasharp:open_screen")]
public sealed partial class OpenScreenMessage : Message
{
    /// <summary>
    ///     A screen title, bounded before the allocation. Titles are short labels; a peer naming a
    ///     long one gains nothing but the memory, which is reason enough to refuse it.
    /// </summary>
    public const int MaxNameBytes = 64;

    [WireField]
    public sbyte SyncId { get; set; }

    /// <summary>Which screen to open — see <see cref="KnownInventories" />.</summary>
    [WireField]
    public sbyte ScreenHandlerId { get; set; }

    [WireField(MaxLength = MaxNameBytes)]
    public string Name { get; set; } = string.Empty;

    [WireField]
    public sbyte SlotsCount { get; set; }

    public enum KnownInventories : byte
    {
        Crafting = 1,
        Chest = 2,
        Furnace = 3,

        /// <summary>Also known as Dispenser.</summary>
        Trap = 4,
        Minecart = 5,
    }
}
