using System.IO.Compression;
using OmniBlock.Util;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Network.Messages;

/// <summary>One compact, immutable server-cache tile for distant presentation.</summary>
public sealed class TerrainLodTileMessage : Message
{
    private const int MaximumIdentityLength = 128;
    private const int MaximumCompressedBytes = TerrainLodScaleBudget.MaximumCompressedTileBytes;
    private const int MaximumDecodedBytes = TerrainLodScaleBudget.MaximumDecodedTileBytes;
    public static readonly ResourceLocation Id = new(
        Namespace.Get("omniblock"), "terrain_lod_tile_v2");

    public int Dimension { get; set; }
    public string CacheIdentity { get; set; } = "";
    public byte[] Compressed { get; set; } = [];
    private TerrainLodColumnTile? LoopbackTile { get; set; }
    public override ResourceLocation Key => Id;
    public override int SchemaVersion => 2;
    public override SendPriority Priority => SendPriority.Bulk;

    public static TerrainLodTileMessage Of(
        int dimension,
        TerrainLodColumnTile tile,
        string cacheIdentity = "")
    {
        return FromCompressed(dimension, Encode(tile), cacheIdentity);
    }

    internal static TerrainLodTileMessage Loopback(
        int dimension,
        TerrainLodColumnTile tile,
        string cacheIdentity = "") =>
        new() { Dimension = dimension, CacheIdentity = cacheIdentity, LoopbackTile = tile };

    internal static TerrainLodTileMessage FromCompressed(
        int dimension,
        byte[] compressed,
        string cacheIdentity = "") =>
        new() { Dimension = dimension, CacheIdentity = cacheIdentity, Compressed = compressed };

    internal static byte[] Encode(TerrainLodColumnTile tile)
    {
        ArgumentNullException.ThrowIfNull(tile);
        var payload = TerrainLodColumnTileCacheStore.EncodePortable(tile);
        using MemoryStream output = new(Math.Max(256, payload.Length / 4));
        using (var compressor = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true))
            compressor.Write(payload);
        var compressed = output.ToArray();
        if (compressed.Length > MaximumCompressedBytes)
            throw new InvalidDataException(
                $"Terrain LOD tile {tile.Key} compresses to {compressed.Length} bytes; " +
                $"the message limit is {MaximumCompressedBytes}.");
        return compressed;
    }

    public TerrainLodColumnTile Decode(
        int maximumSpatialLevel = TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel)
    {
        if (maximumSpatialLevel is < 0 or
            > TerrainLodSpatialPolicy.MaximumGeneratedSpatialLevel)
            throw new ArgumentOutOfRangeException(nameof(maximumSpatialLevel));
        if (LoopbackTile is { } loopbackTile)
        {
            ValidateSpatialLevel(loopbackTile, maximumSpatialLevel);
            return loopbackTile;
        }
        using MemoryStream input = new(Compressed, writable: false);
        using ZLibStream decompressor = new(input, CompressionMode.Decompress);
        using MemoryStream output = new(Math.Min(MaximumDecodedBytes, Compressed.Length * 4));
        var buffer = new byte[8192];
        int read;
        while ((read = decompressor.Read(buffer)) > 0)
        {
            if (output.Length + read > MaximumDecodedBytes)
                throw new InvalidDataException(
                    $"Terrain LOD tile expands past {MaximumDecodedBytes} bytes.");
            output.Write(buffer, 0, read);
        }
        var tile = TerrainLodColumnTileCacheStore.DecodePortable(output.GetBuffer().AsSpan(
            0, checked((int)output.Length)));
        ValidateSpatialLevel(tile, maximumSpatialLevel);
        return tile;
    }

    public override void Read(Stream stream)
    {
        Dimension = stream.ReadInt();
        CacheIdentity = stream.ReadString(MaximumIdentityLength);
        Compressed = stream.ReadByteArray(MaximumCompressedBytes);
    }

    public override void Write(Stream stream)
    {
        if (LoopbackTile is not null)
            throw new InvalidOperationException("A loopback terrain LOD tile has no wire payload.");
        if (Compressed.Length > MaximumCompressedBytes)
            throw new InvalidOperationException("Terrain LOD tile payload exceeds its wire limit.");
        stream.WriteInt(Dimension);
        stream.WriteString(CacheIdentity);
        stream.WriteByteArray(Compressed);
    }

    public override int Size() => sizeof(int) + sizeof(ushort) +
                                  ModifiedUtf8.GetByteCount(CacheIdentity) +
                                  StreamExtensions.ByteArraySize(Compressed);

    private static void ValidateSpatialLevel(
        TerrainLodColumnTile tile,
        int maximumSpatialLevel)
    {
        if (tile.Key.Level > maximumSpatialLevel)
            throw new InvalidDataException(
                $"Terrain LOD response contains spatial level {tile.Key.Level}, exceeding the " +
                $"negotiated maximum {maximumSpatialLevel}.");
    }
}
