using OmniBlock.Server.Worlds;

namespace OmniBlock.Tests.Worlds;

public sealed class WorldGenerationCoordinatorTests
{
    [Fact]
    public void Gameplay_always_precedes_background_radial_order()
    {
        Assert.True(GenerationRequestPriority.GameplayAt(1000, 1000)
            .CompareTo(GenerationRequestPriority.BackgroundAt(0)) < 0);
        Assert.True(GenerationRequestPriority.BackgroundAt(3)
            .CompareTo(GenerationRequestPriority.BackgroundAt(4)) < 0);
    }

    [Fact]
    public async Task Same_coordinate_shares_work_and_last_owner_controls_cancellation()
    {
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim release = new();
        var executions = 0;
        using WorldGenerationCoordinator<int> coordinator = new(1, (_, token) =>
        {
            Interlocked.Increment(ref executions);
            started.Set();
            release.Wait(token);
            return 42;
        });
        var key = new GenerationWorkKey("world", 0, 3, -4);
        using var player = coordinator.Request(key, "player", GenerationDesiredStage.Activated,
            GenerationRequestPriority.Gameplay, 7);
        using var job = coordinator.Request(key, "job", GenerationDesiredStage.Saved,
            GenerationRequestPriority.Background, 7);

        Assert.True(started.Wait(TimeSpan.FromSeconds(2)));
        Assert.Same(player.Completion, job.Completion);
        player.Dispose();
        Assert.False(job.Completion.IsCanceled);
        release.Set();
        Assert.Equal(42, await job.Completion);
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task Stage_and_priority_promote_before_bounded_admission()
    {
        using ManualResetEventSlim blockerStarted = new();
        using ManualResetEventSlim release = new();
        GenerationWorkContext observed = default;
        using WorldGenerationCoordinator<int> coordinator = new(1, (context, token) =>
        {
            if (context.Key.ChunkX == 0)
            {
                blockerStarted.Set();
                release.Wait(token);
            }
            else observed = context;
            return context.Key.ChunkX;
        });
        using var blocker = coordinator.Request(new GenerationWorkKey("world", 0, 0, 0), "blocker",
            GenerationDesiredStage.Terrain, GenerationRequestPriority.Gameplay, 1);
        Assert.True(blockerStarted.Wait(TimeSpan.FromSeconds(2)));
        var key = new GenerationWorkKey("world", 0, 1, 0);
        using var background = coordinator.Request(key, "background", GenerationDesiredStage.Terrain,
            GenerationRequestPriority.Background, 1);
        using var gameplay = coordinator.Request(key, "gameplay", GenerationDesiredStage.Saved,
            GenerationRequestPriority.RelocationCritical, 1);

        release.Set();
        Assert.Equal(1, await gameplay.Completion);
        Assert.Equal(GenerationDesiredStage.Saved, observed.DesiredStage);
    }

    [Fact]
    public async Task Newer_revision_cancels_stale_shared_result()
    {
        using ManualResetEventSlim oldStarted = new();
        using ManualResetEventSlim releaseOld = new();
        using WorldGenerationCoordinator<long> coordinator = new(2, (context, token) =>
        {
            if (context.Revision == 1)
            {
                oldStarted.Set();
                releaseOld.Wait(token);
            }
            return context.Revision;
        });
        var key = new GenerationWorkKey("world", -1, 8, 9);
        using var stale = coordinator.Request(key, "old", GenerationDesiredStage.Lit,
            GenerationRequestPriority.Background, 1);
        Assert.True(oldStarted.Wait(TimeSpan.FromSeconds(2)));
        using var current = coordinator.Request(key, "new", GenerationDesiredStage.Lit,
            GenerationRequestPriority.Gameplay, 2);

        Assert.Equal(2, await current.Completion);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stale.Completion);
        releaseOld.Set();
    }

    [Fact]
    public async Task Worker_count_is_a_hard_concurrency_bound()
    {
        using ManualResetEventSlim release = new();
        var running = 0;
        var maximum = 0;
        using WorldGenerationCoordinator<int> coordinator = new(2, (context, token) =>
        {
            var now = Interlocked.Increment(ref running);
            int seen;
            do seen = Volatile.Read(ref maximum);
            while (now > seen && Interlocked.CompareExchange(ref maximum, now, seen) != seen);
            release.Wait(token);
            Interlocked.Decrement(ref running);
            return context.Key.ChunkX;
        });
        var requests = Enumerable.Range(0, 6)
            .Select(x => coordinator.Request(new GenerationWorkKey("world", 0, x, 0), $"owner-{x}",
                GenerationDesiredStage.Terrain, GenerationRequestPriority.Background, 1))
            .ToArray();
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref running) == 2, TimeSpan.FromSeconds(2)));
        Assert.Equal(2, coordinator.Snapshot().Running);
        release.Set();
        await Task.WhenAll(requests.Select(static request => request.Completion));
        Assert.Equal(2, maximum);
        foreach (var request in requests) request.Dispose();
    }

    [Fact]
    public async Task Owner_can_reprioritize_queued_work_without_creating_a_second_request()
    {
        using ManualResetEventSlim blockerStarted = new();
        using ManualResetEventSlim release = new();
        var executionOrder = new List<int>();
        using WorldGenerationCoordinator<int> coordinator = new(1, (context, token) =>
        {
            if (context.Key.ChunkX == 0)
            {
                blockerStarted.Set();
                release.Wait(token);
            }
            lock (executionOrder) executionOrder.Add(context.Key.ChunkX);
            return context.Key.ChunkX;
        });
        using var blocker = coordinator.Request(new GenerationWorkKey("world", 0, 0, 0), "blocker",
            GenerationDesiredStage.Terrain, GenerationRequestPriority.Gameplay, 1);
        Assert.True(blockerStarted.Wait(TimeSpan.FromSeconds(2)));
        using var first = coordinator.Request(new GenerationWorkKey("world", 0, 1, 0), "first",
            GenerationDesiredStage.Activated, GenerationRequestPriority.GameplayAt(8, 0), 1);
        using var second = coordinator.Request(new GenerationWorkKey("world", 0, 2, 0), "second",
            GenerationDesiredStage.Activated, GenerationRequestPriority.GameplayAt(4, 0), 1);

        first.UpdateDemand(GenerationDesiredStage.Activated,
            GenerationRequestPriority.RelocationCritical);
        release.Set();

        await Task.WhenAll(first.Completion, second.Completion);
        Assert.Equal([0, 1, 2], executionOrder);
    }
}
