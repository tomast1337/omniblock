using OmniBlock.Network.Chunks;
using OmniBlock.Network.Messages;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Tests.Worlds;

/// <summary>
///     Whether the pass that first lights a chunk can be observed by anything downstream of it.
///     <para>
///         <see cref="Chunk.PopulateHeightMap" /> establishes sky light by writing the nibble array
///         directly. Only <see cref="LightingEngine.SetLight" /> raises
///         <c>OnLightUpdated</c>, and that is the sole thing a block update — and so a send —
///         is built from. Everything this pass writes is therefore invisible: correct on the
///         server, and unannounceable.
///     </para>
///     <para>
///         That is survivable only while every reader is guaranteed to look after the pass has run.
///         A client is not: its copy is encoded from the chunk at the moment the send happens, and
///         a client that was sent a chunk before it was lit holds zeros with no mechanism able to
///         correct them — it does not recompute light itself, and nothing will ever mention it.
///     </para>
/// </summary>
public sealed class SilentSkyFillTests
{
    /// <summary>
    ///     The pass on its own. Sky light appears, and the chunk says so, which is the property
    ///     every other guarantee in this file is built out of.
    /// </summary>
    [Fact]
    public void The_first_sky_fill_records_what_it_wrote()
    {
        LightTestWorld world = new();
        var chunk = world.Chunks.Add(0, 0);
        world.DrainLighting();

        Assert.Equal(15, world.SkyLightAt(8, 100, 8));
        Assert.NotEqual(0u, chunk.LightDirtySections);
    }

    /// <summary>
    ///     Taking the dirty sections clears them, so one change is claimed once and a sender does
    ///     not resend the same section every tick for as long as the chunk is loaded.
    /// </summary>
    [Fact]
    public void Taking_the_dirty_sections_clears_them()
    {
        LightTestWorld world = new();
        var chunk = world.Chunks.Add(0, 0);
        world.DrainLighting();

        Assert.NotEqual(0u, chunk.TakeLightDirtySections());
        Assert.Equal(0u, chunk.LightDirtySections);
        Assert.Equal(0u, chunk.TakeLightDirtySections());
    }

    /// <summary>
    ///     A section survives being copied out and back. Worth its own test because a section is
    ///     not a contiguous run: the index is <c>(x &lt;&lt; 11) | (z &lt;&lt; 7) | y</c>, so a
    ///     slice of the column is 256 short runs rather than one block of bytes, and an off-by-one
    ///     in that striding would move light between columns rather than lose it — which reads as
    ///     a lighting bug anywhere except here.
    /// </summary>
    [Fact]
    public void A_light_section_survives_a_copy_out_and_back()
    {
        LightTestWorld source = new();
        LightTestWorld target = new();

        var from = source.Chunks.Add(0, 0);
        var to = target.Chunks.Add(0, 0, populateLight: false);

        // Distinct per cell, so a run landing in the wrong column is a failure rather than a
        // coincidence that happens to match.
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var y = 32; y < 48; y++)
                {
                    from.SetLight(LightType.Sky, x, y, z, (x + y) & 15);
                    from.SetLight(LightType.Block, x, y, z, (z + y) & 15);
                }
            }
        }

        var payload = new byte[Chunk.LightSectionPayloadBytes];
        from.CopyLightSection(2, payload);
        to.ApplyLightSection(2, payload);

        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                for (var y = 32; y < 48; y++)
                {
                    Assert.Equal(from.GetLight(LightType.Sky, x, y, z), to.GetLight(LightType.Sky, x, y, z));
                    Assert.Equal(from.GetLight(LightType.Block, x, y, z), to.GetLight(LightType.Block, x, y, z));
                }
            }
        }
    }

    /// <summary>Nothing outside the named section is touched by applying one.</summary>
    [Fact]
    public void Applying_a_light_section_leaves_the_others_alone()
    {
        LightTestWorld world = new();
        var chunk = world.Chunks.Add(0, 0);

        chunk.SetLight(LightType.Sky, 3, 20, 5, 7);
        chunk.SetLight(LightType.Sky, 3, 60, 5, 9);

        chunk.ApplyLightSection(2, new byte[Chunk.LightSectionPayloadBytes]);

        Assert.Equal(0, chunk.GetLight(LightType.Sky, 3, 40, 5));
        Assert.Equal(7, chunk.GetLight(LightType.Sky, 3, 20, 5));
        Assert.Equal(9, chunk.GetLight(LightType.Sky, 3, 60, 5));
    }

    /// <summary>
    ///     The consequence, end to end: a client whose copy was taken during the window between the
    ///     chunk arriving and the chunk being lit. Everything the server later announces is applied,
    ///     and the two are compared.
    /// </summary>
    [Fact]
    public void A_client_sent_a_chunk_before_it_was_lit_ends_up_with_the_servers_light()
    {
        LightTestWorld server = new();
        LightTestWorld client = new();

        // Present and unlit, which is what the chunk is for the whole window this test is about.
        var serverChunk = server.Chunks.Add(0, 0, populateLight: false);

        // The client's copy, encoded from the chunk exactly as it stands — which is how
        // ServerPlayerEntity.SendChunkData builds it, live at send time rather than from a snapshot.
        var clientChunk = client.Chunks.Add(0, 0, populateLight: false);
        clientChunk.LoadFromBlob(ChunkBlobCodec.Encode(
            serverChunk.Blocks, serverChunk.Meta.Bytes,
            serverChunk.BlockLight.Bytes, serverChunk.SkyLight.Bytes));

        Assert.Equal(0, client.SkyLightAt(8, 100, 8));

        // The server catching up after the send, which is the whole of what this test is about.
        serverChunk.PopulateHeightMap();
        serverChunk.PopulateBlockLight();
        server.DrainLighting();

        Assert.Equal(15, server.SkyLightAt(8, 100, 8));

        Replay(serverChunk, client);

        Assert.Equal(server.SkyLightAt(8, 100, 8), client.SkyLightAt(8, 100, 8));
    }

    /// <summary>
    ///     Sends the chunk's dirty light sections, through the wire form rather than around it, so
    ///     the test fails if the message cannot carry what the receiver needs.
    /// </summary>
    private static void Replay(Chunk serverChunk, LightTestWorld client)
    {
        var sections = serverChunk.TakeLightDirtySections();
        Assert.NotEqual(0u, sections);

        var sent = LightSectionsMessage.Of(serverChunk, sections);

        using MemoryStream buffer = new();
        sent.Write(buffer);

        Assert.Equal(buffer.Length, sent.Size());

        buffer.Position = 0;
        LightSectionsMessage received = new();
        received.Read(buffer);

        Assert.Equal(buffer.Length, buffer.Position);

        received.ApplyTo(client.BlockHost.GetChunk(received.ChunkX, received.ChunkZ));
        client.DrainLighting();
    }
}
