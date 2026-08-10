using System.IO.Compression;
using System.Numerics;
using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Network.Messages;

/// <summary>
///     The sky and block light for whole 16-block sections of one chunk column, compressed.
/// </summary>
/// <remarks>
///     <para>
///         Light travels apart from blocks and as whole sections, because a per-cell light byte
///         riding on a block update cannot express the changes that matter. A cell's light can
///         change with no block change anywhere near it, and — worse — the pass that first lights a
///         chunk writes the arrays directly rather than through the lighting engine, so it produces
///         no per-cell event to attach a byte to. A receiver sent a chunk before that pass ran then
///         holds zeros that nothing is able to correct.
///     </para>
///     <para>
///         A section is a snapshot of the array rather than a sequence of edits, so it does not
///         matter whether the receiver saw the ones before it, whether they arrived in order, or
///         whether the sender read it while propagation was still running. Late and duplicate
///         sections are both harmless.
///     </para>
///     <para>
///         The cost is 4 KB per section before compression against one byte per cell, which is why
///         the sender works from a dirty mask and sends only sections that were actually written.
///     </para>
/// </remarks>
[WireMessage("omniblock:light_sections")]
public sealed partial class LightSectionsMessage : Message
{
    /// <summary>
    ///     Every section of a column at once, which is the largest this can honestly be. Checked
    ///     during decompression, since a limit tested on the result has already paid for the
    ///     allocation it was meant to refuse.
    /// </summary>
    public static int MaxDecodedBytes => Chunk.LightSectionCount * Chunk.LightSectionPayloadBytes;

    [WireField]
    public int ChunkX { get; set; }

    [WireField]
    public int ChunkZ { get; set; }

    /// <summary>
    ///     One bit per section, lowest bit lowest section. The payload holds one section's worth of
    ///     bytes for each set bit, in ascending section order.
    /// </summary>
    [WireField]
    public uint Sections { get; set; }

    /// <summary>The zlib'd run of sections, each sky nibbles then block nibbles.</summary>
    [WireField(MaxLength = 1 << 20)]
    public byte[] Compressed { get; set; } = [];

    /// <summary>Reads the named sections out of a chunk and compresses them.</summary>
    public static LightSectionsMessage Of(Chunk chunk, uint sections)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        byte[] raw = new byte[BitOperations.PopCount(sections) * Chunk.LightSectionPayloadBytes];
        int offset = 0;

        for (int section = 0; section < Chunk.LightSectionCount; section++)
        {
            if ((sections & (1u << section)) == 0)
            {
                continue;
            }

            chunk.CopyLightSection(section, raw.AsSpan(offset, Chunk.LightSectionPayloadBytes));
            offset += Chunk.LightSectionPayloadBytes;
        }

        MemoryStream output = new(raw.Length / 8);
        using (ZLibStream compressor = new(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            compressor.Write(raw);
        }

        return new LightSectionsMessage
        {
            ChunkX = chunk.X,
            ChunkZ = chunk.Z,
            Sections = sections,
            Compressed = output.ToArray(),
        };
    }

    /// <summary>
    ///     Writes every section this message names into a chunk.
    /// </summary>
    /// <remarks>
    ///     Refuses a payload that does not hold exactly what the mask claims. The two come from the
    ///     same peer, so a mask naming fewer sections than the payload carries cannot be used to
    ///     smuggle an over-large expansion past the limit.
    /// </remarks>
    public void ApplyTo(Chunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        byte[] raw = Decompress();
        int expected = BitOperations.PopCount(Sections & SectionMask) * Chunk.LightSectionPayloadBytes;

        if (raw.Length != expected)
        {
            throw new InvalidDataException(
                $"Light payload is {raw.Length} bytes for a mask naming {expected}; refusing to apply.");
        }

        int offset = 0;

        for (int section = 0; section < Chunk.LightSectionCount; section++)
        {
            if ((Sections & (1u << section)) == 0)
            {
                continue;
            }

            chunk.ApplyLightSection(section, raw.AsSpan(offset, Chunk.LightSectionPayloadBytes));
            offset += Chunk.LightSectionPayloadBytes;
        }
    }

    /// <summary>Bits that name a section this world actually has.</summary>
    private static uint SectionMask =>
        Chunk.LightSectionCount >= 32 ? uint.MaxValue : (1u << Chunk.LightSectionCount) - 1u;

    private byte[] Decompress()
    {
        int limit = MaxDecodedBytes;

        using MemoryStream input = new(Compressed, writable: false);
        using ZLibStream decompressor = new(input, CompressionMode.Decompress);

        MemoryStream output = new(Math.Min(limit, Compressed.Length * 8));
        byte[] buffer = new byte[8192];
        int read;

        while ((read = decompressor.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > limit)
            {
                throw new InvalidDataException(
                    $"Light sections expand past {limit} bytes; refusing to continue.");
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
}
