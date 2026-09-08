using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkVisibilityComputerTests
{
    private static readonly Vector3D<double> View = new(8, 8, 8);

    [Fact]
    public void Empty_section_connects_every_boundary_face()
    {
        LightTestWorld world = new();
        world.Chunks.Add(0, 0);

        var visibility = Compute(world);

        Assert.Equal(ChunkDirectionMask.All, From(visibility, ChunkDirectionMask.West));
    }

    [Fact]
    public void Opaque_wall_separates_its_two_components()
    {
        LightTestWorld world = new();
        var stone = (byte)world.Content.Blocks.Get("stone").Id;
        world.Chunks.Add(0, 0, chunk =>
        {
            for (var y = 0; y < 16; y++)
            for (var z = 0; z < 16; z++)
                chunk.Blocks[ChuckFormat.GetIndex(8, y, z)] = stone;
        });

        var visibility = Compute(world);

        Assert.Equal(ChunkDirectionMask.None, From(visibility, ChunkDirectionMask.West) & ChunkDirectionMask.East);
        Assert.Equal(ChunkDirectionMask.None, From(visibility, ChunkDirectionMask.East) & ChunkDirectionMask.West);
        Assert.NotEqual(ChunkDirectionMask.None, From(visibility, ChunkDirectionMask.West) & ChunkDirectionMask.North);
    }

    [Fact]
    public void Fully_opaque_section_connects_no_faces()
    {
        LightTestWorld world = new();
        var stone = (byte)world.Content.Blocks.Get("stone").Id;
        world.Chunks.Add(0, 0, chunk => Array.Fill(chunk.Blocks, stone));

        Assert.Equal(ChunkDirectionMask.None, From(Compute(world), ChunkDirectionMask.All));
    }

    private static ChunkVisibilityStore Compute(LightTestWorld world)
    {
        using WorldRegionSnapshot snapshot = new(world, 0, 0, 0, 15, 15, 15);
        return ChunkVisibilityComputer.Compute(snapshot, 0, 0, 0);
    }

    private static ChunkDirectionMask From(ChunkVisibilityStore visibility, ChunkDirectionMask incoming) =>
        visibility.GetVisibleFrom(incoming, View, new SubChunkRenderer(new Vector3D<int>(0, 0, 0)));
}
