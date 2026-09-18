using System.Collections.Concurrent;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Worlds;

public sealed class TerrainLodParentConstructionServiceTests
{
    private static readonly TerrainLodMaterialCatalog Materials = new(
    [
        new TerrainLodMaterialDefinition(1, "example:stone",
            TerrainLodGeometryClass.Opaque, true, 0x707070),
        new TerrainLodMaterialDefinition(2, "example:water",
            TerrainLodGeometryClass.Liquid, false, 0x4040FF)
    ]);

    [Fact]
    public async Task New_child_identity_during_build_discards_stale_parent_and_rebuilds()
    {
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using ManualResetEventSlim release = new();
        var attempts = 0;
        using var service = Service(4, 2, input =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                entered.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(5));
            }
            return Build(input);
        });
        var key = new TerrainLodTileKey(1, -2, 4);
        var oldChildren = Children(key, block: 1, revision: 7);
        var newChildren = Children(key, block: 2, revision: 8);

        Assert.Equal(TerrainLodParentAdmissionResult.Accepted,
            service.Submit(key, oldChildren, 1,
                TerrainLodParentWorkKind.Coverage, distanceChunks: 8));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TerrainLodParentAdmissionResult.Coalesced,
            service.Submit(key, newChildren, 1,
                TerrainLodParentWorkKind.Coverage, distanceChunks: 8));
        release.Set();

        var result = await Take(service);
        var expected = TerrainLodColumnTile.BuildParent(key, newChildren, 1);
        Assert.Equal(expected.CanonicalHash, result.Tile.CanonicalHash);
        Assert.Equal(2, attempts);
        Assert.Equal(1, service.Snapshot().StaleConstructionsDiscarded);
        Assert.Equal(1, service.Snapshot().CompletedConstructions);
    }

    [Fact]
    public async Task Scheduler_builds_coarser_tiles_then_nearer_tiles_then_finer_tiles()
    {
        using ManualResetEventSlim releaseFirst = new();
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentQueue<TerrainLodTileKey> order = new();
        var attempts = 0;
        using var service = Service(8, 8, input =>
        {
            order.Enqueue(input.Key);
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstEntered.TrySetResult();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }
            return Build(input);
        });
        var blocker = new TerrainLodTileKey(1, 20, 20);
        var fine = new TerrainLodTileKey(1, 0, 0);
        var coarseFar = new TerrainLodTileKey(3, 3, 3);
        var coarseNear = new TerrainLodTileKey(3, -1, -1);

        Submit(service, blocker, distance: 0);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Submit(service, fine, distance: 1);
        Submit(service, coarseFar, distance: 50);
        Submit(service, coarseNear, distance: 5);
        releaseFirst.Set();
        await WaitUntil(() => service.Snapshot().Ready == 4);

        Assert.Equal(
            [blocker, coarseNear, coarseFar, fine],
            order.ToArray());
    }

    [Fact]
    public async Task Completed_capacity_stops_worker_without_unbounding_queued_inputs()
    {
        using var service = new TerrainLodParentConstructionService(
            capacity: 4, completedCapacity: 1);
        var first = new TerrainLodTileKey(1, 0, 0);
        var second = new TerrainLodTileKey(1, 1, 0);
        var third = new TerrainLodTileKey(1, 2, 0);
        Submit(service, first, 1);
        Submit(service, second, 2);
        Submit(service, third, 3);

        await WaitUntil(() => service.Snapshot().Ready == 1);
        await Task.Delay(20);
        var blocked = service.Snapshot();
        Assert.Equal(1, blocked.Ready);
        Assert.Equal(2, blocked.Queued);
        Assert.Equal(3, blocked.Owned);
        Assert.True(blocked.RetainedInputColumns > 0);
        var level = Assert.Single(blocked.Levels);
        Assert.Equal(2, level.Queued);
        Assert.True(level.OldestCoverageAgeMs > 0);

        Assert.True(service.TryTakeCompleted(out _));
        await WaitUntil(() => service.Snapshot().CompletedConstructions == 2);
        Assert.Equal(1, service.Snapshot().Ready);
    }

    [Fact]
    public async Task Capacity_rejects_new_keys_but_allows_existing_ancestor_to_coalesce()
    {
        using ManualResetEventSlim gate = new();
        var entered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = Service(2, 2, input =>
        {
            entered.TrySetResult();
            gate.Wait(TimeSpan.FromSeconds(5));
            return Build(input);
        });
        var first = new TerrainLodTileKey(1, 0, 0);
        var ancestor = new TerrainLodTileKey(1, 1, 0);
        var rejected = new TerrainLodTileKey(1, 2, 0);

        Submit(service, first, 1);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(TerrainLodParentAdmissionResult.Accepted,
            service.Submit(ancestor, Children(ancestor, 1, 1), 1,
                TerrainLodParentWorkKind.Refinement, 2));
        Assert.Equal(TerrainLodParentAdmissionResult.RejectedAtCapacity,
            service.Submit(rejected, Children(rejected, 1, 1), 1,
                TerrainLodParentWorkKind.Coverage, 3));
        Assert.Equal(TerrainLodParentAdmissionResult.Coalesced,
            service.Submit(ancestor, Children(ancestor, 2, 2), 1,
                TerrainLodParentWorkKind.Refinement, 2));

        var snapshot = service.Snapshot();
        Assert.Equal(2, snapshot.Owned);
        Assert.Equal(1, snapshot.CoalescedSubmissions);
        Assert.Equal(1, snapshot.RejectedCapacitySubmissions);
        gate.Set();
    }

    [Fact]
    public async Task Failure_is_observable_and_retryable_with_per_level_diagnostics()
    {
        var attempts = 0;
        using var service = Service(4, 2, input =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                throw new InvalidDataException("parent fixture failed");
            return Build(input);
        });
        var key = new TerrainLodTileKey(2, -1, 3);
        Submit(service, key, 12, TerrainLodParentWorkKind.Refinement);
        await WaitUntil(() => service.Snapshot().Failed == 1);

        Assert.True(service.TryGetFailure(key, out var error));
        Assert.Equal("parent fixture failed", error);
        var level = Assert.Single(service.Snapshot().Levels);
        Assert.Equal(2, level.SpatialLevel);
        Assert.Equal(1, level.Failed);
        Assert.True(service.Retry(key));

        var result = await Take(service);
        Assert.Equal(key, result.Tile.Key);
        Assert.Equal(1, service.Snapshot().FailedConstructions);
        Assert.Equal(1, service.Snapshot().RetriedConstructions);
    }

    [Fact]
    public async Task Coverage_leads_but_a_refinement_lane_cannot_starve()
    {
        using ManualResetEventSlim releaseFirst = new();
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentQueue<TerrainLodParentWorkKind> order = new();
        var attempts = 0;
        using var service = Service(12, 12, input =>
        {
            order.Enqueue(input.WorkKind);
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstEntered.TrySetResult();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }
            return Build(input);
        });
        Submit(service, new TerrainLodTileKey(1, -20, 0), 1);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        for (var x = 0; x < 8; x++)
            Submit(service, new TerrainLodTileKey(1, x, 0), x + 2);
        Submit(service, new TerrainLodTileKey(1, 30, 0), 100,
            TerrainLodParentWorkKind.Refinement);

        releaseFirst.Set();
        await WaitUntil(() => service.Snapshot().Ready == 10);

        var built = order.ToArray();
        Assert.Equal(10, built.Length);
        Assert.Equal(TerrainLodParentWorkKind.Refinement, built[8]);
        Assert.Equal(1, built.Count(static kind =>
            kind == TerrainLodParentWorkKind.Refinement));
    }

    private static TerrainLodParentConstructionService Service(
        int capacity,
        int completedCapacity,
        Func<TerrainLodParentBuildInput, TerrainLodColumnTile> build) =>
        new(capacity, completedCapacity, build);

    private static TerrainLodColumnTile Build(TerrainLodParentBuildInput input) =>
        TerrainLodColumnTile.BuildParent(
            input.Key, input.Children, input.HorizontalSampleLevel);

    private static void Submit(
        TerrainLodParentConstructionService service,
        TerrainLodTileKey key,
        double distance,
        TerrainLodParentWorkKind workKind = TerrainLodParentWorkKind.Coverage)
    {
        Assert.Equal(TerrainLodParentAdmissionResult.Accepted,
            service.Submit(key, Children(key, 1, 1), key.Level,
                workKind, distance));
    }

    private static TerrainLodColumnTile[] Children(
        TerrainLodTileKey parent,
        byte block,
        long revision) => Enumerable.Range(0, 4)
        .Select(index => Tile(parent.Child(index), block, revision))
        .ToArray();

    private static TerrainLodColumnTile Tile(
        TerrainLodTileKey key,
        byte block,
        long revision)
    {
        if (key.Level == 0)
        {
            const int height = 2;
            var blocks = Enumerable.Repeat(block, 16 * height * 16).ToArray();
            return TerrainLodColumnTile.BuildLeaf(new TerrainLodSourceSnapshot(
                key.X,
                key.Z,
                16,
                height,
                16,
                blocks,
                new byte[blocks.Length],
                revision), Materials);
        }
        return TerrainLodColumnTile.BuildParent(
            key,
            Children(key, block, revision),
            horizontalSampleLevel: key.Level);
    }

    private static async Task<TerrainLodParentConstructionResult> Take(
        TerrainLodParentConstructionService service)
    {
        TerrainLodParentConstructionResult? result = null;
        await WaitUntil(() => service.TryTakeCompleted(out result));
        return result!;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException();
            await Task.Delay(1);
        }
    }
}
