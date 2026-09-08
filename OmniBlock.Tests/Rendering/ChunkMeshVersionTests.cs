using OmniBlock.Util;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkMeshVersionTests
{
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
