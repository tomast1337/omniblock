using OmniBlock.Client.Rendering.Entities;

namespace OmniBlock.Tests.Rendering;

public sealed class EntityInstanceBatchRendererTests
{
    [Fact]
    public void Static_mesh_is_reused_only_while_the_staged_geometry_size_matches()
    {
        Assert.True(EntityInstanceBatchRenderer.NeedsStaticMeshUpload(12_672, null));
        Assert.False(EntityInstanceBatchRenderer.NeedsStaticMeshUpload(12_672, 12_672));
        Assert.True(EntityInstanceBatchRenderer.NeedsStaticMeshUpload(17_100, 12_672));
    }
}
