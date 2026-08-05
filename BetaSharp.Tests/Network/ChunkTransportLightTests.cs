using BetaSharp.Network.Chunks;
using BetaSharp.Tests.TestSupport;
using BetaSharp.Worlds.Chunks;

namespace BetaSharp.Tests.Network;

/// <summary>
///     That a chunk's light reaches the other side, by both routes a chunk travels.
/// </summary>
/// <remarks>
///     A client holds no light of its own on load: both <see cref="Chunk.LoadFromBlob" /> and
///     <see cref="Chunk.LoadFromPacket" /> call <c>PopulateHeightMapOnly</c>, which does the
///     heightmap and not the light. So whatever the sender put in these bytes is the entire basis
///     for what the receiver can draw, and neither route had a test saying the light survives it.
/// </remarks>
public sealed class ChunkTransportLightTests
{
    /// <summary>The palette encoding, used for a peer that speaks the extended protocol.</summary>
    [Fact]
    public void The_blob_carries_sky_light()
    {
        Chunk lit = Lit();
        Chunk received = Empty();

        byte[] blob = ChunkBlobCodec.Encode(
            lit.Blocks, lit.Meta.Bytes, lit.BlockLight.Bytes, lit.SkyLight.Bytes);

        ChunkBlobCodec.Decode(
            blob, received.Blocks, received.Meta.Bytes, received.BlockLight.Bytes, received.SkyLight.Bytes);

        Assert.Equal(lit.SkyLight.Bytes, received.SkyLight.Bytes);
        Assert.NotEqual(0, received.GetPackedLight(8, 80, 8));
    }

    /// <summary>
    ///     The format Beta 1.7.3 defines, which is what a vanilla peer and the loopback connection
    ///     get — so this is the route a singleplayer world's chunks actually take.
    /// </summary>
    [Fact]
    public void The_legacy_packet_carries_sky_light()
    {
        Chunk lit = Lit();
        Chunk received = Empty();

        // Sized as the sender sizes it: blocks, then a nibble each for meta, block light and sky.
        byte[] bytes = new byte[ChuckFormat.ChunkSize * 5 / 2];
        lit.ToPacket(bytes, 0, 0, 0, 16, ChuckFormat.ChunkHeight, 16, 0);

        received.LoadFromPacket(bytes, 0, 0, 0, 16, ChuckFormat.ChunkHeight, 16, 0);

        Assert.Equal(lit.SkyLight.Bytes, received.SkyLight.Bytes);
        Assert.NotEqual(0, received.GetPackedLight(8, 80, 8));
    }

    /// <summary>A chunk that has been through the first light pass, as a sent one has.</summary>
    private static Chunk Lit()
    {
        LightTestWorld world = new();
        world.Chunks.Add(0, 0);
        world.DrainLighting();

        Chunk chunk = world.BlockHost.GetChunk(0, 0);

        // Without this the comparisons below would hold for two empty arrays.
        Assert.NotEqual(0, chunk.GetPackedLight(8, 80, 8));
        return chunk;
    }

    /// <summary>A chunk holding nothing, as a receiver's is before the bytes land.</summary>
    private static Chunk Empty()
    {
        LightTestWorld world = new();
        world.Chunks.Add(0, 0, populateLight: false);
        return world.BlockHost.GetChunk(0, 0);
    }
}
