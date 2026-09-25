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
    public void Coverage_plan_coarsens_only_the_far_boundary_to_fit_its_hard_budget()
    {
        var plan = TerrainLodCoveragePlanner.PlanRequiredTiles(
            cameraChunkX: 0.25,
            cameraChunkZ: -0.25,
            nearDistanceChunks: 4,
            horizonDistanceChunks: 4096,
            rootLevel: 10,
            nearBoundaryMinimumLevel: 2,
            outerBoundaryMinimumLevel: 2,
            maximumTiles: TerrainLodScaleBudget.MaximumCoverageTiles);

        Assert.True(plan.CoarsenedForBudget);
        Assert.Equal(2, plan.RequestedOuterBoundaryMinimumLevel);
        Assert.InRange(plan.EffectiveOuterBoundaryMinimumLevel, 3, 10);
        Assert.InRange(plan.Tiles.Count, 1, TerrainLodScaleBudget.MaximumCoverageTiles);
        Assert.Contains(plan.Tiles, key => key.Level == 2);
    }

    [Fact]
    public void Optional_refinement_plan_defers_instead_of_exceeding_its_budget()
    {
        var planned = TerrainLodCoveragePlanner.TryPlanRequiredTiles(
            cameraChunkX: 0,
            cameraChunkZ: 20,
            nearDistanceChunks: 4,
            horizonDistanceChunks: 64,
            rootLevel: 2,
            nearBoundaryMinimumLevel: 2,
            outerBoundaryMinimumLevel: 2,
            out var plan,
            maximumTiles: TerrainLodScaleBudget.MaximumCoverageTiles);

        Assert.False(planned);
        Assert.Null(plan);
    }

    [Fact]
    public void Selection_collapses_ready_descendants_to_their_parent_before_exceeding_budget()
    {
        var policy = TerrainLodSpatialPolicy.CreateDefault();
        var root = new TerrainLodTileKey(3, 0, 0);
        HashSet<TerrainLodTileKey> ready = [root];
        for (var index = 0; index < 4; index++) ready.Add(root.Child(index));

        var selection = TerrainLodCoveragePlanner.SelectCompleteCover(
            [root], 0, 0, policy, ready.Contains, maximumSelectedNodes: 1);

        Assert.True(selection.CompleteCoverage);
        Assert.True(selection.CompleteHorizon);
        Assert.True(selection.BudgetLimited);
        Assert.Equal(root, Assert.Single(Assert.Single(selection.Roots).Nodes).Tile);
    }

    [Fact]
    public void Selection_retains_no_partial_band_when_it_cannot_fit_the_node_budget()
    {
        var policy = TerrainLodSpatialPolicy.CreateDefault();
        var root = new TerrainLodTileKey(3, 0, 0);
        HashSet<TerrainLodTileKey> ready = [];
        for (var index = 0; index < 4; index++) ready.Add(root.Child(index));

        var selection = TerrainLodCoveragePlanner.SelectCompleteCover(
            [root], 0, 0, policy, ready.Contains, maximumSelectedNodes: 2);

        Assert.False(selection.CompleteCoverage);
        Assert.False(selection.CompleteHorizon);
        Assert.True(selection.BudgetLimited);
        Assert.Empty(selection.Roots);
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
    public void Cold_cover_replaces_a_tiny_preferred_island_with_a_much_larger_ready_patch()
    {
        var policy = TerrainLodSpatialPolicy.CreateDefault();
        var tiny = new TerrainLodTileKey(2, 8, -5);
        var patch = (from x in Enumerable.Range(7, 3)
                     from z in Enumerable.Range(-2, 4)
                     select new TerrainLodTileKey(2, x, z)).ToArray();
        var ready = patch.Append(tiny).ToHashSet();

        var selection = TerrainLodCoveragePlanner.SelectContiguousAvailableCover(
            ready, minimumVisibleLevel: 2,
            cameraChunkX: 33, cameraChunkZ: -8,
            maximumDistanceChunks: 20, policy, ready.Contains,
            new HashSet<TerrainLodTileKey> { tiny });

        Assert.Equal(patch.Length, selection.SelectedNodes);
        Assert.DoesNotContain(selection.Roots, root => root.Root == tiny);
        Assert.Contains(selection.Roots, root => root.Root == new TerrainLodTileKey(2, 8, 0));
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
    public void Invalidated_remote_tile_is_requested_while_old_coverage_is_retained()
    {
        using var renderer = new ClientTerrainLodRenderer(new LightTestWorld());
        Vector3D<double> camera = new(0, 80, 0);
        var column = TerrainLodColumn.Create(4,
        [new TerrainLodColumnSpan(0, 4, TerrainLodMaterial.Air, 0, 15)]);
        List<TerrainLodTileKey> received = [];
        for (var batch = 0; batch < 8; batch++)
        {
            var requests = renderer.TakeRemoteSpatialRequests(
                camera, nearDistanceChunks: 0, horizonDistanceChunks: 4,
                maximumRequests: 16);
            if (requests.Length == 0) break;
            foreach (var key in requests)
            {
                received.Add(key);
                renderer.ObserveRemoteSpatialTile(TerrainLodColumnTile.CreateUniform(
                    key, 0, 4, column, "refresh-test"));
            }
        }
        Assert.NotEmpty(received);
        Assert.Empty(renderer.TakeRemoteSpatialRequests(
            camera, 0, 4, maximumRequests: 16));

        var stale = received[0];
        renderer.ObserveRemoteSpatialStatus(stale, TerrainLodTileStatus.Invalidated,
            generation: 1);
        Assert.Equal([stale], renderer.TakeRemoteSpatialRequests(
            camera, 0, 4, maximumRequests: 16));
        Assert.Empty(renderer.TakeRemoteSpatialRequests(
            camera, 0, 4, maximumRequests: 16));

        renderer.ObserveRemoteSpatialTile(TerrainLodColumnTile.CreateUniform(
            stale, 0, 4, column, "refresh-test"));
        Assert.True(renderer.HasPendingRemoteRefresh(stale));
        renderer.ObserveRemoteSpatialStatus(stale, TerrainLodTileStatus.Invalidated,
            generation: 2);
        renderer.ObserveRemoteSpatialTile(TerrainLodColumnTile.CreateUniform(
            stale, 0, 4, column, "refresh-test-intermediate"), generation: 1);
        Assert.True(renderer.HasPendingRemoteRefresh(stale));
        renderer.ObserveRemoteSpatialTile(TerrainLodColumnTile.CreateUniform(
            stale, 0, 4, column, "refresh-test-replacement"), generation: 2);
        Assert.False(renderer.HasPendingRemoteRefresh(stale));
        Assert.Empty(renderer.TakeRemoteSpatialRequests(
            camera, 0, 4, maximumRequests: 16));
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
    public void Remote_coverage_respects_the_negotiated_peer_maximum()
    {
        using var renderer = new ClientTerrainLodRenderer(new LightTestWorld());

        var requests = renderer.TakeRemoteSpatialRequests(
            new Vector3D<double>(0, 80, 0),
            nearDistanceChunks: 0,
            horizonDistanceChunks: 256,
            maximumRequests: 4,
            maximumSpatialLevel: 4);

        Assert.NotEmpty(requests);
        Assert.All(requests, key => Assert.Equal(4, key.Level));
    }

    [Fact]
    public void Generated_hierarchy_keeps_large_horizon_partitions_sublinear()
    {
        var policy = TerrainLodSpatialPolicy.CreateForMaximumHorizon(
            TerrainLodSpatialPolicy.MaximumGeneratedHorizonChunks);
        int[] horizons = [64, 128, 256, 512, 1024, 2048, 4096];
        var partitions = horizons.Select(horizon =>
        {
            var rootLevel = policy.DesiredSpatialLevel(horizon);
            var outerBoundaryMinimumLevel =
                TerrainLodCoveragePlanner.RecommendedOuterBoundaryMinimumLevel(
                    rootLevel, TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel);
            var keys = TerrainLodCoveragePlanner.RequiredTiles(
                cameraChunkX: -3.25,
                cameraChunkZ: 5.75,
                nearDistanceChunks: 8,
                horizonDistanceChunks: horizon,
                rootLevel,
                TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel,
                outerBoundaryMinimumLevel);
            return (Horizon: horizon, RootLevel: rootLevel,
                OuterBoundaryMinimumLevel: outerBoundaryMinimumLevel, Keys: keys);
        }).ToArray();

        Assert.Equal([4, 5, 6, 7, 8, 9, 10],
            partitions.Select(static value => value.RootLevel));
        Assert.Equal([2, 3, 4, 5, 6, 7, 8],
            partitions.Select(static value => value.OuterBoundaryMinimumLevel));
        Assert.All(partitions, partition =>
        {
            // The far-boundary floor rises with hierarchy depth, so the complete annulus remains
            // within one stable node budget instead of growing with either area or circumference.
            Assert.InRange(partition.Keys.Length, 1, 512);
            Assert.All(partition.Keys, key => Assert.InRange(
                key.Level,
                TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel,
                partition.RootLevel));
        });
        for (var index = 1; index < partitions.Length; index++)
            Assert.True(partitions[index].Keys.Length <=
                        partitions[index - 1].Keys.Length * 3 / 2);

        // Every coarse root is capped at 64x64 horizontal samples. This bounds per-draw vertex,
        // upload, and memory work even though its represented area grows by four each level.
        Assert.All(partitions.SelectMany(static partition => partition.Keys), key =>
        {
            var samplesAcross = key.ChunkWidth * 16 /
                                (1 << policy.HorizontalSampleLevelForSpatialLevel(key.Level));
            Assert.InRange(samplesAcross, 1, 64);
        });
    }

    [Fact]
    public void Near_and_far_boundaries_use_independent_refinement_floors()
    {
        const double cameraX = -3.25;
        const double cameraZ = 5.75;
        const int nearDistance = 8;
        const int horizonDistance = 256;
        const int rootLevel = 6;
        const int nearMinimumLevel = 2;
        var outerMinimumLevel =
            TerrainLodCoveragePlanner.RecommendedOuterBoundaryMinimumLevel(
                rootLevel, nearMinimumLevel);

        var keys = TerrainLodCoveragePlanner.RequiredTiles(
            cameraX, cameraZ, nearDistance, horizonDistance,
            rootLevel, nearMinimumLevel, outerMinimumLevel);

        Assert.Equal(4, outerMinimumLevel);
        Assert.Contains(keys, key => key.Level == nearMinimumLevel &&
                                     key.DistanceTo(cameraX, cameraZ) < nearDistance &&
                                     FurthestDistance(key, cameraX, cameraZ) > nearDistance);
        Assert.All(keys.Where(key =>
                key.DistanceTo(cameraX, cameraZ) <= horizonDistance &&
                FurthestDistance(key, cameraX, cameraZ) > horizonDistance &&
                !(key.DistanceTo(cameraX, cameraZ) < nearDistance &&
                  FurthestDistance(key, cameraX, cameraZ) > nearDistance)),
            key => Assert.True(key.Level >= outerMinimumLevel));
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
