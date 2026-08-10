using OmniBlock;

namespace OmniBlock.Network.Messages;

public class PlayerRespawnMessage : Message
{
    /// <summary>The dimension the player respawns into. In Beta 1.7.3 this is
    /// always 0 (the overworld); the Nether existed but respawning into it was
    /// not implemented.</summary>
    public sbyte DimensionId { get; set; }

    public static readonly ResourceLocation Id = new(Namespace.Get("beta"), "player_respawn");

    public override ResourceLocation Key => Id;

    public override int SchemaVersion => 1;

    public override void Read(Stream stream)
    {
        DimensionId = (sbyte)stream.ReadByte();
    }

    public override void Write(Stream stream)
    {
        stream.WriteByte((byte)DimensionId);
    }

    public override int Size()
    {
        return
            1;
    }
}
