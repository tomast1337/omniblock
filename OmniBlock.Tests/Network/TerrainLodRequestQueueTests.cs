using OmniBlock.Server;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Network;

public sealed class TerrainLodRequestQueueTests
{
    [Fact]
    public void Queue_is_fifo_and_deduplicates_retries()
    {
        TerrainLodRequestQueue queue = new();
        var first = Request(2, 1, 2);
        var second = Request(3, 4, 5);

        Assert.Equal(TerrainLodRequestEnqueueResult.Added, queue.Enqueue(first));
        Assert.Equal(TerrainLodRequestEnqueueResult.Duplicate, queue.Enqueue(first));
        Assert.Equal(TerrainLodRequestEnqueueResult.Added, queue.Enqueue(second));
        Assert.Equal(2, queue.Count);

        Assert.True(queue.TryDequeue(out var dequeuedFirst));
        Assert.Equal(first, dequeuedFirst);
        Assert.True(queue.TryDequeue(out var dequeuedSecond));
        Assert.Equal(second, dequeuedSecond);
    }

    [Fact]
    public void Queue_has_a_hard_per_client_bound()
    {
        TerrainLodRequestQueue queue = new();
        for (var i = 0; i < TerrainLodRequestQueue.Capacity; i++)
            Assert.Equal(
                TerrainLodRequestEnqueueResult.Added,
                queue.Enqueue(Request(2, i, 0)));

        Assert.Equal(TerrainLodRequestQueue.Capacity, queue.Count);
        Assert.Equal(
            TerrainLodRequestEnqueueResult.Full,
            queue.Enqueue(Request(2, TerrainLodRequestQueue.Capacity, 0)));
    }

    private static QueuedTerrainLodRequest Request(int level, int x, int z) =>
        new(0, "identity", 6, TerrainLodSpatialPolicy.CurrentQualityPolicyVersion,
            new TerrainLodTileKey(level, x, z));
}
