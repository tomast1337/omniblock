using OmniBlock;

namespace OmniBlock.Network.Messages;

/// <summary>
///     The player's movement intent for a tick. Replaces <c>PlayerInputC2SPacket</c>.
///     <para>
///         The movement rewrite will grow this into the redundant input frame batches client-side
///         prediction needs. Migrating it ahead of that work rather than alongside it is
///         deliberate: the payload is unchanged, so the change is free, and it means the rewrite
///         starts from a message with a schema version to bump rather than from a packet ID to
///         replace.
///     </para>
/// </summary>
public sealed class PlayerInputMessage : Message
{
    public float Sideways { get; set; }

    public float Forward { get; set; }

    public float Pitch { get; set; }

    public float Yaw { get; set; }

    public bool Jumping { get; set; }

    public bool Sneaking { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "player_input");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        Sideways = stream.ReadFloat();
        Forward = stream.ReadFloat();
        Pitch = stream.ReadFloat();
        Yaw = stream.ReadFloat();
        Jumping = stream.ReadBoolean();
        Sneaking = stream.ReadBoolean();
    }

    public override void Write(Stream stream)
    {
        stream.WriteFloat(Sideways);
        stream.WriteFloat(Forward);
        stream.WriteFloat(Pitch);
        stream.WriteFloat(Yaw);
        stream.WriteBoolean(Jumping);
        stream.WriteBoolean(Sneaking);
    }

    public override int Size()
    {
        return
            4
            + 4
            + 4
            + 4
            + 1
            + 1;
    }
}
