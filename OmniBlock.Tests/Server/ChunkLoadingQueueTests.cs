using OmniBlock.Server;

namespace OmniBlock.Tests.Server;

public sealed class ChunkLoadingQueueTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(10, 8)]
    [InlineData(32, 8)]
    [InlineData(128, 8)]
    public void Chunk_loader_concurrency_is_bounded(int processors, int expected)
    {
        Assert.Equal(expected, ChunkLoadingQueue.GetWorkerCount(processors));
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(2, 1, false)]
    [InlineData(8, 3, false)]
    [InlineData(3, int.MaxValue, true)]
    public void Completed_chunks_wait_for_every_closer_ring(
        int completedRing,
        int nearestOutstandingRing,
        bool expected)
    {
        Assert.Equal(expected,
            ChunkLoadingQueue.CanPublishRing(completedRing, nearestOutstandingRing));
    }
}
