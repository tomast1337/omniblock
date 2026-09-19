using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodSpatialForestSelectorTests
{
    private static readonly TerrainLodSpatialPolicy Policy = new(
        distanceUnitChunks: 8,
        distanceGrowth: 2,
        horizontalSampleLevelBySpatialLevel: [0, 0, 1, 1, 2],
        verticalSliceBudgetBySpatialLevel: [32, 24, 16, 12, 8]);

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
