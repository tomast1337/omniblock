using BetaSharp.Items;

namespace BetaSharp.Network.Messages;

/// <summary>
///     A right-click against a block, or against nothing. Replaces
///     <c>PlayerInteractBlockC2SPacket</c>.
///     <para>
///         <b>Side 255 means "no block".</b> The client sends x, y and z as -1 in that case and the
///         server keys off the side alone. Preserved rather than tidied into a separate message,
///         because the two paths share a reach check and a held-item check and splitting them would
///         duplicate both.
///     </para>
/// </summary>
[WireMessage("betasharp:interact_block")]
public sealed partial class InteractBlockMessage : Message
{
    [WireField]
    public int X { get; set; }

    [WireField]
    public byte Y { get; set; }

    [WireField]
    public int Z { get; set; }

    /// <summary>Block face, or 255 for an interaction with no block behind it.</summary>
    [WireField]
    public byte Side { get; set; }

    /// <summary>
    ///     What the client believes it is holding. Advisory — the server uses its own record of the
    ///     player's inventory — and carried because the packet it replaces carried it.
    /// </summary>
    [WireField]
    public ItemStack? Stack { get; set; }
}
