using OmniBlock.Server.Worlds;
using OmniBlock.Util.Maths;

namespace OmniBlock.Tests.Worlds;

public sealed class WorldDecorationCoordinatorTests
{
    [Fact]
    public async Task Canonically_identical_regions_share_one_transaction_across_owners()
    {
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim release = new();
        var executions = 0;
        using WorldDecorationCoordinator coordinator = new((targets, token) =>
        {
            Interlocked.Increment(ref executions);
            started.Set();
            release.Wait(token);
            return new InactiveGenerationBatch([]) { DecoratedTargets = targets };
        });
        using var first = coordinator.Request(
            [new ChunkPos(1, 0), new ChunkPos(0, 0)],
            "job:first",
            GenerationRequestPriority.BackgroundAt(2));
        Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
        using var second = coordinator.Request(
            [new ChunkPos(0, 0), new ChunkPos(1, 0), new ChunkPos(0, 0)],
            "job:second",
            GenerationRequestPriority.BackgroundAt(1));

        Assert.Same(first.Completion, second.Completion);
        release.Set();
        var result = await second.Completion;

        Assert.Equal(1, executions);
        Assert.Equal([new ChunkPos(0, 0), new ChunkPos(1, 0)], result.DecoratedTargets);
    }

    [Fact]
    public async Task Partially_overlapping_regions_never_decorate_concurrently()
    {
        using ManualResetEventSlim firstStarted = new();
        using ManualResetEventSlim releaseFirst = new();
        var running = 0;
        var maximum = 0;
        var order = new List<int>();
        using WorldDecorationCoordinator coordinator = new((targets, token) =>
        {
            var now = Interlocked.Increment(ref running);
            int seen;
            do seen = Volatile.Read(ref maximum);
            while (now > seen && Interlocked.CompareExchange(ref maximum, now, seen) != seen);
            lock (order) order.Add(targets[0].X);
            if (targets[0].X == 0)
            {
                firstStarted.Set();
                releaseFirst.Wait(token);
            }
            Interlocked.Decrement(ref running);
            return new InactiveGenerationBatch([]) { DecoratedTargets = targets };
        });
        using var first = coordinator.Request(
            [new ChunkPos(0, 0), new ChunkPos(1, 0)],
            "job:first",
            GenerationRequestPriority.BackgroundAt(0));
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(2)));
        using var overlapping = coordinator.Request(
            [new ChunkPos(1, 0), new ChunkPos(2, 0)],
            "job:second",
            GenerationRequestPriority.BackgroundAt(0));

        Assert.Equal(new DecorationCoordinatorSnapshot(1, 1, 0, 2), coordinator.Snapshot());
        Assert.False(overlapping.Completion.IsCompleted);
        releaseFirst.Set();
        await Task.WhenAll(first.Completion, overlapping.Completion);

        Assert.Equal(1, maximum);
        Assert.Equal([0, 1], order);
    }

    [Fact]
    public async Task Queued_regions_keep_background_radial_priority()
    {
        using ManualResetEventSlim blockerStarted = new();
        using ManualResetEventSlim releaseBlocker = new();
        var order = new List<int>();
        using WorldDecorationCoordinator coordinator = new((targets, token) =>
        {
            if (targets[0].X == 0)
            {
                blockerStarted.Set();
                releaseBlocker.Wait(token);
            }
            lock (order) order.Add(targets[0].X);
            return new InactiveGenerationBatch([]) { DecoratedTargets = targets };
        });
        using var blocker = coordinator.Request(
            [new ChunkPos(0, 0)], "blocker", GenerationRequestPriority.Gameplay);
        Assert.True(blockerStarted.Wait(TimeSpan.FromSeconds(2)));
        using var far = coordinator.Request(
            [new ChunkPos(10, 0)], "far", GenerationRequestPriority.BackgroundAt(10));
        using var near = coordinator.Request(
            [new ChunkPos(2, 0)], "near", GenerationRequestPriority.BackgroundAt(2));

        releaseBlocker.Set();
        await Task.WhenAll(blocker.Completion, far.Completion, near.Completion);

        Assert.Equal([0, 2, 10], order);
    }

    [Fact]
    public async Task Releasing_the_last_owner_cancels_a_queued_region()
    {
        using ManualResetEventSlim blockerStarted = new();
        using ManualResetEventSlim releaseBlocker = new();
        var executedCanceledRegion = false;
        using WorldDecorationCoordinator coordinator = new((targets, token) =>
        {
            if (targets[0].X == 0)
            {
                blockerStarted.Set();
                releaseBlocker.Wait(token);
            }
            else
            {
                executedCanceledRegion = true;
            }
            return new InactiveGenerationBatch([]) { DecoratedTargets = targets };
        });
        using var blocker = coordinator.Request(
            [new ChunkPos(0, 0)], "blocker", GenerationRequestPriority.Gameplay);
        Assert.True(blockerStarted.Wait(TimeSpan.FromSeconds(2)));
        var canceled = coordinator.Request(
            [new ChunkPos(5, 0)], "job", GenerationRequestPriority.BackgroundAt(5));

        canceled.Dispose();
        releaseBlocker.Set();
        await blocker.Completion;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await canceled.Completion);
        Assert.False(executedCanceledRegion);
        Assert.Equal(0, coordinator.Snapshot().Queued);
    }
}
