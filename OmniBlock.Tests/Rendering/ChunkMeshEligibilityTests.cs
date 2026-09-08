using OmniBlock.Client.Rendering.Chunks;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkMeshEligibilityTests
{
    [Fact]
    public void Loaded_source_chunk_is_renderable_without_neighbor_ring()
    {
        LightTestWorld world = new();
        world.Chunks.Add(0, 0);

        Assert.True(ChunkRenderer.HasRenderableSourceChunk(world, new Vector3D<int>(0, 64, 0)));
    }

    [Fact]
    public void Placeholder_source_chunk_is_not_renderable_before_decode_finishes()
    {
        LightTestWorld world = new();
        var chunk = world.Chunks.Add(0, 0);
        chunk.Loaded = false;

        Assert.False(ChunkRenderer.HasRenderableSourceChunk(world, new Vector3D<int>(0, 64, 0)));
    }
}
