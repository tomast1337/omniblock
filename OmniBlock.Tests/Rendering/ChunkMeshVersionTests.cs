using OmniBlock.Util;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkMeshVersionTests
{
    [Fact]
    public void Queued_revision_is_replaced_by_the_latest_epoch_without_completing_the_old_one()
    {
        var version = ChunkMeshVersion.Get();
        try
        {
            version.MarkDirty();
            Assert.Equal(1, version.SnapshotIfNeeded());
            version.MarkDirty();
            Assert.Equal(2, version.ReplaceQueuedSnapshotWithLatest());
            version.CompleteMesh(1);
            Assert.Equal((2, 0, 2), version.State);
            version.CompleteMesh(2);
            Assert.Equal((2, 2, -1), version.State);
        }
        finally
        {
            version.Release();
        }
    }

    [Fact]
    public void Cancelled_revision_releases_pending_without_claiming_completion()
    {
        var version = ChunkMeshVersion.Get();
        try
        {
            version.MarkDirty();
            Assert.Equal(1, version.SnapshotIfNeeded());
            version.CancelMesh(1);
            Assert.Equal((1, 0, -1), version.State);
            Assert.Equal(1, version.SnapshotIfNeeded());
        }
        finally
        {
            version.Release();
        }
    }

    [Fact]
    public void Abandoned_request_can_snapshot_the_unmeshed_epoch_again()
    {
        var version = ChunkMeshVersion.Get();
        try
        {
            version.MarkDirty();
            Assert.Equal(1, version.SnapshotIfNeeded());
            Assert.Null(version.SnapshotIfNeeded());

            version.AbandonPendingMesh();

            Assert.Equal(1, version.SnapshotIfNeeded());
            version.CompleteMesh(1);
            Assert.False(version.IsModified());
        }
        finally
        {
            version.Release();
        }
    }
}
