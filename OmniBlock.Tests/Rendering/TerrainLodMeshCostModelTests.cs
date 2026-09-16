using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodMeshCostModelTests
{
    private static readonly TerrainLodMaterialCatalog Materials = new(
    [
        new TerrainLodMaterialDefinition(
            1, "example:stone", TerrainLodGeometryClass.Opaque, true, 0x707070)
    ]);

    [Fact]
    public void Cold_estimate_scales_with_the_requested_hierarchy_cells()
    {
        var hierarchy = Hierarchy();
        TerrainLodMeshCostModel model = new();

        var coverage = model.Estimate(hierarchy, 2, 4);
        var refinement = model.Estimate(hierarchy, 0, 4);

        Assert.True(refinement.WorkCells > coverage.WorkCells);
        Assert.True(refinement.CompilationMs > coverage.CompilationMs);
        Assert.True(refinement.ResultBytes > coverage.ResultBytes);
        Assert.True(refinement.UploadMs > coverage.UploadMs);
    }

    [Fact]
    public void Observations_replace_cold_compile_result_and_upload_costs()
    {
        var hierarchy = Hierarchy();
        TerrainLodMeshCostModel model = new();
        var cold = model.Estimate(hierarchy, 2, 4);

        model.RecordCompilation(12, cold.WorkCells, 512_000);
        model.RecordUpload(2, 2 * 1024 * 1024);
        var learned = model.Estimate(hierarchy, 2, 4);
        var snapshot = model.Snapshot();

        Assert.Equal(1, snapshot.CompilationSamples);
        Assert.Equal(1, snapshot.UploadSamples);
        Assert.Equal(12, learned.CompilationMs, precision: 6);
        Assert.Equal(512_000, learned.ResultBytes);
        Assert.True(model.EstimateUploadMs(4 * 1024 * 1024) >
                    model.EstimateUploadMs(1024));
    }

    [Fact]
    public void Coverage_leads_but_refinement_cannot_starve()
    {
        TerrainLodWorkFairness fairness = new();

        for (var i = 0; i < TerrainLodWorkFairness.CoverageBurstLimit; i++)
        {
            Assert.Equal(TerrainLodMeshWorkKind.Coverage,
                fairness.Peek(hasCoverage: true, hasRefinement: true));
            fairness.Commit(TerrainLodMeshWorkKind.Coverage);
        }

        Assert.Equal(TerrainLodMeshWorkKind.Refinement,
            fairness.Peek(hasCoverage: true, hasRefinement: true));
        fairness.Commit(TerrainLodMeshWorkKind.Refinement);
        Assert.Equal(TerrainLodMeshWorkKind.Coverage,
            fairness.Peek(hasCoverage: true, hasRefinement: true));
    }

    private static TerrainLodHierarchy Hierarchy()
    {
        var blocks = Enumerable.Repeat((byte)1, 16 * 16 * 16).ToArray();
        var source = new TerrainLodSourceSnapshot(
            0, 0, 16, 16, 16, blocks, new byte[blocks.Length], 1);
        return TerrainLodReducer.Build(
            source, Materials, TerrainLodReductionStrategy.SurfacePreserving);
    }
}
