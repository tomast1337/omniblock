using System.IO.Compression;
using OmniBlock.Network.Chunks;

namespace OmniBlock.Network.Messages;

/// <summary>
///     One whole chunk, encoded by <see cref="ChunkBlobCodec" /> and then compressed.
///     <para>
///         Measured over 200 chunks of a played-in save this costs 1,966 bytes against the 2,610
///         the raw format takes — a quarter, and worth stating because the saving looks like it
///         should be far larger. It is not, because zlib over the raw chunk already finds most of
///         the redundancy the palette removes.
///     </para>
///     <para>
///         <b>Full chunks only, deliberately.</b> The codec's unit is a chunk, and a sub-box has no
///         sections to encode. An arbitrary box goes as <see cref="RegionDataMessage" /> instead;
///         stretching the codec to cover both would cost the section structure the whole encoding
///         rests on, to save bytes on a path that is already rare.
///     </para>
/// </summary>
[WireMessage("omniblock:chunk_data")]
public sealed partial class ChunkDataMessage : Message
{
    /// <summary>
    ///     Bulk, and the reason the priority split exists at all. A chunk must never overtake an
    ///     entity update or a clock probe: it is the one payload large enough that letting it go
    ///     first is visible as a stall.
    /// </summary>
    public override SendPriority Priority => SendPriority.Normal;

    /// <summary>
    ///     Refuses a blob that would expand past what a chunk can possibly hold. Without it a
    ///     hostile or corrupt payload decides how much memory this peer allocates, and the
    ///     decompressor has no reason of its own to stop.
    /// </summary>
    public const int MaxDecodedBytes = 256 * 1024;

    [WireField]
    public int ChunkX { get; set; }

    [WireField]
    public int ChunkZ { get; set; }

    /// <summary>The zlib'd output of <see cref="ChunkBlobCodec.Encode" />.</summary>
    [WireField(MaxLength = MaxDecodedBytes)]
    public byte[] Compressed { get; set; } = [];

    /// <summary>
    ///     Encodes and compresses a chunk's arrays into a message.
    ///     <para>
    ///         Compression stays here rather than inside the codec: the codec's job is to remove the
    ///         structural redundancy a byte-oriented compressor is worst at, and which general
    ///         compressor runs over the result afterwards is a transport decision. zlib because it
    ///         is in the framework; swapping it for zstd is a dependency question rather than a
    ///         format one.
    ///     </para>
    /// </summary>
    public static ChunkDataMessage Of(
        int chunkX,
        int chunkZ,
        ReadOnlySpan<byte> blocks,
        ReadOnlySpan<byte> meta,
        ReadOnlySpan<byte> blockLight,
        ReadOnlySpan<byte> skyLight) => new()
        {
            ChunkX = chunkX,
            ChunkZ = chunkZ,
            Compressed = Compress(ChunkBlobCodec.Encode(blocks, meta, blockLight, skyLight)),
        };

    /// <summary>
    ///     Compresses an encoded blob. Separate from <see cref="Of" /> because the content-hash path
    ///     needs the blob itself to hash before deciding whether to send it at all, and encoding it
    ///     twice to get both would double the cost of the case that sends nothing.
    /// </summary>
    public static byte[] Compress(ReadOnlySpan<byte> blob)
    {
        MemoryStream output = new(blob.Length / 4);
        using (ZLibStream compressor = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(blob);
        }

        return output.ToArray();
    }

    /// <summary>
    ///     Decompresses back to the codec's blob.
    ///     <para>
    ///         Bounded by <see cref="MaxDecodedBytes" /> during decompression, not after it. A limit
    ///         checked on the result is not a limit: the allocation has already happened by then,
    ///         which is the whole of what a decompression bomb is asking for.
    ///     </para>
    /// </summary>
    public byte[] Decompress() => Decompress(Compressed);

    /// <summary>
    ///     Decompresses a payload in this message's format. Static so the chunk cache, which stores
    ///     the compressed bytes exactly as they arrived, can read them back without going through a
    ///     message it does not have.
    /// </summary>
    public static byte[] Decompress(byte[] compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);

        using MemoryStream input = new(compressed, writable: false);
        using ZLibStream decompressor = new(input, CompressionMode.Decompress);

        MemoryStream output = new(compressed.Length * 4);
        byte[] buffer = new byte[8192];
        int read;

        while ((read = decompressor.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > MaxDecodedBytes)
            {
                throw new InvalidDataException(
                    $"Chunk data expands past {MaxDecodedBytes} bytes; refusing to continue.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
