using System.IO.Compression;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Network.Messages;

/// <summary>One compact, immutable server-cache tile for distant presentation.</summary>
public sealed class TerrainLodTileMessage : Message
{
    private const int MaximumCompressedBytes = 2 * 1024 * 1024 - 128;
    private const int MaximumDecodedBytes = 64 * 1024 * 1024;
    public static readonly ResourceLocation Id = new(
        Namespace.Get("omniblock"), "terrain_lod_tile");

    public int Dimension { get; set; }
    public byte[] Compressed { get; set; } = [];
    private TerrainLodColumnTile? LoopbackTile { get; set; }
    public override ResourceLocation Key => Id;

    public static TerrainLodTileMessage Of(int dimension, TerrainLodColumnTile tile)
    {
        return FromCompressed(dimension, Encode(tile));
    }

    internal static TerrainLodTileMessage Loopback(int dimension, TerrainLodColumnTile tile) =>
        new() { Dimension = dimension, LoopbackTile = tile };

    internal static TerrainLodTileMessage FromCompressed(int dimension, byte[] compressed) =>
        new() { Dimension = dimension, Compressed = compressed };

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

    public TerrainLodColumnTile Decode()
    {
        if (LoopbackTile is { } tile) return tile;
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
        return TerrainLodColumnTileCacheStore.DecodePortable(output.GetBuffer().AsSpan(
            0, checked((int)output.Length)));
    }

    public override void Read(Stream stream)
    {
        Dimension = stream.ReadInt();
        Compressed = stream.ReadByteArray(MaximumCompressedBytes);
    }

    public override void Write(Stream stream)
    {
        if (LoopbackTile is not null)
            throw new InvalidOperationException("A loopback terrain LOD tile has no wire payload.");
        if (Compressed.Length > MaximumCompressedBytes)
            throw new InvalidOperationException("Terrain LOD tile payload exceeds its wire limit.");
        stream.WriteInt(Dimension);
        stream.WriteByteArray(Compressed);
    }

    public override int Size() => sizeof(int) + StreamExtensions.ByteArraySize(Compressed);
}
