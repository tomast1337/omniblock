using OmniBlock.Client.Rendering.Chunks;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkMeshWorkerCountTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(8, 3)]
    [InlineData(16, 7)]
    [InlineData(32, 8)]
    [InlineData(128, 8)]
    public void Worker_count_reserves_capacity_and_is_capped(int processors, int expected) => Assert.Equal(expected, ChunkRenderer.GetMeshWorkerCount(processors));


    [Theory]
    [InlineData(0, 8, 64)]
    [InlineData(60, 8, 4)]
    [InlineData(64, 8, 0)]
    [InlineData(1000, 8, 0)]
    [InlineData(7, 1, 1)]
    public void Discovery_stops_when_the_consumer_backlog_is_full(
        int pending, int workers, int expected) =>
        Assert.Equal(expected, ChunkRenderer.GetMeshDiscoveryCapacity(pending, workers));

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(1, 1, 1)]
    [InlineData(2, 1, 0)]
    [InlineData(12, 8, 4)]
    [InlineData(16, 8, 0)]
    public void Safety_discovery_keeps_a_small_reserved_foreground_window(
        int foregroundPending, int workers, int expected) =>
        Assert.Equal(expected, ChunkRenderer.GetMeshSafetyDiscoveryCapacity(foregroundPending, workers));
}
