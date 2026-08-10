namespace OmniBlock.Network.Messages;

/// <summary>The player moved without turning. See <see cref="PlayerMoveMessage" /> for the priority.</summary>
public sealed class PlayerMovePositionMessage : Message, IPlayerMovePosition
{
    public static readonly ResourceLocation Id = new(Namespace.Get("omniblock"), "player_move_position");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;
    public double X { get; set; }

    public double Y { get; set; }

    public double EyeHeight { get; set; }

    public double Z { get; set; }

    public bool OnGround { get; set; }

    public override void Read(Stream stream)
    {
        X = stream.ReadDouble();
        Y = stream.ReadDouble();
        EyeHeight = stream.ReadDouble();
        Z = stream.ReadDouble();
        OnGround = stream.ReadBoolean();
    }

    public override void Write(Stream stream)
    {
        stream.WriteDouble(X);
        stream.WriteDouble(Y);
        stream.WriteDouble(EyeHeight);
        stream.WriteDouble(Z);
        stream.WriteBoolean(OnGround);
    }

    public override int Size() =>
        8
        + 8
        + 8
        + 8
        + 1;
}
