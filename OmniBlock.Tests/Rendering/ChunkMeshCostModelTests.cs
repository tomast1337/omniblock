using OmniBlock.Client.Rendering.Chunks;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkMeshCostModelTests
{
    [Fact]
    public void Cold_estimate_scales_with_rebuilt_page_count()
    {
        ChunkMeshCostModel model = new();

        var one = model.Estimate(new SectionMeshRebuildPlan(0b0001));
        var four = model.Estimate(SectionMeshRebuildPlan.Full);

        Assert.Equal(ChunkMeshCostModel.ColdBuildMsPerPage, one.BuildMs);
        Assert.Equal(ChunkMeshCostModel.ColdResultBytesPerPage, one.ResultBytes);
        Assert.Equal(one.BuildMs * 4, four.BuildMs);
        Assert.Equal(one.ResultBytes * 4, four.ResultBytes);
        Assert.True(four.UploadMs > one.UploadMs);
    }

    [Fact]
    public void Build_observations_replace_cold_defaults_and_local_history_overrides_global_bytes()
    {
        ChunkMeshCostModel model = new();
        model.RecordBuild(elapsedMs: 8, pages: 4, resultBytes: 4000);

        var learned = model.Estimate(new SectionMeshRebuildPlan(0b0011));
        var local = model.Estimate(new SectionMeshRebuildPlan(0b0011), previousBytesPerPage: 7000);

        Assert.Equal(4, learned.BuildMs);
        Assert.Equal(2000, learned.ResultBytes);
        Assert.Equal(14_000, local.ResultBytes);
        Assert.Equal(4, local.BuildMs);
    }

    [Fact]
    public void Upload_observations_update_both_fixed_and_byte_costs()
    {
        ChunkMeshCostModel model = new();
        var before = model.Snapshot();

        model.RecordUpload(elapsedMs: 2, bytes: 2 * 1024 * 1024);
        var after = model.Snapshot();

        Assert.Equal(1, after.UploadSamples);
        Assert.NotEqual(before.UploadBaseMs, after.UploadBaseMs);
        Assert.NotEqual(before.UploadMsPerMiB, after.UploadMsPerMiB);
        Assert.True(model.EstimateUploadMs(4 * 1024 * 1024) > model.EstimateUploadMs(1024));
    }
}
