using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Chunks.Occlusion;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class SectionPresentationTests
{
    [Fact]
    public void Installing_a_presentation_swaps_all_mesh_metadata_together()
    {
        using var renderer = new SubChunkRenderer(new Vector3D<int>(16, 32, 48));
        var oldVisibility = Visible(ChunkDirection.West, ChunkDirection.East);
        var previous = SectionPresentation.MetadataOnly(
            epoch: 3,
            visibilityData: oldVisibility,
            isLit: false,
            solidVertexCount: 4);
        renderer.InstallPresentation(previous);

        var newVisibility = Visible(ChunkDirection.North, ChunkDirection.South);
        var replacement = SectionPresentation.MetadataOnly(
            epoch: 4,
            visibilityData: newVisibility,
            isLit: true,
            solidVertexCount: 8,
            translucentVertexCount: 4);

        renderer.InstallPresentation(replacement);

        Assert.True(previous.IsDisposed);
        Assert.Same(replacement, renderer.Presentation);
        Assert.Equal(4, renderer.PresentedEpoch);
        Assert.True(renderer.IsLit);
        Assert.Equal(8 * 24, renderer.SolidMeshSizeBytes);
        Assert.Equal(4 * 24, renderer.TranslucentMeshSizeBytes);
        Assert.True(renderer.HasTranslucentMesh);
        Assert.Equal(
            ChunkDirectionMask.South,
            renderer.VisibilityData.GetVisibleFrom(
                ChunkDirectionMask.North,
                default,
                renderer));
    }

    [Fact]
    public void Rejecting_an_older_presentation_preserves_the_installed_snapshot()
    {
        using var renderer = new SubChunkRenderer(default);
        var installed = SectionPresentation.MetadataOnly(epoch: 7, isLit: true);
        renderer.InstallPresentation(installed);
        using var older = SectionPresentation.MetadataOnly(epoch: 6, isLit: false);

        Assert.Throws<InvalidOperationException>(() => renderer.InstallPresentation(older));

        Assert.Same(installed, renderer.Presentation);
        Assert.Equal(7, renderer.PresentedEpoch);
        Assert.True(renderer.IsLit);
        Assert.False(installed.IsDisposed);
    }

    [Fact]
    public void Section_commit_publishes_the_presentation_and_epoch_together()
    {
        using var state = new SectionRenderState(new Vector3D<int>(16, 32, 48));
        var renderer = new SubChunkRenderer(state.Position);
        state.Version.MarkDirty();
        var epoch = state.Version.SnapshotIfNeeded();
        Assert.NotNull(epoch);
        var presentation = SectionPresentation.MetadataOnly(epoch.Value, isLit: true);

        state.CommitPresentation(renderer, presentation);

        Assert.Same(renderer, state.Renderer);
        Assert.Same(presentation, renderer.Presentation);
        Assert.Equal(epoch.Value, renderer.PresentedEpoch);
        Assert.Equal(epoch.Value, state.Version.State.LastMeshed);
        Assert.Equal(-1, state.Version.State.Pending);
        Assert.True(state.IsLit);
    }

    [Fact]
    public void Invalid_epoch_does_not_publish_a_candidate()
    {
        using var state = new SectionRenderState(default);
        using var renderer = new SubChunkRenderer(default);
        state.Version.MarkDirty();
        var pending = state.Version.SnapshotIfNeeded();
        Assert.NotNull(pending);
        var lastMeshed = state.Version.State.LastMeshed;
        using var candidate = SectionPresentation.MetadataOnly(pending.Value + 1);

        Assert.Throws<InvalidOperationException>(() => state.CommitPresentation(renderer, candidate));

        Assert.Null(state.Renderer);
        Assert.Null(renderer.Presentation);
        Assert.Equal(pending.Value, state.Version.State.Pending);
        Assert.Equal(lastMeshed, state.Version.State.LastMeshed);
    }

    private static ChunkVisibilityStore Visible(ChunkDirection from, ChunkDirection to)
    {
        ChunkVisibilityStore visibility = new();
        visibility.SetVisible(from, to);
        return visibility;
    }
}
