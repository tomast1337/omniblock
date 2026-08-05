using BetaSharp.Network.Messages;
using BetaSharp.Tests.TestSupport;
using BetaSharp.Worlds.Chunks;

namespace BetaSharp.Tests.Network;

/// <summary>
///     That light survives the wire, from a lit chunk on one side to a dark one on the other.
/// </summary>
/// <remarks>
///     The section transport is the client's only source of light for a change once the client
///     stops propagating its own. Nothing else was checking that it round-trips through actual
///     serialization rather than through a direct call to <c>ApplyTo</c>, so a message whose fields
///     did not survive <c>Write</c>/<c>Read</c> would look correct in every existing test and
///     deliver nothing in the game.
/// </remarks>
public sealed class LightSectionsRoundTripTests
{
    [Fact]
    public void A_light_section_survives_write_and_read()
    {
        LightTestWorld source = new();
        source.Chunks.Add(0, 0);
        source.DrainLighting();

        Chunk lit = source.BlockHost.GetChunk(0, 0);

        // Sky light exists to be carried; without it the assertions below would hold trivially.
        Assert.True(lit.GetPackedLight(8, 80, 8) != 0, "the source chunk should hold sky light to send");

        uint sections = lit.LightDirtySections;
        Assert.NotEqual(0u, sections);

        LightSectionsMessage sent = LightSectionsMessage.Of(lit, sections);

        using MemoryStream buffer = new();
        sent.Write(buffer);
        buffer.Position = 0;

        LightSectionsMessage received = new();
        received.Read(buffer);

        Assert.Equal(sent.ChunkX, received.ChunkX);
        Assert.Equal(sent.ChunkZ, received.ChunkZ);
        Assert.Equal(sent.Sections, received.Sections);
        Assert.Equal(sent.Compressed, received.Compressed);

        // And that what arrived actually lights a chunk that had nothing.
        LightTestWorld destination = new();
        destination.Chunks.Add(0, 0, populateLight: false);
        Chunk dark = destination.BlockHost.GetChunk(0, 0);
        Assert.Equal(0, dark.GetPackedLight(8, 80, 8));

        received.ApplyTo(dark);

        Assert.Equal(lit.GetPackedLight(8, 80, 8), dark.GetPackedLight(8, 80, 8));
    }
}
