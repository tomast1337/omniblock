using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Network.Messages;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Lod;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodRemoteCoveragePlannerTests
{
    [Fact]
    public void Required_tiles_form_a_deterministic_radial_annulus()
    {
        const double cameraX = -3.25;
        const double cameraZ = 5.75;
        var roots = TerrainLodCoveragePlanner.RequiredTiles(
            cameraX, cameraZ, nearDistanceChunks: 12,
            horizonDistanceChunks: 64, rootLevel: 4, minimumLevel: 2);

        Assert.NotEmpty(roots);
        Assert.Equal(roots.Length, roots.Distinct().Count());
        Assert.All(roots, key =>
        {
            Assert.InRange(key.Level, 2, 4);
            Assert.True(key.DistanceTo(cameraX, cameraZ) <= 64);
            Assert.True(FurthestDistance(key, cameraX, cameraZ) > 12);
            if (key.Level > 2)
            {
                Assert.True(FurthestDistance(key, cameraX, cameraZ) <= 64);
                Assert.True(key.DistanceTo(cameraX, cameraZ) >= 12);
            }
        });
        Assert.Equal(roots, roots
            .OrderBy(key => key.DistanceTo(cameraX, cameraZ))
            .ThenByDescending(static key => key.Level)
            .ThenBy(static key => key.X)
            .ThenBy(static key => key.Z));
        Assert.Equal(roots, TerrainLodCoveragePlanner.RequiredTiles(
            cameraX, cameraZ, 12, 64, 4, 2));
    }

    [Fact]
    public void Negative_coordinates_use_the_same_tile_coverage_contract()
    {
        const double cameraX = -20.5;
        const double cameraZ = -11.5;
        var containing = TerrainLodTileKey.ContainingChunk(2, -21, -12);

        var roots = TerrainLodCoveragePlanner.RequiredTiles(
            cameraX, cameraZ, nearDistanceChunks: 0,
            horizonDistanceChunks: 6, rootLevel: 2, minimumLevel: 2);

        Assert.Contains(containing, roots);
        Assert.Equal(-6, containing.X);
        Assert.Equal(-3, containing.Z);
    }

    [Fact]
    public void Root_or_complete_descendant_partition_satisfies_coverage()
    {
        var root = new TerrainLodTileKey(4, -2, 3);
        HashSet<TerrainLodTileKey> available = [];

        Assert.False(TerrainLodCoveragePlanner.HasCompleteCoverage(
            root, 2, available.Contains));

        foreach (var levelThree in Enumerable.Range(0, 4).Select(root.Child))
        foreach (var levelTwo in Enumerable.Range(0, 4).Select(levelThree.Child))
            available.Add(levelTwo);
        Assert.True(TerrainLodCoveragePlanner.HasCompleteCoverage(
            root, 2, available.Contains));

        available.Remove(root.Child(0).Child(0));
        Assert.False(TerrainLodCoveragePlanner.HasCompleteCoverage(
            root, 2, available.Contains));

        available.Clear();
        available.Add(root);
        Assert.True(TerrainLodCoveragePlanner.HasCompleteCoverage(
            root, 2, available.Contains));
    }

    [Fact]
    public void Missing_nodes_are_prioritized_by_coverage_gain_before_distance()
    {
        var policy = TerrainLodSpatialPolicy.CreateDefault();
        var nearbyFine = new TerrainLodTileKey(2, 0, 0);
        var fartherCoarse = new TerrainLodTileKey(4, 2, 0);

        var ordered = TerrainLodCoveragePlanner.PrioritizeMissing(
            [nearbyFine, fartherCoarse], 0, 0, policy);

        Assert.Equal(fartherCoarse, ordered[0]);
        Assert.True(
            TerrainLodCoveragePlanner.CoverageGainPerBuildUnit(fartherCoarse, policy) >
            TerrainLodCoveragePlanner.CoverageGainPerBuildUnit(nearbyFine, policy));
    }

    [Fact]
    public void Coarse_cover_never_exposes_a_ready_island_beyond_a_missing_band()
    {
        var policy = TerrainLodSpatialPolicy.CreateDefault();
        var first = new TerrainLodTileKey(2, 0, 0);
        var second = new TerrainLodTileKey(2, 1, 0);
        HashSet<TerrainLodTileKey> ready = [second];

        var incomplete = TerrainLodCoveragePlanner.SelectCompleteCover(
            [first, second], 0, 0, policy, ready.Contains);

        Assert.False(incomplete.CompleteCoverage);
        Assert.Empty(incomplete.Roots);
        Assert.Equal(0, incomplete.SelectedNodes);

        ready.Add(first);
        var complete = TerrainLodCoveragePlanner.SelectCompleteCover(
            [first, second], 0, 0, policy, ready.Contains);

        Assert.True(complete.CompleteCoverage);
        Assert.True(complete.CompleteHorizon);
        Assert.Equal(2, complete.Roots.Count);
        Assert.Equal(2, complete.SelectedNodes);
    }

    [Fact]
    public void Coarse_cover_publishes_a_complete_near_band_before_the_full_horizon()
    {
        var policy = TerrainLodSpatialPolicy.CreateDefault();
        var near = new TerrainLodTileKey(2, 0, 0);
        var far = new TerrainLodTileKey(2, 2, 0);
        HashSet<TerrainLodTileKey> ready = [near];

        var selection = TerrainLodCoveragePlanner.SelectCompleteCover(
            [near, far], 0, 0, policy, ready.Contains);

        Assert.True(selection.CompleteCoverage);
        Assert.False(selection.CompleteHorizon);
        Assert.Equal(near, Assert.Single(selection.Roots).Root);
    }

    [Fact]
    public void Coarse_cover_accepts_a_complete_descendant_partition()
    {
        var policy = TerrainLodSpatialPolicy.CreateDefault();
        var root = new TerrainLodTileKey(3, 0, 0);
        HashSet<TerrainLodTileKey> ready = [];
        for (var index = 0; index < 4; index++) ready.Add(root.Child(index));

        var selection = TerrainLodCoveragePlanner.SelectCompleteCover(
            [root], 0, 0, policy, ready.Contains);

        Assert.True(selection.CompleteCoverage);
        Assert.Single(selection.Roots);
        Assert.Equal(4, selection.SelectedNodes);
    }

    [Fact]
    public void Unavailable_frontier_exposes_only_one_connected_ready_component()
    {
        var policy = TerrainLodSpatialPolicy.CreateDefault();
        var near = new TerrainLodTileKey(2, 0, 0);
        var adjacent = new TerrainLodTileKey(2, 1, 0);
        var isolated = new TerrainLodTileKey(2, 10, 0);
        HashSet<TerrainLodTileKey> ready = [near, adjacent, isolated];

        var selection = TerrainLodCoveragePlanner.SelectContiguousAvailableCover(
            ready, minimumVisibleLevel: 2,
            cameraChunkX: 0, cameraChunkZ: 0,
            maximumDistanceChunks: 64, policy, ready.Contains);

        Assert.True(selection.CompleteCoverage);
        Assert.False(selection.CompleteHorizon);
        Assert.Equal(2, selection.SelectedNodes);
        Assert.Contains(selection.Roots, root => root.Root == near);
        Assert.Contains(selection.Roots, root => root.Root == adjacent);
        Assert.DoesNotContain(selection.Roots, root => root.Root == isolated);

        var retained = TerrainLodCoveragePlanner.SelectContiguousAvailableCover(
            ready, minimumVisibleLevel: 2,
            cameraChunkX: 0, cameraChunkZ: 0,
            maximumDistanceChunks: 64, policy, ready.Contains,
            new HashSet<TerrainLodTileKey> { isolated });
        Assert.Equal(isolated, Assert.Single(retained.Roots).Root);
    }

    [Fact]
    public void Recenter_keeps_a_larger_previous_cover_until_the_leading_edge_catches_up()
    {
        TerrainLodTileSelection[] previous =
        [
            new(new TerrainLodTileKey(3, 0, 0), 1, 12),
            new(new TerrainLodTileKey(3, 1, 0), 1, 12)
        ];
        TerrainLodTileSelection[] smallCandidate =
        [new(new TerrainLodTileKey(3, 2, 0), 1, 12)];
        TerrainLodTileSelection[] completeCandidate =
        [
            new(new TerrainLodTileKey(3, 2, 0), 1, 12),
            new(new TerrainLodTileKey(3, 3, 0), 1, 12)
        ];

        Assert.True(TerrainLodCoveragePlanner.ShouldRetainPreviousCover(
            previous, smallCandidate, 16, 0, 64));
        Assert.False(TerrainLodCoveragePlanner.ShouldRetainPreviousCover(
            previous, completeCandidate, 16, 0, 64));
        Assert.False(TerrainLodCoveragePlanner.ShouldRetainPreviousCover(
            previous, smallCandidate, 200, 0, 16));
    }

    [Fact]
    public void Horizon_inside_near_radius_has_no_remote_contract()
    {
        Assert.Empty(TerrainLodCoveragePlanner.RequiredTiles(0, 0, 16, 16, 4, 2));
        Assert.Empty(TerrainLodCoveragePlanner.RequiredTiles(0, 0, 32, 16, 4, 2));
    }

    [Fact]
    public void Renderer_reserves_bounded_request_capacity_for_coarse_coverage_first()
    {
        using var renderer = new ClientTerrainLodRenderer(new LightTestWorld());
        Vector3D<double> camera = new(0, 80, 0);
        List<TerrainLodTileKey> admitted = [];

        for (var batch = 0; batch < 4; batch++)
            admitted.AddRange(renderer.TakeRemoteSpatialRequests(
                camera, nearDistanceChunks: 0, horizonDistanceChunks: 64,
                maximumRequests: 4));

        Assert.Equal(16, admitted.Count);
        Assert.All(admitted, key => Assert.Equal(4, key.Level));
        Assert.Equal(admitted.Count, admitted.Distinct().Count());
        Assert.Empty(renderer.TakeRemoteSpatialRequests(
            camera, nearDistanceChunks: 0, horizonDistanceChunks: 64,
            maximumRequests: 4));
    }

    [Fact]
    public void Missing_coarse_tile_opens_its_children_before_unrelated_refinement()
    {
        using var renderer = new ClientTerrainLodRenderer(new LightTestWorld());
        Vector3D<double> camera = new(0, 80, 0);
        var roots = renderer.TakeRemoteSpatialRequests(
            camera, nearDistanceChunks: 0, horizonDistanceChunks: 64,
            maximumRequests: 4);
        Assert.All(roots, key => Assert.Equal(4, key.Level));
        foreach (var root in roots)
            renderer.ObserveRemoteSpatialStatus(root, TerrainLodTileStatus.Missing);

        var descendants = renderer.TakeRemoteSpatialRequests(
            camera, nearDistanceChunks: 0, horizonDistanceChunks: 64,
            maximumRequests: 4);

        Assert.Equal(4, descendants.Length);
        Assert.All(descendants, key => Assert.Equal(3, key.Level));
        Assert.All(descendants, key => Assert.Equal(roots[0], key.Parent()));
    }

    [Fact]
    public void Coverage_root_scale_tracks_the_configured_horizon()
    {
        Vector3D<double> camera = new(0, 80, 0);
        using var nearRenderer = new ClientTerrainLodRenderer(new LightTestWorld());
        using var farRenderer = new ClientTerrainLodRenderer(new LightTestWorld());
        using var fartherRenderer = new ClientTerrainLodRenderer(new LightTestWorld());
        using var maximumRenderer = new ClientTerrainLodRenderer(new LightTestWorld());

        var near = nearRenderer.TakeRemoteSpatialRequests(
            camera, nearDistanceChunks: 0, horizonDistanceChunks: 16,
            maximumRequests: 4);
        var far = farRenderer.TakeRemoteSpatialRequests(
            camera, nearDistanceChunks: 0, horizonDistanceChunks: 64,
            maximumRequests: 4);
        var farther = fartherRenderer.TakeRemoteSpatialRequests(
            camera, nearDistanceChunks: 0, horizonDistanceChunks: 128,
            maximumRequests: 4);
        var maximum = maximumRenderer.TakeRemoteSpatialRequests(
            camera, nearDistanceChunks: 0, horizonDistanceChunks: 256,
            maximumRequests: 4);

        Assert.All(near, key => Assert.Equal(2, key.Level));
        Assert.All(far, key => Assert.Equal(4, key.Level));
        Assert.All(farther, key => Assert.Equal(5, key.Level));
        Assert.All(maximum, key => Assert.Equal(6, key.Level));
    }

    [Fact]
    public void Generated_hierarchy_keeps_large_horizon_partitions_sublinear()
    {
        var policy = TerrainLodSpatialPolicy.CreateDefault();
        int[] horizons = [64, 128, 256];
        var partitions = horizons.Select(horizon =>
        {
            var rootLevel = policy.DesiredSpatialLevel(horizon);
            var keys = TerrainLodCoveragePlanner.RequiredTiles(
                cameraChunkX: -3.25,
                cameraChunkZ: 5.75,
                nearDistanceChunks: 8,
                horizonDistanceChunks: horizon,
                rootLevel,
                TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel);
            return (Horizon: horizon, RootLevel: rootLevel, Keys: keys);
        }).ToArray();

        Assert.Equal([4, 5, 6], partitions.Select(static value => value.RootLevel));
        Assert.All(partitions, partition =>
        {
            // Boundary refinement grows with circumference, not horizon area. The loose constant
            // leaves room for camera alignment while catching a regression to level-2 tiling of
            // the complete disk immediately.
            Assert.InRange(partition.Keys.Length, 1, partition.Horizon * 6);
            Assert.All(partition.Keys, key => Assert.InRange(
                key.Level,
                TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel,
                partition.RootLevel));
        });
        Assert.True(partitions[1].Keys.Length <= partitions[0].Keys.Length * 3);
        Assert.True(partitions[2].Keys.Length <= partitions[1].Keys.Length * 3);

        // Every coarse root is capped at 64x64 horizontal samples. This bounds per-draw vertex,
        // upload, and memory work even though its represented area grows by four each level.
        Assert.All(partitions.SelectMany(static partition => partition.Keys), key =>
        {
            var samplesAcross = key.ChunkWidth * 16 /
                                (1 << policy.HorizontalSampleLevelForSpatialLevel(key.Level));
            Assert.InRange(samplesAcross, 1, 64);
        });
    }

    private static double FurthestDistance(
        TerrainLodTileKey key,
        double chunkX,
        double chunkZ)
    {
        var dx = Math.Max(
            Math.Abs(key.MinChunkX - chunkX),
            Math.Abs(key.MaxChunkX + 1.0 - chunkX));
        var dz = Math.Max(
            Math.Abs(key.MinChunkZ - chunkZ),
            Math.Abs(key.MaxChunkZ + 1.0 - chunkZ));
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
