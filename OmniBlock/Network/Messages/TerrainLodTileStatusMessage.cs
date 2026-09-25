using OmniBlock.Worlds.Lod;
using OmniBlock.Util;

namespace OmniBlock.Network.Messages;

public enum TerrainLodTileStatus : byte
{
    Pending = 0,
    Missing = 1,
    Deferred = 2,
    Incompatible = 3,
    Invalidated = 4
}

/// <summary>
///     Explicit negative response for a distant-terrain request. This keeps a client from
///     repeatedly treating a cold cache read, an absent record, and transport backpressure as the
///     same silent timeout.
/// </summary>
public sealed class TerrainLodTileStatusMessage : Message
{
    private const int MaximumIdentityLength = 128;
    private const int MaximumDiagnosticLength = 512;
    public static readonly ResourceLocation Id = new(
        Namespace.Get("omniblock"), "terrain_lod_tile_status_v4");

    public int Dimension { get; set; }
    public string CacheIdentity { get; set; } = "";
    public TerrainLodTileKey Tile { get; set; }
    public long Generation { get; set; }
    public TerrainLodTileStatus Status { get; set; }
    public string Diagnostic { get; set; } = "";
    public override ResourceLocation Key => Id;
    public override int SchemaVersion => 4;
    public override SendPriority Priority => Status == TerrainLodTileStatus.Invalidated
        ? SendPriority.Normal : SendPriority.Bulk;

    public override void Read(Stream stream)
    {
        Dimension = stream.ReadInt();
        CacheIdentity = stream.ReadString(MaximumIdentityLength);
        Tile = new TerrainLodTileKey(stream.ReadVarInt(), stream.ReadInt(), stream.ReadInt());
        Generation = stream.ReadLong();
        if (Generation < 0) throw new InvalidDataException("Negative terrain LOD generation.");
        var status = checked((byte)stream.ReadByte());

        if (!Enum.IsDefined(typeof(TerrainLodTileStatus), status))
            throw new InvalidDataException($"Unknown terrain LOD tile status {status}.");

        Status = (TerrainLodTileStatus)status;
        Diagnostic = stream.ReadString(MaximumDiagnosticLength);
    }

    public override void Write(Stream stream)
    {
        if (!Enum.IsDefined(Status))
            throw new InvalidOperationException($"Unknown terrain LOD tile status {(byte)Status}.");
        if (Generation < 0)
            throw new InvalidOperationException("Negative terrain LOD generation.");
        
        if (ModifiedUtf8.GetByteCount(Diagnostic) > MaximumDiagnosticLength)
            throw new InvalidOperationException($"Terrain LOD status diagnostic exceeds {MaximumDiagnosticLength} bytes.");

        stream.WriteInt(Dimension);
        stream.WriteString(CacheIdentity);
        stream.WriteVarInt(Tile.Level);
        stream.WriteInt(Tile.X);
        stream.WriteInt(Tile.Z);
        stream.WriteLong(Generation);
        stream.WriteByte((byte)Status);
        stream.WriteString(Diagnostic);
    }

    public override int Size() => sizeof(int) + sizeof(ushort) +
                                  ModifiedUtf8.GetByteCount(CacheIdentity) +
                                  StreamExtensions.VarIntSize(Tile.Level) + sizeof(int) * 2 +
                                  sizeof(long) +
                                  sizeof(byte) + sizeof(ushort) +
                                  ModifiedUtf8.GetByteCount(Diagnostic);
}
