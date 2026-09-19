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
        var roots = TerrainLodRemoteCoveragePlanner.RequiredTiles(
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
        Assert.Equal(roots, TerrainLodRemoteCoveragePlanner.RequiredTiles(
            cameraX, cameraZ, 12, 64, 4, 2));
    }

    [Fact]
    public void Negative_coordinates_use_the_same_tile_coverage_contract()
    {
        const double cameraX = -20.5;
        const double cameraZ = -11.5;
        var containing = TerrainLodTileKey.ContainingChunk(2, -21, -12);

        var roots = TerrainLodRemoteCoveragePlanner.RequiredTiles(
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

        Assert.False(TerrainLodRemoteCoveragePlanner.HasCompleteCoverage(
            root, 2, available.Contains));

        foreach (var levelThree in Enumerable.Range(0, 4).Select(root.Child))
        foreach (var levelTwo in Enumerable.Range(0, 4).Select(levelThree.Child))
            available.Add(levelTwo);
        Assert.True(TerrainLodRemoteCoveragePlanner.HasCompleteCoverage(
            root, 2, available.Contains));

        available.Remove(root.Child(0).Child(0));
        Assert.False(TerrainLodRemoteCoveragePlanner.HasCompleteCoverage(
            root, 2, available.Contains));

        available.Clear();
        available.Add(root);
        Assert.True(TerrainLodRemoteCoveragePlanner.HasCompleteCoverage(
            root, 2, available.Contains));
    }

    [Fact]
    public void Horizon_inside_near_radius_has_no_remote_contract()
    {
        Assert.Empty(TerrainLodRemoteCoveragePlanner.RequiredTiles(0, 0, 16, 16, 4, 2));
        Assert.Empty(TerrainLodRemoteCoveragePlanner.RequiredTiles(0, 0, 32, 16, 4, 2));
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

        var near = nearRenderer.TakeRemoteSpatialRequests(
            camera, nearDistanceChunks: 0, horizonDistanceChunks: 16,
            maximumRequests: 4);
        var far = farRenderer.TakeRemoteSpatialRequests(
            camera, nearDistanceChunks: 0, horizonDistanceChunks: 64,
            maximumRequests: 4);

        Assert.All(near, key => Assert.Equal(2, key.Level));
        Assert.All(far, key => Assert.Equal(4, key.Level));
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
