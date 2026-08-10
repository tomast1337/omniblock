using OmniBlock;

namespace OmniBlock.Network.Messages;

public class PlayerSpawnPositionMessage : Message
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "player_spawn_position");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        X = stream.ReadInt();
        Y = stream.ReadInt();
        Z = stream.ReadInt();
    }

    public override void Write(Stream stream)
    {
        stream.WriteInt(X);
        stream.WriteInt(Y);
        stream.WriteInt(Z);
    }

    public override int Size()
    {
        return
            4
            + 4
            + 4;
    }
}
