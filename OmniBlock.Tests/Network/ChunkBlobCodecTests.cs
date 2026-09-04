using System.IO.Compression;
using OmniBlock.Network.Chunks;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Gen.Chunks;
using Xunit.Abstractions;

namespace OmniBlock.Tests.Network;

/// <summary>
///     <see cref="ChunkBlobCodec" />: the chunk wire encoding.
///     <para>
///         Two things have to hold. The blob must round-trip <em>exactly</em> — a wire format that
///         loses a metadata nibble produces terrain that is subtly wrong in a way no player reports
///         as a network bug. And it must actually be smaller, measured on terrain the generator
///         produces rather than on a synthetic chunk chosen to flatter it.
///     </para>
/// </summary>
public sealed class ChunkBlobCodecTests(ITestOutputHelper output)
{
    /// <summary>
    ///     What the inherited format sends: ids plus three nibble arrays, uncompressed.
    /// </summary>
    private const int RawChunkBytes = 81_920;

    /// <summary>
    ///     Real Beta terrain, from the real generator at a real seed. Synthetic chunks are no use
    ///     for the size question — the whole claim is about what generated worlds look like, and it
    ///     is easy to build a chunk that palettises beautifully and does not exist.
    /// </summary>
    private static Chunk Generate(long seed, int chunkX, int chunkZ)
    {
        FakeWorldContext world = new();
        OverworldChunkGenerator generator = new(world, seed);

        return generator.GetChunk(chunkX, chunkZ);
    }

    private static byte[] EncodeOf(Chunk chunk) => ChunkBlobCodec.Encode(
        chunk.Blocks, chunk.Meta.Bytes, chunk.BlockLight.Bytes, chunk.SkyLight.Bytes);

    private static void AssertRoundTrips(Chunk chunk)
    {
        var blob = EncodeOf(chunk);

        var blocks = new byte[chunk.Blocks.Length];
        var meta = new byte[chunk.Meta.Bytes.Length];
        var blockLight = new byte[chunk.BlockLight.Bytes.Length];
        var skyLight = new byte[chunk.SkyLight.Bytes.Length];

        ChunkBlobCodec.Decode(blob, blocks, meta, blockLight, skyLight);

        Assert.Equal(chunk.Blocks, blocks);
        Assert.Equal(chunk.Meta.Bytes, meta);
        Assert.Equal(chunk.BlockLight.Bytes, blockLight);
        Assert.Equal(chunk.SkyLight.Bytes, skyLight);
    }

    // ---- correctness ----

    /// <summary>
    ///     The property everything else rests on. Generated terrain exercises every encoding at once:
    ///     air sections above the surface go uniform, terrain sections palettise, and the light
    ///     arrays span uniform, palette and raw within one chunk.
    /// </summary>
    [Theory]
    [InlineData(1L, 0, 0)]
    [InlineData(1L, 12, -7)]
    [InlineData(-4_172_144_997_902_289_642L, 0, 0)] // the Beta seed with the well-known spawn
    [InlineData(987_654_321L, 100, 100)]
    public void Generated_terrain_round_trips_exactly(long seed, int chunkX, int chunkZ) =>
        AssertRoundTrips(Generate(seed, chunkX, chunkZ));

    /// <summary>
    ///     An empty chunk is the degenerate case in both directions: every field takes its uniform
    ///     encoding, and any off-by-one in the section walk shows up immediately because every byte
    ///     is supposed to be identical.
    /// </summary>
    [Fact]
    public void An_all_air_chunk_round_trips_and_costs_almost_nothing()
    {
        var blocks = new byte[ChuckFormat.ChunkSize];
        var meta = new byte[ChuckFormat.ChunkSize / 2];
        var blockLight = new byte[ChuckFormat.ChunkSize / 2];
        var skyLight = new byte[ChuckFormat.ChunkSize / 2];
        skyLight.AsSpan().Fill(0xFF); // full sky throughout

        var blob = ChunkBlobCodec.Encode(blocks, meta, blockLight, skyLight);

        var decodedBlocks = new byte[blocks.Length];
        var decodedMeta = new byte[meta.Length];
        var decodedBlockLight = new byte[blockLight.Length];
        var decodedSkyLight = new byte[skyLight.Length];

        ChunkBlobCodec.Decode(blob, decodedBlocks, decodedMeta, decodedBlockLight, decodedSkyLight);

        Assert.Equal(blocks, decodedBlocks);
        Assert.Equal(meta, decodedMeta);
        Assert.Equal(blockLight, decodedBlockLight);
        Assert.Equal(skyLight, decodedSkyLight);

        // Eight sections, three uniform fields each, three bytes for states and two for each light
        // field, plus a two-byte header. Nothing about 81,920 bytes of air is worth sending.
        Assert.True(blob.Length < 100, $"an empty chunk encoded to {blob.Length} bytes");
    }

