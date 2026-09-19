using OmniBlock.Worlds.Lod;

namespace OmniBlock.Network.Messages;

public enum TerrainLodTileStatus : byte
{
    Pending = 0,
    Missing = 1,
    Deferred = 2
}

/// <summary>
///     Explicit negative response for a distant-terrain request. This keeps a client from
///     repeatedly treating a cold cache read, an absent record, and transport backpressure as the
///     same silent timeout.
/// </summary>
public sealed class TerrainLodTileStatusMessage : Message
{
    public static readonly ResourceLocation Id = new(
        Namespace.Get("omniblock"), "terrain_lod_tile_status");

    public int Dimension { get; set; }
    public TerrainLodTileKey Tile { get; set; }
    public TerrainLodTileStatus Status { get; set; }
    public override ResourceLocation Key => Id;

    public override void Read(Stream stream)
    {
        Dimension = stream.ReadInt();
        Tile = new TerrainLodTileKey(stream.ReadVarInt(), stream.ReadInt(), stream.ReadInt());
        var status = checked((byte)stream.ReadByte());
        if (!Enum.IsDefined(typeof(TerrainLodTileStatus), status))
            throw new InvalidDataException($"Unknown terrain LOD tile status {status}.");
        Status = (TerrainLodTileStatus)status;
    }

    public override void Write(Stream stream)
    {
        if (!Enum.IsDefined(Status))
            throw new InvalidOperationException($"Unknown terrain LOD tile status {(byte)Status}.");
        stream.WriteInt(Dimension);
        stream.WriteVarInt(Tile.Level);
        stream.WriteInt(Tile.X);
        stream.WriteInt(Tile.Z);
        stream.WriteByte((byte)Status);
    }

    public override int Size() => sizeof(int) + StreamExtensions.VarIntSize(Tile.Level) +
                                  sizeof(int) * 2 + sizeof(byte);
}
