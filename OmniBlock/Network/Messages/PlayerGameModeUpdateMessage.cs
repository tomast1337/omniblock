using OmniBlock;
using OmniBlock.Util;

namespace OmniBlock.Network.Messages;

public class PlayerGameModeUpdateMessage : Message
{
    public string GameModeNamespace { get; set; } = "";

    public string GameModeName { get; set; } = "";

    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "player_game_mode_update");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        GameModeNamespace = stream.ReadString();
        GameModeName = stream.ReadString();
    }

    public override void Write(Stream stream)
    {
        stream.WriteString(GameModeNamespace);
        stream.WriteString(GameModeName);
    }

    public override int Size()
    {
        return
            (2 + ModifiedUtf8.GetByteCount(GameModeNamespace))
            + (2 + ModifiedUtf8.GetByteCount(GameModeName));
    }
}
