using OmniBlock;

namespace OmniBlock.Network.Messages;

/// <summary>
///     The player both moved and turned, and the only variant the server ever sends: a teleport has
///     to state both. See <see cref="PlayerMoveMessage" /> for the priority.
/// </summary>
public sealed class PlayerMoveFullMessage : Message, IPlayerMovePosition, IPlayerMoveLook
{
    public double X { get; set; }

    public double Y { get; set; }

    public double EyeHeight { get; set; }

    public double Z { get; set; }

    public float Yaw { get; set; }

    public float Pitch { get; set; }

    public bool OnGround { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "player_move_full");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        X = stream.ReadDouble();
        Y = stream.ReadDouble();
        EyeHeight = stream.ReadDouble();
        Z = stream.ReadDouble();
        Yaw = stream.ReadFloat();
        Pitch = stream.ReadFloat();
        OnGround = stream.ReadBoolean();
    }

    public override void Write(Stream stream)
    {
        stream.WriteDouble(X);
        stream.WriteDouble(Y);
        stream.WriteDouble(EyeHeight);
        stream.WriteDouble(Z);
        stream.WriteFloat(Yaw);
        stream.WriteFloat(Pitch);
        stream.WriteBoolean(OnGround);
    }

    public override int Size()
    {
        return
            8
            + 8
            + 8
            + 8
            + 4
            + 4
            + 1;
    }
}
