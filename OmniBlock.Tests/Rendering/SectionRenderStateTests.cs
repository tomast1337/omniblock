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

    [Fact]
    public void Deferred_invalidations_coalesce_without_mutating_the_active_request()
    {
        using var state = new SectionRenderState(new Vector3D<int>(16, 32, 48));
        state.RememberRequest(SectionDirtyReason.BlockChange, MeshWorkPriority.Critical, 20);

        state.DeferRequest(SectionDirtyReason.StreamingBoundary, 30);
        state.DeferRequest(SectionDirtyReason.StreamingBoundary, 40);

        Assert.Equal(SectionDirtyReason.BlockChange, state.DirtyReasons);
        Assert.Equal(20, state.RequestedAt);
        Assert.Equal(SectionDirtyReason.StreamingBoundary, state.DeferredDirtyReasons);
        Assert.Equal(30, state.DeferredAt);

        Assert.True(state.TryConsumeDeferredRequest(out var reasons, out var requestedAt));
        Assert.Equal(SectionDirtyReason.StreamingBoundary, reasons);
        Assert.Equal(30, requestedAt);
        Assert.Equal(SectionDirtyReason.None, state.DeferredDirtyReasons);
        Assert.Equal(-1, state.DeferredAt);
        Assert.Equal(SectionDirtyReason.BlockChange, state.DirtyReasons);
    }

    [Fact]
    public void Clearing_an_uploaded_request_preserves_a_later_deferred_invalidation()
    {
        using var state = new SectionRenderState(new Vector3D<int>(16, 32, 48));
        state.RememberRequest(SectionDirtyReason.InitialTerrain, MeshWorkPriority.Foreground, 10);
        state.DeferRequest(SectionDirtyReason.StreamingBoundary, 20);

        state.ClearRequest();

        Assert.Equal(SectionDirtyReason.None, state.DirtyReasons);
        Assert.Equal(SectionDirtyReason.StreamingBoundary, state.DeferredDirtyReasons);
        Assert.Equal(20, state.DeferredAt);
    }

    [Fact]
    public void Abandoning_a_removed_queue_entry_releases_its_pending_epoch()
    {
        using var state = new SectionRenderState(new Vector3D<int>(16, 32, 48));
        state.Version.MarkDirty();
        Assert.NotNull(state.Version.SnapshotIfNeeded());
        state.RememberRequest(SectionDirtyReason.InitialTerrain, MeshWorkPriority.Background, 10);

        state.AbandonRequest();

        Assert.Equal(-1, state.Version.State.Pending);
        Assert.Equal(-1, state.RequestedAt);
        Assert.Equal(SectionDirtyReason.None, state.DirtyReasons);
        Assert.NotNull(state.Version.SnapshotIfNeeded());
    }
}
