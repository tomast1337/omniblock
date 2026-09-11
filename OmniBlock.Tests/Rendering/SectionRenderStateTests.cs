using OmniBlock.Client.Rendering.Chunks;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class SectionRenderStateTests
{
    [Fact]
    public void Repeated_requests_keep_the_oldest_age_and_highest_priority()
    {
        using var state = new SectionRenderState(new Vector3D<int>(16, 32, 48));

        state.RememberRequest(SectionDirtyReason.InitialTerrain, MeshWorkPriority.Background, 10);
        state.RememberRequest(SectionDirtyReason.StreamingBoundary, MeshWorkPriority.Foreground, 20);
        state.RememberRequest(SectionDirtyReason.Lighting, MeshWorkPriority.Background, 30);

        Assert.Equal(10, state.RequestedAt);
        Assert.Equal(MeshWorkPriority.Foreground, state.RequestedPriority);
        Assert.Equal(
            SectionDirtyReason.InitialTerrain |
            SectionDirtyReason.StreamingBoundary |
            SectionDirtyReason.Lighting,
            state.DirtyReasons);
    }

    [Fact]
    public void Completing_a_request_resets_only_request_metadata()
    {
        using var state = new SectionRenderState(new Vector3D<int>(16, 32, 48));
        var version = state.Version;
        state.RememberRequest(SectionDirtyReason.BlockChange, MeshWorkPriority.Critical, 10);

        state.ClearRequest();

        Assert.Same(version, state.Version);
        Assert.Equal(-1, state.RequestedAt);
        Assert.Equal(MeshWorkPriority.Background, state.RequestedPriority);
        Assert.Equal(SectionDirtyReason.None, state.DirtyReasons);
    }

    [Fact]
    public void Upload_timestamps_preserve_first_residency_and_track_latest_replacement()
    {
        using var state = new SectionRenderState(new Vector3D<int>(16, 32, 48));

        state.RecordUploaded(12);
        state.RecordUploaded(37);

        Assert.Equal(12, state.FirstUploadedAt);
        Assert.Equal(37, state.LastUploadedAt);
    }
}
