using OmniBlock.Items;

namespace OmniBlock.Network.Messages;

/// <summary>
///     Every slot of an open screen at once. Replaces <c>InventoryS2CPacket</c>.
///     <para>
///         The packet charged five bytes for every slot — <c>3 + Contents.Length * 5</c> — while an
///         empty slot writes two. A player's inventory is mostly empty slots, so the declared size
///         was wrong on essentially every send, and wrong by more the emptier the inventory was.
///     </para>
/// </summary>
[WireMessage("omniblock:inventory")]
public sealed partial class InventoryMessage : Message
{
    /// <summary>
    ///     Far above the largest screen the game opens, and finite, which is the part that matters:
    ///     the count decides an array allocation.
    /// </summary>
    public const int MaxSlots = 1024;

    /// <summary>-1 addresses the player's own inventory rather than an open screen.</summary>
    [WireField]
    public sbyte SyncId { get; set; }

    [WireField(MaxLength = MaxSlots)]
    public ItemStack?[] Contents { get; set; } = [];
}
