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

    [Theory]
    [InlineData(0, 8, 32)]
    [InlineData(24, 8, 8)]
    [InlineData(32, 8, 0)]
    [InlineData(100, 8, 0)]
    public void Foreground_ring_admission_is_bounded(
        int foregroundPending, int workers, int expected) =>
        Assert.Equal(expected, ChunkRenderer.GetMeshForegroundDiscoveryCapacity(foregroundPending, workers));

    [Theory]
    [InlineData(0, 8, 16)]
    [InlineData(9, 8, 7)]
    [InlineData(16, 8, 0)]
    [InlineData(100, 8, 0)]
    public void Streaming_boundary_admission_has_an_independent_bounded_window(
        int streamingPending, int workers, int expected) =>
        Assert.Equal(expected,
            ChunkRenderer.GetStreamingBoundaryAdmissionCapacity(streamingPending, workers));

    [Theory]
    [InlineData(0, 8, 16)]
    [InlineData(11, 8, 5)]
    [InlineData(16, 8, 0)]
    [InlineData(100, 8, 0)]
    public void Leading_edge_has_an_independent_bounded_admission_window(
        int leadingPending, int workers, int expected) =>
        Assert.Equal(expected,
            ChunkRenderer.GetLeadingEdgeAdmissionCapacity(leadingPending, workers));
}
