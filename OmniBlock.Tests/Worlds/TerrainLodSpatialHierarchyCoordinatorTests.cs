using System.Collections.Concurrent;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Worlds;

public sealed class TerrainLodSpatialHierarchyCoordinatorTests
{
    private static readonly TerrainLodMaterialCatalog Materials = new(
    [
        new TerrainLodMaterialDefinition(1, "example:stone",
            TerrainLodGeometryClass.Opaque, true, 0x707070),
        new TerrainLodMaterialDefinition(2, "example:water",
            TerrainLodGeometryClass.Liquid, false, 0x4040FF)
    ]);

    [Fact]
    public async Task Candidate_is_not_coverage_until_the_coordinator_publishes_it()
    {
        using var coordinator = Coordinator(MaximumLevelOnePolicy());
        var parent = new TerrainLodTileKey(1, -2, 3);

        foreach (var leaf in Children(parent, 1, 1)) coordinator.PublishLeaf(leaf);
        await WaitUntil(() => coordinator.Snapshot().Construction.Ready == 1);

        Assert.False(coordinator.TryGetCoverage(parent, out _, out _));
        List<TerrainLodColumnTile> publications = [];
        Assert.Equal(1, coordinator.DrainCompleted(1, publications));
        Assert.Equal(parent, Assert.Single(publications).Key);
        Assert.True(coordinator.TryGetCoverage(parent, out var tile, out var current));
        Assert.True(current);
        Assert.Equal(parent, tile!.Key);
    }

    [Fact]
    public async Task Leaf_edits_retain_old_parent_as_fallback_until_atomic_replacement()
    {
        using ManualResetEventSlim releaseReplacement = new();
        var replacementEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        var construction = new TerrainLodParentConstructionService(8, 4, input =>
        {
            if (Interlocked.Increment(ref attempts) == 2)
            {
                replacementEntered.TrySetResult();
                releaseReplacement.Wait(TimeSpan.FromSeconds(5));
            }
            return TerrainLodColumnTile.BuildParent(
                input.Key, input.Children, input.HorizontalSampleLevel);
        });
        using var coordinator = new TerrainLodSpatialHierarchyCoordinator(
            MaximumLevelOnePolicy(), construction, null, 32, ownsDependencies: true);
        var parent = new TerrainLodTileKey(1, 0, 0);
        var originalLeaves = Children(parent, 1, 4);
        foreach (var leaf in originalLeaves) coordinator.PublishLeaf(leaf);
        await WaitUntil(() => coordinator.DrainCompleted(1) == 1);
        Assert.True(coordinator.TryGetCoverage(parent, out var original, out var originalCurrent));
        Assert.True(originalCurrent);

        coordinator.PublishLeaf(Leaf(parent.Child(0), block: 2, revision: 5));
        await replacementEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(coordinator.TryGetCoverage(parent, out var fallback, out var fallbackCurrent));
        Assert.False(fallbackCurrent);
        Assert.Equal(original!.CanonicalHash, fallback!.CanonicalHash);
        releaseReplacement.Set();
        await WaitUntil(() => coordinator.DrainCompleted(1) == 1);

        Assert.True(coordinator.TryGetCoverage(parent, out var replacement, out var current));
        Assert.True(current);
        Assert.NotEqual(original.CanonicalHash, replacement!.CanonicalHash);
        Assert.Equal(0, coordinator.Snapshot().FallbackTiles);
    }

    [Fact]
    public async Task Published_parents_recursively_wake_the_next_complete_ancestor()
    {
        using var coordinator = Coordinator(MaximumLevelTwoPolicy(), tileCapacity: 32);
        var root = new TerrainLodTileKey(2, -1, 1);
        for (var child = 0; child < 4; child++)
        foreach (var leaf in Leaves(root.Child(child), block: (byte)(child % 2 + 1), revision: 9))
            coordinator.PublishLeaf(leaf);

        await PumpUntil(coordinator, () => coordinator.IsCurrent(root));

        Assert.True(coordinator.TryGetCoverage(root, out var rootTile, out var current));
        Assert.True(current);
        Assert.Equal(2, rootTile!.HorizontalSampleLevel);
        for (var child = 0; child < 4; child++)
        {
            Assert.True(coordinator.TryGetCoverage(
                root.Child(child), out var childTile, out var childCurrent));
            Assert.True(childCurrent);
            Assert.Equal(childTile!.CanonicalHash, rootTile.InputHashes[child]);
        }
        var snapshot = coordinator.Snapshot();
        Assert.Equal(5, snapshot.ParentPublications);
        Assert.Equal(0, snapshot.FallbackTiles);
    }

