using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodSpatialResidencyPolicyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Active_cover_at_or_above_target_does_not_pin_optional_cache(long excess)
    {
        var candidates = new[] { (new TerrainLodTileKey(3, 0, 0), 1024L) };
        Assert.Empty(TerrainLodSpatialResidencyPolicy.SelectLeadingEdge(
            candidates, TerrainLodScaleBudget.TargetSpatialGpuBytes + excess));
    }

    [Fact]
    public void Leading_edge_uses_only_remaining_bytes_in_input_order()
    {
        var candidates = new[]
        {
            (new TerrainLodTileKey(3, 0, 0), 60L),
            (new TerrainLodTileKey(3, 1, 0), 50L),
            (new TerrainLodTileKey(3, 2, 0), 40L)
        };
        Assert.Equal(new[] { candidates[0].Item1, candidates[2].Item1 },
            TerrainLodSpatialResidencyPolicy.SelectLeadingEdge(
                candidates, TerrainLodScaleBudget.TargetSpatialGpuBytes - 100));
    }

    [Fact]
    public void Tiny_entries_still_obey_the_count_limit()
    {
        var candidates = Enumerable.Range(0, 1000)
            .Select(x => (new TerrainLodTileKey(3, x, 0), 1L));
        Assert.Equal(TerrainLodScaleBudget.MaximumLeadingEdgePresentations,
            TerrainLodSpatialResidencyPolicy.SelectLeadingEdge(candidates, 0).Count());
    }
}