    /// <summary>
    ///     Every distinct state, so the palette is at its widest and the packing is at eight bits.
    ///     This is the boundary the raw fallback sits just past, and getting it wrong would corrupt
    ///     exactly the chunks a builder cares most about.
    /// </summary>
    [Fact]
    public void A_section_at_the_palette_limit_round_trips()
    {
        var blocks = new byte[ChuckFormat.ChunkSize];
        var meta = new byte[ChuckFormat.ChunkSize / 2];
        var blockLight = new byte[ChuckFormat.ChunkSize / 2];
        var skyLight = new byte[ChuckFormat.ChunkSize / 2];

        // 256 distinct states across the first section: 16 ids x 16 metadata values.
        for (var i = 0; i < ChunkBlobCodec.SectionVolume; i++)
        {
            var x = (i >> 8) & 15;
            var z = (i >> 4) & 15;
            var y = i & 15;
            var index = ChuckFormat.GetIndex(x, y, z);

            blocks[index] = (byte)(1 + (i & 15));
            var nibble = (i >> 4) & 15;
            meta[index >> 1] = (byte)((index & 1) == 0
                ? (meta[index >> 1] & 0xF0) | nibble
                : (meta[index >> 1] & 0x0F) | (nibble << 4));
        }

        var blob = ChunkBlobCodec.Encode(blocks, meta, blockLight, skyLight);

        var decodedBlocks = new byte[blocks.Length];
        var decodedMeta = new byte[meta.Length];
        var decodedBlockLight = new byte[blockLight.Length];
        var decodedSkyLight = new byte[skyLight.Length];

        ChunkBlobCodec.Decode(blob, decodedBlocks, decodedMeta, decodedBlockLight, decodedSkyLight);

        Assert.Equal(blocks, decodedBlocks);
        Assert.Equal(meta, decodedMeta);
    }

    /// <summary>
    ///     Past the palette limit the section falls back to raw, which must still be exact. A palette
    ///     that silently truncated here would corrupt only heavily-built chunks, which is the worst
    ///     possible failure distribution.
    /// </summary>
    [Fact]
    public void A_section_past_the_palette_limit_falls_back_to_raw_without_loss()
    {
        var blocks = new byte[ChuckFormat.ChunkSize];
        var meta = new byte[ChuckFormat.ChunkSize / 2];
        var blockLight = new byte[ChuckFormat.ChunkSize / 2];
        var skyLight = new byte[ChuckFormat.ChunkSize / 2];

        // 4,096 distinct states in one section: id and metadata both vary across the whole range.
        for (var i = 0; i < ChunkBlobCodec.SectionVolume; i++)
        {
            var x = (i >> 8) & 15;
            var z = (i >> 4) & 15;
            var y = i & 15;
            var index = ChuckFormat.GetIndex(x, y, z);

            blocks[index] = (byte)(1 + (i >> 4));
            var nibble = i & 15;
            meta[index >> 1] = (byte)((index & 1) == 0
                ? (meta[index >> 1] & 0xF0) | nibble
                : (meta[index >> 1] & 0x0F) | (nibble << 4));
        }

        var blob = ChunkBlobCodec.Encode(blocks, meta, blockLight, skyLight);

        var decodedBlocks = new byte[blocks.Length];
        var decodedMeta = new byte[meta.Length];
        var decodedBlockLight = new byte[blockLight.Length];
        var decodedSkyLight = new byte[skyLight.Length];

        ChunkBlobCodec.Decode(blob, decodedBlocks, decodedMeta, decodedBlockLight, decodedSkyLight);

        Assert.Equal(blocks, decodedBlocks);
        Assert.Equal(meta, decodedMeta);

        // Raw never costs more than the format it replaces, which is what makes it a safe fallback.
        Assert.True(blob.Length <= RawChunkBytes, $"raw fallback grew the chunk to {blob.Length} bytes");
    }