    [Fact]
    public void Cached_parent_without_current_children_is_fallback_only()
    {
        using var coordinator = Coordinator(MaximumLevelOnePolicy());
        var parent = new TerrainLodTileKey(1, 2, -3);
        var cached = TerrainLodColumnTile.BuildParent(parent, Children(parent, 1, 3), 1);

        Assert.Equal(TerrainLodTilePublicationResult.Added,
            coordinator.PublishCached(cached));

        Assert.True(coordinator.TryGetCoverage(parent, out var coverage, out var current));
        Assert.Same(cached, coverage);
        Assert.False(current);
        Assert.Equal(1, coordinator.Snapshot().FallbackTiles);
        Assert.Equal(0, coordinator.Snapshot().Construction.Owned);
    }

    [Fact]
    public void Matching_cached_parent_is_promoted_without_reconstruction()
    {
        using var coordinator = Coordinator(MaximumLevelOnePolicy());
        var parent = new TerrainLodTileKey(1, -4, 5);
        var children = Children(parent, 1, 12);
        var cached = TerrainLodColumnTile.BuildParent(parent, children, 1);
        coordinator.PublishCached(cached);

        // These instances may have originated in cache, but the caller has validated them against
        // the current authoritative source before publishing them as current leaves.
        foreach (var child in children) coordinator.PublishLeaf(child);

        Assert.True(coordinator.TryGetCoverage(parent, out var promoted, out var current));
        Assert.True(current);
        Assert.Same(cached, promoted);
        var snapshot = coordinator.Snapshot();
        Assert.Equal(1, snapshot.CachedParentPromotions);
        Assert.Equal(0, snapshot.ParentPublications);
        Assert.Equal(0, snapshot.Construction.Owned);
    }

    [Fact]
    public void Unvalidated_cached_leaf_is_fallback_and_cannot_seed_a_parent()
    {
        using var coordinator = Coordinator(MaximumLevelOnePolicy());
        var leaf = Leaf(new TerrainLodTileKey(0, 2, 3), 1, 7);

        coordinator.PublishCached(leaf);

        Assert.True(coordinator.TryGetCoverage(leaf.Key, out var fallback, out var current));
        Assert.Same(leaf, fallback);
        Assert.False(current);
        Assert.Equal(0, coordinator.Snapshot().Construction.Owned);
    }

    [Fact]
    public void Tile_capacity_rejects_new_leaves_without_mutating_existing_coverage()
    {
        using var coordinator = Coordinator(MaximumLevelOnePolicy(), tileCapacity: 2);
        var first = Leaf(new TerrainLodTileKey(0, 0, 0), 1, 1);
        var second = Leaf(new TerrainLodTileKey(0, 1, 0), 1, 1);
        var rejected = Leaf(new TerrainLodTileKey(0, 2, 0), 1, 1);

        Assert.Equal(TerrainLodTilePublicationResult.Added,
            coordinator.PublishLeaf(first));
        Assert.Equal(TerrainLodTilePublicationResult.Added,
            coordinator.PublishLeaf(second));
        Assert.Equal(TerrainLodTilePublicationResult.RejectedAtCapacity,
            coordinator.PublishLeaf(rejected));

        Assert.True(coordinator.TryGetCoverage(first.Key, out var retained, out var current));
        Assert.Same(first, retained);
        Assert.True(current);
        Assert.False(coordinator.TryGetCoverage(rejected.Key, out _, out _));
        Assert.Equal(1, coordinator.Snapshot().TileCapacityRejections);
    }

    [Fact]
    public async Task Evicted_parent_is_rebuilt_when_its_current_children_remain_resident()
    {
        using var coordinator = Coordinator(MaximumLevelOnePolicy());
        var parent = new TerrainLodTileKey(1, 4, 2);
        foreach (var leaf in Children(parent, 1, 1)) coordinator.PublishLeaf(leaf);
        await PumpUntil(coordinator, () => coordinator.IsCurrent(parent));

        Assert.True(coordinator.Evict(parent));
        Assert.False(coordinator.TryGetCoverage(parent, out _, out _));
        await PumpUntil(coordinator, () => coordinator.IsCurrent(parent));

        Assert.True(coordinator.TryGetCoverage(parent, out _, out var current));
        Assert.True(current);
        Assert.Equal(1, coordinator.Snapshot().Evictions);
    }

    [Fact]
    public async Task Accepted_parent_is_persisted_through_the_bounded_async_writer()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var store = new TerrainLodColumnTileCacheStore(
                directory, Identity(), 16 * 1024 * 1024, 8 * 1024 * 1024);
            using var coordinator = new TerrainLodSpatialHierarchyCoordinator(
                MaximumLevelOnePolicy(), store, tileCapacity: 32,
                constructionCapacity: 8, completedCapacity: 4, persistenceCapacity: 8);
            var parent = new TerrainLodTileKey(1, -1, -1);
            foreach (var leaf in Children(parent, 1, 17)) coordinator.PublishLeaf(leaf);

