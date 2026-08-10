using OmniBlock;

namespace OmniBlock.Network.Messages;

/// <summary>
///     A state change the client is asking for: respawn, open inventory, leave a bed.
///     Replaces <c>ClientCommandC2SPacket</c>.
/// </summary>
public sealed class ClientCommandMessage : Message
{
    public int EntityId { get; set; }

    /// <summary>1 respawns, 2 opens the inventory, 3 leaves a bed.</summary>
    public sbyte Mode { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "client_command");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        EntityId = stream.ReadInt();
        Mode = (sbyte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(EntityId);
        stream.WriteByte((byte)Mode);
    }

    public override int Size()
    {
        return
            4
            + 1;
    }
}
