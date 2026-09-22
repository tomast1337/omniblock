using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodSpatialForestSelectorTests
{
    private static readonly TerrainLodSpatialPolicy Policy = new(
        distanceUnitChunks: 8,
        distanceGrowth: 2,
        horizontalSampleLevelBySpatialLevel: [0, 0, 1, 1, 2],
        verticalSliceBudgetBySpatialLevel: [32, 24, 16, 12, 8]);

    [Theory]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(4096)]
    public void Sparse_large_domains_probe_residency_not_their_theoretical_area(int horizon)
    {
        var policy = TerrainLodSpatialPolicy.CreateForMaximumHorizon(horizon);
        HashSet<TerrainLodTileKey> ready = [new(2, 0, 0), new(2, -1, -1)];
        var probes = 0;
        var forest = TerrainLodSpatialForestSelector.Select(
            ready, 2, 0, 0, horizon, policy, key =>
            {
                probes++;
                return ready.Contains(key);
            });

        Assert.Equal(ready.OrderBy(key => key.X),
            forest.Roots.SelectMany(root => root.Nodes).Select(node => node.Tile).OrderBy(key => key.X));
        Assert.All(forest.Roots, root => Assert.False(root.CompleteCoverage));
        Assert.InRange(probes, 1, 256);
    }

    [Fact]
    public void Occupancy_pruning_preserves_full_scan_partitions()
    {
        var managementRoot = new TerrainLodTileKey(4, 0, 0);
        HashSet<TerrainLodTileKey> candidates = [managementRoot];
        AddDescendants(managementRoot, 0, candidates);
        var random = new Random(1024);
        for (var sample = 0; sample < 100; sample++)
        {
            var ready = candidates.Where(_ => random.Next(4) == 0).ToHashSet();
            var cameraX = random.Next(-64, 65);
            var cameraZ = random.Next(-64, 65);
            List<TerrainLodTileSelection> expected = [];
            var complete = FullScan(managementRoot);
            var actual = TerrainLodSpatialForestSelector.Select(
                ready, 2, cameraX, cameraZ, 256, Policy, ready.Contains);
            Assert.Equal(expected, actual.Roots.SelectMany(root => root.Nodes));
            if (expected.Count > 0) Assert.Equal(complete, Assert.Single(actual.Roots).CompleteCoverage);

            bool FullScan(TerrainLodTileKey root)
            {
                var selection = TerrainLodSpatialSelector.Select(root, cameraX, cameraZ, Policy, ready.Contains);
                if (selection.CompleteCoverage)
                {
                    expected.AddRange(selection.Nodes);
                    return true;
                }
                if (root.Level == 2) return false;
                var all = true;
                for (var i = 0; i < 4; i++) all &= FullScan(root.Child(i));
                return all;
            }
        }
    }

    [Fact]
    public void Incomplete_management_root_exposes_available_level_two_descendants()
    {
        var first = new TerrainLodTileKey(2, 0, 0);
        var second = new TerrainLodTileKey(2, 1, 0);
        HashSet<TerrainLodTileKey> ready = [first, second];

        var forest = TerrainLodSpatialForestSelector.Select(
            ready, minimumVisibleLevel: 2,
            cameraChunkX: 0, cameraChunkZ: 0,
            maximumDistanceChunks: 64,
            Policy, ready.Contains);

        var root = Assert.Single(forest.Roots);
        Assert.False(root.CompleteCoverage);
        Assert.Equal(new TerrainLodTileKey(4, 0, 0), root.ManagementRoot);
        Assert.Equal([first, second], root.Nodes.Select(static node => node.Tile));
    }

    [Fact]
    public void Ready_distant_parent_atomically_replaces_its_descendants()
    {
        var managementRoot = new TerrainLodTileKey(4, 0, 0);
        var parent = managementRoot.Child(0); // level 3, chunks 0..7
        HashSet<TerrainLodTileKey> ready = [parent];
        for (var index = 0; index < 4; index++) ready.Add(parent.Child(index));

        var forest = TerrainLodSpatialForestSelector.Select(
            ready, minimumVisibleLevel: 2,
            cameraChunkX: -64, cameraChunkZ: -64,
            maximumDistanceChunks: 256,
            Policy, ready.Contains);

        var root = Assert.Single(forest.Roots);
        Assert.False(root.CompleteCoverage);
        Assert.Equal(parent, Assert.Single(root.Nodes).Tile);
    }

    [Fact]
    public void Complete_management_root_selects_non_overlapping_mixed_levels()
    {
        var managementRoot = new TerrainLodTileKey(4, 0, 0);
        HashSet<TerrainLodTileKey> ready = [managementRoot];
        AddDescendants(managementRoot, minimumLevel: 2, ready);

        var forest = TerrainLodSpatialForestSelector.Select(
            ready, minimumVisibleLevel: 2,
            cameraChunkX: -63, cameraChunkZ: 4,
            maximumDistanceChunks: 256,
            Policy, ready.Contains);

        var root = Assert.Single(forest.Roots);
        Assert.True(root.CompleteCoverage);
        Assert.NotEmpty(root.Nodes);
        for (var first = 0; first < root.Nodes.Count; first++)
        for (var second = first + 1; second < root.Nodes.Count; second++)
            Assert.False(Overlaps(root.Nodes[first].Tile, root.Nodes[second].Tile));
        Assert.Contains(root.Nodes, static node => node.Tile.Level == 2);
        Assert.Contains(root.Nodes, static node => node.Tile.Level > 2);
    }

    [Fact]
    public void Separate_management_roots_have_deterministic_distance_order()
    {
        var near = new TerrainLodTileKey(2, 0, 0);
        var far = new TerrainLodTileKey(2, 4, 0);
        HashSet<TerrainLodTileKey> ready = [far, near];

        var forest = TerrainLodSpatialForestSelector.Select(
            ready, minimumVisibleLevel: 2,
            cameraChunkX: 0, cameraChunkZ: 0,
            maximumDistanceChunks: 128,
            Policy, ready.Contains);

        Assert.Equal(2, forest.Roots.Count);
        Assert.Equal(new TerrainLodTileKey(4, 0, 0), forest.Roots[0].ManagementRoot);
        Assert.Equal(new TerrainLodTileKey(4, 1, 0), forest.Roots[1].ManagementRoot);
    }

    private static void AddDescendants(
        TerrainLodTileKey root,
        int minimumLevel,
        ISet<TerrainLodTileKey> destination)
    {
        if (root.Level == minimumLevel) return;
        for (var index = 0; index < 4; index++)
        {
            var child = root.Child(index);
            destination.Add(child);
            AddDescendants(child, minimumLevel, destination);
        }
    }

    private static bool Overlaps(TerrainLodTileKey first, TerrainLodTileKey second) =>
        first.MinChunkX <= second.MaxChunkX && first.MaxChunkX >= second.MinChunkX &&
        first.MinChunkZ <= second.MaxChunkZ && first.MaxChunkZ >= second.MinChunkZ;
}