            await PumpUntil(coordinator, () => coordinator.IsCurrent(parent));
            await WaitUntil(() => coordinator.Snapshot().Persistence!.Written >= 5);

            var cached = store.Read(parent);
            Assert.Equal(TerrainLodColumnTileCacheReadStatus.Hit, cached.Status);
            Assert.True(coordinator.TryGetCoverage(parent, out var tile, out _));
            Assert.Equal(tile!.CanonicalHash, cached.Tile!.CanonicalHash);
        }
        finally
        {
            Directory.Delete(directory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Async_tile_writer_coalesces_latest_key_and_bounds_pending_work()
    {
        using ManualResetEventSlim releaseFirst = new();
        var firstEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        ConcurrentQueue<TerrainLodColumnTile> written = new();
        var attempts = 0;
        using var writer = new TerrainLodColumnTileAsyncCacheWriter(1, tile =>
        {
            written.Enqueue(tile);
            if (Interlocked.Increment(ref attempts) == 1)
            {
                firstEntered.TrySetResult();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }
            return TerrainLodColumnTileCacheWriteStatus.Written;
        });
        var first = Leaf(new TerrainLodTileKey(0, 0, 0), 1, 1);
        var pendingKey = new TerrainLodTileKey(0, 1, 0);
        var pendingOld = Leaf(pendingKey, 1, 1);
        var pendingNew = Leaf(pendingKey, 2, 2);
        var rejected = Leaf(new TerrainLodTileKey(0, 2, 0), 1, 1);

        Assert.True(writer.TrySubmit(first));
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(writer.TrySubmit(pendingOld));
        Assert.True(writer.TrySubmit(pendingNew));
        Assert.False(writer.TrySubmit(rejected));
        releaseFirst.Set();
        await WaitUntil(() => writer.Snapshot().Written == 2);

        Assert.Equal([first.CanonicalHash, pendingNew.CanonicalHash],
            written.Select(static tile => tile.CanonicalHash).ToArray());
        Assert.Equal(1, writer.Snapshot().Coalesced);
        Assert.Equal(1, writer.Snapshot().RejectedAtCapacity);
    }

    private static TerrainLodSpatialHierarchyCoordinator Coordinator(
        TerrainLodSpatialPolicy policy,
        int tileCapacity = 64) => new(
        policy,
        cache: null,
        tileCapacity: tileCapacity,
        constructionCapacity: 16,
        completedCapacity: 8);

    private static TerrainLodSpatialPolicy MaximumLevelOnePolicy() => new(
        4,
        2,
        [0, 1],
        [32, 16]);

    private static TerrainLodSpatialPolicy MaximumLevelTwoPolicy() => new(
        4,
        2,
        [0, 1, 2],
        [32, 16, 8]);

    private static TerrainLodColumnTile[] Children(
        TerrainLodTileKey parent,
        byte block,
        long revision) => Enumerable.Range(0, 4)
        .Select(index => Leaf(parent.Child(index), block, revision))
        .ToArray();

    private static IEnumerable<TerrainLodColumnTile> Leaves(
        TerrainLodTileKey tile,
        byte block,
        long revision)
    {
        if (tile.Level == 0)
        {
            yield return Leaf(tile, block, revision);
            yield break;
        }
        for (var child = 0; child < 4; child++)
        foreach (var leaf in Leaves(tile.Child(child), block, revision))
            yield return leaf;
    }

    private static TerrainLodColumnTile Leaf(
        TerrainLodTileKey key,
        byte block,
        long revision)
    {
        if (key.Level != 0) throw new ArgumentException("Expected a leaf key.", nameof(key));
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

    private static TerrainLodCacheIdentity Identity() => new(
        "coordinator-world",
        0,
        "coordinator-content",
        "coordinator-generator",
        TerrainLodHierarchy.ReductionSchemaVersion,
        Materials.RulesFingerprint);

    private static async Task PumpUntil(
        TerrainLodSpatialHierarchyCoordinator coordinator,
        Func<bool> condition)
    {
        await WaitUntil(() =>
        {
            coordinator.DrainCompleted(16);
            return condition();
        });
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

    private static DirectoryInfo CreateTemporaryDirectory()
    {
        var directory = new DirectoryInfo(Path.Combine(
            Path.GetTempPath(), $"omniblock-spatial-coordinator-{Guid.NewGuid():N}"));
        directory.Create();
        return directory;
    }
}