    // ---- robustness ----

    [Fact]
    public void A_truncated_blob_is_refused_rather_than_read_off_the_end()
    {
        var blob = EncodeOf(Generate(1L, 0, 0));

        Assert.Throws<InvalidDataException>(() => ChunkBlobCodec.Decode(
            blob.AsSpan(0, blob.Length / 2),
            new byte[ChuckFormat.ChunkSize],
            new byte[ChuckFormat.ChunkSize / 2],
            new byte[ChuckFormat.ChunkSize / 2],
            new byte[ChuckFormat.ChunkSize / 2]));
    }

    [Fact]
    public void A_blob_from_another_version_is_refused()
    {
        var blob = EncodeOf(Generate(1L, 0, 0));
        blob[0] = ChunkBlobCodec.Version + 1;

        Assert.Throws<InvalidDataException>(() => ChunkBlobCodec.Decode(
            blob,
            new byte[ChuckFormat.ChunkSize],
            new byte[ChuckFormat.ChunkSize / 2],
            new byte[ChuckFormat.ChunkSize / 2],
            new byte[ChuckFormat.ChunkSize / 2]));
    }

    [Fact]
    public void An_empty_blob_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => ChunkBlobCodec.Decode(
            [],
            new byte[ChuckFormat.ChunkSize],
            new byte[ChuckFormat.ChunkSize / 2],
            new byte[ChuckFormat.ChunkSize / 2],
            new byte[ChuckFormat.ChunkSize / 2]));
    }

    // ---- the point of the exercise ----

    /// <summary>
    ///     The size claim, and it has to be made <em>after</em> compression or it is not the claim.
    ///     <para>
    ///         Comparing the blob against the 81,920-byte raw form flatters it enormously and means
    ///         nothing: the raw form has never been what goes on the wire. The wire has always been
    ///         zlib over the raw chunk, and zlib already finds most of the redundancy a palette does.
    ///         Measured over 200 chunks of a real save the honest figure is 2,610 bytes per chunk
    ///         today against 1,966 with this — around a quarter, and far less than a palette
    ///         encoding looks like it should buy.
    ///     </para>
    ///     <para>
    ///         Generated terrain here rather than a save, because a test cannot depend on one. It is
    ///         more uniform than a played-in world and therefore compresses better under both
    ///         schemes; the assertion is only that the ordering holds, and the numbers are printed
    ///         rather than pinned so a generator change cannot fail this.
    ///     </para>
    /// </summary>
    [Theory]
    [InlineData(1L, 0, 0)]
    [InlineData(1L, 12, -7)]
    [InlineData(987_654_321L, 100, 100)]
    public void Generated_terrain_costs_less_on_the_wire_than_the_format_it_replaces(
        long seed, int chunkX, int chunkZ)
    {
        var chunk = Generate(seed, chunkX, chunkZ);

        var raw = new byte[RawChunkBytes];
        chunk.ToPacket(raw, 0, 0, 0, 16, ChuckFormat.ChunkHeight, 16, 0);

        var today = Deflated(raw);
        var replacement = Deflated(EncodeOf(chunk));

        output.WriteLine(
            $"seed {seed} chunk ({chunkX},{chunkZ}): {today} -> {replacement} bytes on the wire "
            + $"({100.0 - 100.0 * replacement / today:F1}% smaller)");

        Assert.True(
            replacement < today,
            $"encoding cost {replacement} bytes against {today} for the format it replaces");
    }

    private static int Deflated(byte[] data)
    {
        MemoryStream output = new();
        using (ZLibStream compressor = new(output, CompressionLevel.Optimal, true))
        {
            compressor.Write(data);
        }

        return (int)output.Length;
    }
}
