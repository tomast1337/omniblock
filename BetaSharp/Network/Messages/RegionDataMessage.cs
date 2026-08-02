using System.IO.Compression;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Network.Messages;

/// <summary>
///     A rectangular box of raw block, metadata and light data, compressed.
///     <para>
///         The batched fallback <c>ChunkMap</c> reaches for when one tick dirties more blocks in a
///         chunk than individual updates are worth, and the full-chunk path for a peer that is not
///         taking the palette encoding. Distinct from <see cref="ChunkDataMessage" />, which carries
///         a whole chunk in that encoding: the codec's unit is a chunk and a sub-box has no sections
///         to encode, so the two cannot be one message. Sending the whole chunk instead would be
///         smaller for a change scattered across the column and several times larger for the tight
///         cluster this path actually sees — a piston, a tree, a small explosion — which is why the
///         box survives the migration rather than being folded in.
///     </para>
///     <para>
///         <b>No loopback shortcut.</b> The packet this replaces had one, in the shape of a
///         <c>ProcessForInternal</c> that swapped the compressed payload back out for the raw array.
///         It never avoided the expensive half: the payload was compressed unconditionally when the
///         packet was built and the result was then discarded, so all loopback ever saved was the
///         matching inflate. Paying that inflate is worth not having a second representation whose
///         reported size is a fiction — and the chunk pacer's budget is denominated in exactly that
///         reported size.
///     </para>
/// </summary>
[WireMessage("betasharp:region_data")]
public sealed partial class RegionDataMessage : Message
{
    /// <summary>
    ///     Refuses a payload that would expand past what the declared box can possibly hold. The
    ///     ceiling is a full chunk column: 16 x 128 x 16 blocks at two and a half bytes each, rounded
    ///     up. Checked during decompression rather than after it, since a limit tested on the result
    ///     has already paid for the allocation it was meant to refuse.
    /// </summary>
    public const int MaxDecodedBytes = 128 * 1024;

    [WireField]
    public int X { get; set; }

    [WireField]
    public short Y { get; set; }

    [WireField]
    public int Z { get; set; }

    [WireField]
    public byte SizeX { get; set; }

    /// <summary>
    ///     Wider than <see cref="SizeX" /> and <see cref="SizeZ" /> because those are bounded by the
    ///     chunk's 16-block footprint and this one is bounded by world height, which
    ///     <c>ChuckFormat.WorldHeight</c> documents as changeable. A byte works today and would
    ///     silently truncate the first time it is raised.
    /// </summary>
    [WireField]
    public short SizeY { get; set; }

    [WireField]
    public byte SizeZ { get; set; }

    /// <summary>The zlib'd block/metadata/light run for the box, in <c>GetChunkData</c> order.</summary>
    [WireField(MaxLength = MaxDecodedBytes)]
    public byte[] Compressed { get; set; } = [];

    /// <summary>Reads the box out of the world and compresses it.</summary>
    public static RegionDataMessage Of(int x, int y, int z, int sizeX, int sizeY, int sizeZ, IWorldContext world)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeX, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeY, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeZ, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sizeX, byte.MaxValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sizeY, short.MaxValue);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sizeZ, byte.MaxValue);

        byte[] raw = world.ChunkHost.GetChunkData(x, y, z, sizeX, sizeY, sizeZ);

        MemoryStream output = new(raw.Length / 4);
        using (ZLibStream compressor = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(raw);
        }

        return new RegionDataMessage
        {
            X = x,
            Y = (short)y,
            Z = z,
            SizeX = (byte)sizeX,
            SizeY = (short)sizeY,
            SizeZ = (byte)sizeZ,
            Compressed = output.ToArray(),
        };
    }

    /// <summary>
    ///     Decompresses the payload, bounded by the volume the box itself declares rather than by the
    ///     ceiling alone. The header and the payload come from the same peer, so a box small enough
    ///     to look harmless cannot be used to smuggle a large expansion past the check.
    /// </summary>
    public byte[] Decompress()
    {
        int limit = Math.Min(MaxDecodedBytes, SizeX * SizeY * SizeZ * 5 / 2 + 1);

        using MemoryStream input = new(Compressed, writable: false);
        using ZLibStream decompressor = new(input, CompressionMode.Decompress);

        MemoryStream output = new(Math.Min(limit, Compressed.Length * 4));
        byte[] buffer = new byte[8192];
        int read;

        while ((read = decompressor.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > limit)
            {
                throw new InvalidDataException(
                    $"Region data expands past {limit} bytes for a {SizeX}x{SizeY}x{SizeZ} box; refusing to continue.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
