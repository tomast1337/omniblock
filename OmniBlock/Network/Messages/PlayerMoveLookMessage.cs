using OmniBlock;

namespace OmniBlock.Network.Messages;

/// <summary>The player turned without moving. See <see cref="PlayerMoveMessage" /> for the priority.</summary>
public sealed class PlayerMoveLookMessage : Message, IPlayerMoveLook
{
    public float Yaw { get; set; }

    public float Pitch { get; set; }

    public bool OnGround { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "player_move_look");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        Yaw = stream.ReadFloat();
        Pitch = stream.ReadFloat();
        OnGround = stream.ReadBoolean();
    }

    public override void Write(Stream stream)
    {
        stream.WriteFloat(Yaw);
        stream.WriteFloat(Pitch);
        stream.WriteBoolean(OnGround);
    }

    public override int Size()
    {
        return
            4
            + 4
            + 1;
    }
}
