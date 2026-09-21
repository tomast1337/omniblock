using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Worlds;

public sealed class TerrainLodSpatialHierarchyTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3, 0, 0)]
    [InlineData(4, 1, 4)]
    [InlineData(-1, -1, -4)]
    [InlineData(-4, -1, -4)]
    [InlineData(-5, -2, -8)]
    public void Tile_coordinates_floor_negative_chunks(
        int chunkX, int expectedTileX, int expectedMinimumChunkX)
    {
        var tile = TerrainLodTileKey.ContainingChunk(2, chunkX, 7);

        Assert.Equal(expectedTileX, tile.X);
        Assert.Equal(expectedMinimumChunkX, tile.MinChunkX);
        Assert.True(tile.ContainsChunk(chunkX, 7));
    }

    [Fact]
    public void Four_children_exactly_partition_the_parent()
    {
        var parent = new TerrainLodTileKey(3, -2, 5);
        var children = Enumerable.Range(0, 4).Select(parent.Child).ToArray();
        var covered = new HashSet<(long X, long Z)>();

        foreach (var child in children)
        {
            Assert.Equal(parent, child.Parent());
            for (var x = child.MinChunkX; x <= child.MaxChunkX; x++)
            for (var z = child.MinChunkZ; z <= child.MaxChunkZ; z++)
                Assert.True(covered.Add((x, z)), $"Chunk {x},{z} was covered twice.");
        }

        Assert.Equal(parent.ChunkWidth * parent.ChunkWidth, covered.Count);
        for (var x = parent.MinChunkX; x <= parent.MaxChunkX; x++)
        for (var z = parent.MinChunkZ; z <= parent.MaxChunkZ; z++)
            Assert.Contains((x, z), covered);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(8, 0)]
    [InlineData(15.99, 0)]
    [InlineData(16, 1)]
    [InlineData(32, 2)]
    [InlineData(64, 3)]
    [InlineData(128, 4)]
    [InlineData(10000, 4)]
    public void Distance_policy_uses_logarithmic_detail_bands(
        double distanceChunks, int expectedLevel)
    {
        var policy = Policy();

        Assert.Equal(expectedLevel, policy.DesiredSpatialLevel(distanceChunks));
    }

    [Fact]
    public void Spatial_and_horizontal_sample_levels_are_independent()
    {
        var policy = Policy();

        Assert.Equal([0, 0, 1, 1, 2], policy.HorizontalSampleLevelBySpatialLevel);
        Assert.Equal(0, policy.HorizontalSampleLevelForSpatialLevel(1));
        Assert.Equal(1, policy.HorizontalSampleLevelForSpatialLevel(3));
        Assert.Equal(2, policy.HorizontalSampleLevelForSpatialLevel(8));
        Assert.Equal([32, 24, 16, 12, 8], policy.VerticalSliceBudgetBySpatialLevel);
        Assert.Equal(24, policy.VerticalSliceBudgetForSpatialLevel(1));
        Assert.Equal(8, policy.VerticalSliceBudgetForSpatialLevel(8));
    }

    [Theory]
    [InlineData(64, 4, 16)]
    [InlineData(128, 5, 32)]
    [InlineData(256, 6, 64)]
    [InlineData(512, 7, 128)]
    [InlineData(1024, 8, 256)]
    [InlineData(2048, 9, 512)]
    [InlineData(4096, 10, 1024)]
    public void Generated_policy_depth_follows_selected_horizon(
        int horizonChunks,
        int expectedMaximumLevel,
        int expectedMaximumFootprintChunks)
    {
        var policy = TerrainLodSpatialPolicy.CreateForMaximumHorizon(horizonChunks);

        Assert.Equal(expectedMaximumLevel, policy.MaximumSpatialLevel);
        Assert.Equal(expectedMaximumLevel, policy.DesiredSpatialLevel(horizonChunks));
        Assert.Equal(expectedMaximumFootprintChunks,
            new TerrainLodTileKey(expectedMaximumLevel, 0, 0).ChunkWidth);
    }

    [Fact]
    public void Generated_policy_preserves_legacy_levels_and_bounds_new_node_quality()
    {
        var policy = TerrainLodSpatialPolicy.CreateDefault();

        Assert.Equal([0, 0, 1, 1, 2, 3, 4],
            policy.HorizontalSampleLevelBySpatialLevel);
        Assert.Equal([32, 24, 16, 12, 8, 6, 4],
            policy.VerticalSliceBudgetBySpatialLevel);
        for (var level = 2; level <= policy.MaximumSpatialLevel; level++)
        {
            var footprintBlocks = new TerrainLodTileKey(level, 0, 0).ChunkWidth * 16;
            var samplesAcross = footprintBlocks /
                                (1 << policy.HorizontalSampleLevelForSpatialLevel(level));
            Assert.InRange(samplesAcross, 1, 64);
        }
    }

    [Fact]
    public void Dormant_large_horizon_policy_keeps_each_aggregate_at_most_sixty_four_samples()
    {
        var policy = TerrainLodSpatialPolicy.CreateForMaximumHorizon(
            TerrainLodSpatialPolicy.MaximumGeneratedHorizonChunks);

        Assert.Equal(TerrainLodSpatialPolicy.MaximumGeneratedSpatialLevel,
            policy.MaximumSpatialLevel);
        Assert.Equal([0, 0, 1, 1, 2, 3, 4, 5, 6, 7, 8],
            policy.HorizontalSampleLevelBySpatialLevel);
        Assert.Equal([32, 24, 16, 12, 8, 6, 4, 4, 4, 4, 4],
            policy.VerticalSliceBudgetBySpatialLevel);
        for (var level = TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel;
             level <= policy.MaximumSpatialLevel;
             level++)
        {
            var footprintBlocks = new TerrainLodTileKey(level, 0, 0).ChunkWidth * 16;
            var samplesAcross = footprintBlocks /
                                (1 << policy.HorizontalSampleLevelForSpatialLevel(level));
            Assert.InRange(samplesAcross, 1, 64);
        }
    }

    [Fact]
    public void Generated_policy_rejects_unsupported_horizons()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TerrainLodSpatialPolicy.CreateForMaximumHorizon(15));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TerrainLodSpatialPolicy.CreateForMaximumHorizon(4097));
    }

    [Fact]
    public void Policy_rejects_levels_that_incremental_parent_builds_cannot_produce()
    {
        Assert.Throws<ArgumentException>(() =>
            new TerrainLodSpatialPolicy(8, 2, [0, 2], [16, 8]));
        Assert.Throws<ArgumentException>(() =>
            new TerrainLodSpatialPolicy(8, 2, [0, 0], [8, 16]));
    }

    [Fact]
    public void Ready_hierarchy_refines_to_complete_non_overlapping_leaves()
    {
        var root = new TerrainLodTileKey(2, 0, 0);
        var selection = TerrainLodSpatialSelector.Select(
            root, 2, 2, Policy(distanceUnit: 1000), static _ => true);

        Assert.True(selection.CompleteCoverage);
        Assert.Equal(16, selection.Nodes.Count);
        Assert.All(selection.Nodes, node =>
        {
            Assert.Equal(0, node.Tile.Level);
            Assert.Equal(0, node.HorizontalSampleLevel);
            Assert.Equal(32, node.MaximumVerticalSlices);
        });
        AssertCompletePartition(root, selection.Nodes);
    }

    [Fact]
    public void Missing_descendant_keeps_its_ready_parent_without_a_hole()
    {
        var root = new TerrainLodTileKey(2, 0, 0);
        var missing = new TerrainLodTileKey(0, 0, 0);
        var selection = TerrainLodSpatialSelector.Select(
            root, 2, 2, Policy(distanceUnit: 1000),
            tile => tile != missing);

        Assert.True(selection.CompleteCoverage);
        Assert.Equal(1, selection.ParentFallbacks);
        Assert.Contains(selection.Nodes, node =>
            node.Tile == new TerrainLodTileKey(1, 0, 0));
        Assert.DoesNotContain(selection.Nodes, node => node.Tile == missing);
        AssertCompletePartition(root, selection.Nodes);
    }

    [Fact]
    public void Partial_children_never_replace_a_ready_root()
    {
        var root = new TerrainLodTileKey(2, -1, -1);
        var readyLeaf = root.Child(0).Child(0);
        var selection = TerrainLodSpatialSelector.Select(
            root, -2, -2, Policy(distanceUnit: 1000),
            tile => tile == root || tile == readyLeaf);

        Assert.True(selection.CompleteCoverage);
        Assert.Single(selection.Nodes);
        Assert.Equal(root, selection.Nodes[0].Tile);
        Assert.Equal(1, selection.ParentFallbacks);
    }

    [Fact]
    public void Missing_parent_and_incomplete_children_report_no_partial_coverage()
    {
        var root = new TerrainLodTileKey(1, 0, 0);
        var selection = TerrainLodSpatialSelector.Select(
            root, 1, 1, Policy(distanceUnit: 1000),
            tile => tile.Level == 0 && tile.X != 1);

        Assert.False(selection.CompleteCoverage);
        Assert.Empty(selection.Nodes);
        Assert.True(selection.MissingCoverageGroups > 0);
    }

    private static TerrainLodSpatialPolicy Policy(double distanceUnit = 8) =>
        new(distanceUnit, 2.0, [0, 0, 1, 1, 2], [32, 24, 16, 12, 8]);

    private static void AssertCompletePartition(
        TerrainLodTileKey root,
        IEnumerable<TerrainLodTileSelection> selection)
    {
        var covered = new HashSet<(long X, long Z)>();
        foreach (var node in selection)
        for (var x = node.Tile.MinChunkX; x <= node.Tile.MaxChunkX; x++)
        for (var z = node.Tile.MinChunkZ; z <= node.Tile.MaxChunkZ; z++)
            Assert.True(covered.Add((x, z)), $"Chunk {x},{z} was covered twice.");

        Assert.Equal(root.ChunkWidth * root.ChunkWidth, covered.Count);
    }
}
