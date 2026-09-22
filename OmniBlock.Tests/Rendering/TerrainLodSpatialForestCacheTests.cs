using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodSpatialForestCacheTests
{
    [Fact]
    public void One_bridge_install_invalidates_stationary_selection_and_connects_ready_outer_terrain()
    {
        using var catalog = new TerrainLodSpatialPresentationSet<Resource>();
        var near = new TerrainLodTileKey(2, 0, 0);
        var bridge = new TerrainLodTileKey(2, 1, 0);
        var far = new TerrainLodTileKey(2, 2, 0);
        catalog.TryInstall(near, "near", () => new(), out _);
        catalog.TryInstall(far, "far", () => new(), out _);
        var cachedKey = Key(catalog.Revision);
        var before = Select();
        Assert.Equal(near, Assert.Single(before.Roots).Root);

        catalog.TryInstall(bridge, "bridge", () => new(), out _);
        var currentKey = Key(catalog.Revision);
        Assert.NotEqual(cachedKey, currentKey);
        // Both revisions used to occupy the same revision/8 bucket despite different coverage.
        Assert.Equal(cachedKey.PresentationRevision / 8, currentKey.PresentationRevision / 8);
        var after = Select();
        Assert.Equal(3, after.SelectedNodes);
        Assert.Contains(after.Roots, root => root.Root == far);
        Assert.Equal(currentKey, Key(catalog.Revision)); // no work -> cache hit

        TerrainLodCoarseCoverSelection Select() => TerrainLodCoveragePlanner.SelectContiguousAvailableCover(
            catalog.ReadyKeys, 2, 0.5, 0.5, 16, TerrainLodSpatialPolicy.CreateDefault(), catalog.IsReady);
    }

    [Fact]
    public void Single_eviction_also_invalidates_the_cached_partition()
    {
        using var catalog = new TerrainLodSpatialPresentationSet<Resource>();
        var tile = new TerrainLodTileKey(2, 0, 0);
        catalog.TryInstall(tile, "one", () => new(), out _);
        var before = Key(catalog.Revision);
        Assert.True(catalog.TryEvict(tile));
        Assert.NotEqual(before, Key(catalog.Revision));
    }

    private static ClientTerrainLodRenderer.SpatialForestCacheKey Key(long revision) =>
        new(0.5, 0.5, 4, 16, 1, revision);

    private sealed class Resource : IDisposable
    {
        public void Dispose() { }
    }
}
