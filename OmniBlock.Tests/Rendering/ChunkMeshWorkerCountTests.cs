using OmniBlock.Client.Rendering.Chunks;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkMeshWorkerCountTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 6)]
    [InlineData(16, 8)]
    [InlineData(32, 8)]
    [InlineData(128, 8)]
    public void Worker_count_reserves_capacity_and_is_capped(int processors, int expected)
    {
        Assert.Equal(expected, ChunkRenderer.GetMeshWorkerCount(processors));
    }
}
