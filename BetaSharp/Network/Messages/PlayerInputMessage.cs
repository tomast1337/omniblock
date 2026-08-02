namespace BetaSharp.Network.Messages;

/// <summary>
///     The player's movement intent for a tick. Replaces <c>PlayerInputC2SPacket</c>.
///     <para>
///         This is the message phase 5 of <c>docs/network-rewrite.md</c> will rebuild into the
///         redundant input frame batches client-side prediction needs. Migrating it now rather than
///         waiting is deliberate: the payload is unchanged, so the change is free, and it means the
///         movement rewrite starts from a message with a schema version to bump rather than from a
///         packet ID to replace.
///     </para>
/// </summary>
[WireMessage("betasharp:player_input")]
public sealed partial class PlayerInputMessage : Message
{
    [WireField]
    public float Sideways { get; set; }

    [WireField]
    public float Forward { get; set; }

    [WireField]
    public float Pitch { get; set; }

    [WireField]
    public float Yaw { get; set; }

    [WireField]
    public bool Jumping { get; set; }

    [WireField]
    public bool Sneaking { get; set; }
}
