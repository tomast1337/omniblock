using BetaSharp.Blocks;
using BetaSharp.Tests.TestSupport;
using BetaSharp.Worlds.Chunks;

namespace BetaSharp.Tests.Worlds;

/// <summary>
///     Sky light crossing a chunk border.
///     <para>
///         The first light pass fills each column straight down and nothing else;
///         everything horizontal comes from the update queue. The only thing that puts a
///         cross-border update on that queue is <c>Chunk.LightGaps</c>, which compares this
///         chunk's column height against its neighbor's and queues work only where the two
///         differ.
///     </para>
/// </summary>
public sealed class ChunkBorderLightTests
{
    private const int RoofY = 70;

    /// <summary>Fills one chunk with a solid roof, leaving everything under it in shadow.</summary>
    private static void Roofed(Chunk chunk)
    {
        int stone = BlockRegistry.Get("stone").id;

        for (int localX = 0; localX < 16; localX++)
        {
            for (int localZ = 0; localZ < 16; localZ++)
            {
                chunk.Blocks[ChuckFormat.GetIndex(localX, localZ) + RoofY] = (byte)stone;
            }
        }
    }

    /// <summary>
    ///     Light under the roof, one block in from the border, reached horizontally from the open
    ///     chunk next door. Full daylight is 15 and one step of travel costs one level.
    /// </summary>
    private static int UnderTheRoofAtTheBorder(LightTestWorld world) => world.SkyLightAt(16, RoofY - 1, 8);

    /// <summary>
    ///     Every chunk the roofed one needs around it. A light update refuses to touch a cell whose
    ///     chunk does not have all of its own neighbors loaded
    ///     (<c>ChunkHost.IsRegionLoaded(x, 0, z, 1)</c>), so a two-chunk world lights nothing at all
    ///     and would make any assertion here pass or fail for the wrong reason.
    /// </summary>
    private static readonly (int X, int Z)[] s_neighborhood =
    [
        (0, -1), (1, -1), (2, -1),
        (0, 0), (2, 0),
        (0, 1), (1, 1), (2, 1)
    ];

    [Fact]
    public void Light_crosses_the_border_when_the_open_chunks_load_first()
    {
        LightTestWorld world = new();

        foreach ((int x, int z) in s_neighborhood)
        {
            world.Chunks.Add(x, z);
        }

        world.Chunks.Add(1, 0, Roofed);
        world.DrainLighting();

        Assert.Equal(14, UnderTheRoofAtTheBorder(world));
    }

    /// <summary>
    ///     A light source already present in the terrain when the chunk loads, which is how every
    ///     lava pool and glowstone cluster arrives. Nothing about placing it goes through
    ///     <c>Chunk.SetBlock</c>, so nothing queues a block-light update for it.
    /// </summary>
    [Fact]
    public void A_light_source_already_in_the_terrain_lights_the_chunk_it_loads_with()
    {
        int glowstone = BlockRegistry.Get("glowstone").id;
        int luminance = Block.BlocksLightLuminance[glowstone];
        Assert.True(luminance > 0, "test needs an emitting block");

        LightTestWorld world = new();

        foreach ((int x, int z) in s_neighborhood)
        {
            world.Chunks.Add(x, z);
        }

        world.Chunks.Add(1, 0, chunk => chunk.Blocks[ChuckFormat.GetIndex(8, 8) + 40] = (byte)glowstone);
        world.DrainLighting();

        Assert.Equal(luminance, world.Lighting.GetBrightness(LightType.Block, 24, 40, 8));
    }

    /// <summary>
    ///     The same source, but with its chunk arriving before any of its neighbors. A light update
    ///     is skipped outright for a cell whose chunk lacks a full neighbor ring, and it is dropped
    ///     from the queue either way, so this is where a source can end up permanently unlit.
    /// </summary>
    [Fact]
    public void A_light_source_survives_its_chunk_arriving_before_its_neighbors()
    {
        int glowstone = BlockRegistry.Get("glowstone").id;
        LightTestWorld world = new();

        world.Chunks.Add(1, 0, chunk => chunk.Blocks[ChuckFormat.GetIndex(8, 8) + 40] = (byte)glowstone);
        world.DrainLighting();

        foreach ((int x, int z) in s_neighborhood)
        {
            world.Chunks.Add(x, z);
            world.DrainLighting();
        }

        Assert.Equal(Block.BlocksLightLuminance[glowstone], world.Lighting.GetBrightness(LightType.Block, 24, 40, 8));
    }

    /// <summary>
    ///     Chunks arriving over several ticks rather than all within one, which is how they
    ///     actually arrive. Each tick drains the light queue, so work queued while the neighbor
    ///     ring was still incomplete is dequeued, skipped, and gone.
    /// </summary>
    [Fact]
    public void Light_crosses_the_border_when_chunks_arrive_over_several_ticks()
    {
        LightTestWorld world = new();
        world.Chunks.Add(1, 0, Roofed);
        world.DrainLighting();

        foreach ((int x, int z) in s_neighborhood)
        {
            world.Chunks.Add(x, z);
            world.DrainLighting();
        }

        Assert.Equal(14, UnderTheRoofAtTheBorder(world));
    }

    [Fact]
    public void Light_crosses_the_border_when_the_roofed_chunk_loads_first()
    {
        LightTestWorld world = new();
        world.Chunks.Add(1, 0, Roofed);

        foreach ((int x, int z) in s_neighborhood)
        {
            world.Chunks.Add(x, z);
        }

        world.DrainLighting();

        Assert.Equal(14, UnderTheRoofAtTheBorder(world));
    }
}
