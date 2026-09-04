using OmniBlock.Items;
using OmniBlock.Registries;

namespace OmniBlock.Network.Messages;

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
public sealed class InteractBlockMessage : Message
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "interact_block");
    private readonly IItemRuntimeView? _items;

    public InteractBlockMessage()
    {
    }

    internal InteractBlockMessage(IItemRuntimeView items) => _items = items;
    public int X { get; set; }

    public byte Y { get; set; }

    public int Z { get; set; }

    /// <summary>Block face, or 255 for an interaction with no block behind it.</summary>
    public byte Side { get; set; }

    /// <summary>
    ///     What the client believes it is holding. Advisory — the server uses its own record of the
    ///     player's inventory — and carried because the packet it replaces carried it.
    /// </summary>
    public ItemStack? Stack { get; set; }

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        X = stream.ReadInt();
        Y = (byte)stream.ReadByte();
        Z = stream.ReadInt();
        Side = (byte)stream.ReadByte();
        Stack = stream.ReadItemStack(_items ?? throw new InvalidOperationException("No item catalog was supplied for decoding."));
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(X);
        stream.WriteByte(Y);
        stream.WriteInt(Z);
        stream.WriteByte(Side);
        stream.WriteItemStack(Stack);
    }

    public override int Size() =>
        4
        + 1
        + 4
        + 1
        + StreamExtensions.ItemStackSize(Stack);
}
