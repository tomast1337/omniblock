using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodQualityDiagnosticsTests
{
    [Theory]
    [InlineData(2, 0, "minimum-spatial-level")]
    [InlineData(2, 2, "distance-target")]
    [InlineData(4, 2, "coarser-than-distance-target")]
    [InlineData(2, 4, "finer-than-distance-target")]
    public void Relation_does_not_confuse_the_remote_floor_with_a_missing_child(
        int selected, int desired, string expected) =>
        Assert.Equal(expected, TerrainLodQualityDiagnostics.SelectionRelation(selected, desired, 2));

    [Fact]
    public void Mesh_quality_uses_compiled_data_not_the_current_policy()
    {
        var mesh = new TerrainLodSpatialMeshData(new TerrainLodTileKey(2, -1, 0), "old", 0,
            24, false, 19, [], new TerrainLodSpatialMeshBuildProfile(0, 0, 0, 0, 4096, 12000, 2,
                14000, 200, 100, 11000)) { CaveCullBelowY = 60 };
        var quality = TerrainLodSpatialMeshQuality.FromMesh(mesh);
        Assert.Equal(1, quality.HorizontalSampleBlocks);
        Assert.Equal(24, quality.VerticalSliceBudget);
        Assert.Equal(19, quality.MaximumRenderedSpans);
        Assert.Equal(4096, quality.SourceColumns);
        Assert.Equal(12000, quality.SourceSpansAfterCaveCulling);
        Assert.Equal(14000, quality.CanonicalSpans);
        Assert.Equal(200, quality.CaveCulledColumns);
        Assert.Equal(100, quality.VerticalReducedColumns);
        Assert.Equal(11000, quality.RenderedSpans);
        Assert.Equal(60, quality.CaveCullBelowY);
        Assert.Equal(2, 1 << TerrainLodSpatialPolicy.CreateDefault().HorizontalSampleLevelForSpatialLevel(2));
        // A new revision cannot silently change diagnostic metadata on the retained predecessor.
        var replacement = TerrainLodSpatialMeshQuality.FromMesh(mesh with { HorizontalSampleLevel = 1 });
        Assert.Equal(2, replacement.HorizontalSampleBlocks);
        Assert.Equal(1, quality.HorizontalSampleBlocks);
    }
}
