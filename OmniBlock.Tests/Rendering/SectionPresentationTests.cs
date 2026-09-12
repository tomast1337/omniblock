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
        Assert.True(renderer.HasSolidGeometry);
        Assert.True(renderer.HasTranslucentGeometry);
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

    [Theory]
    [InlineData(0, 0, false, false)]
    [InlineData(4, 0, true, false)]
    [InlineData(0, 4, false, true)]
    [InlineData(4, 4, true, true)]
    public void Published_geometry_presence_is_layer_specific_and_immutable(
        int solidVertices,
        int translucentVertices,
        bool hasSolid,
        bool hasTranslucent)
    {
        using var presentation = SectionPresentation.MetadataOnly(
            solidVertexCount: solidVertices,
            translucentVertexCount: translucentVertices);

        Assert.Equal(hasSolid, presentation.HasSolidGeometry);
        Assert.Equal(hasTranslucent, presentation.HasTranslucentGeometry);
        Assert.Equal(!hasSolid && !hasTranslucent, presentation.IsEmpty);
    }

    [Fact]
    public void Replacing_geometry_with_empty_presentation_removes_both_layers_atomically()
    {
        using var renderer = new SubChunkRenderer(default);
        var geometry = SectionPresentation.MetadataOnly(
            epoch: 1,
            solidVertexCount: 4,
            translucentVertexCount: 4);
        renderer.InstallPresentation(geometry);

        var emptyVisibility = Visible(ChunkDirection.Up, ChunkDirection.Down);
        var empty = SectionPresentation.MetadataOnly(epoch: 2, visibilityData: emptyVisibility);
        renderer.InstallPresentation(empty);

        Assert.True(geometry.IsDisposed);
        Assert.False(renderer.HasSolidGeometry);
        Assert.False(renderer.HasTranslucentGeometry);
        Assert.Same(empty, renderer.Presentation);
        Assert.Equal(
            ChunkDirectionMask.Down,
            renderer.VisibilityData.GetVisibleFrom(ChunkDirectionMask.Up, default, renderer));
    }

    [Fact]
    public void Visible_sections_are_classified_without_dropping_empty_traversal_nodes()
    {
        using var empty = RendererWithGeometry(0, 0);
        using var solidOnly = RendererWithGeometry(4, 0);
        using var translucentOnly = RendererWithGeometry(0, 4);
        using var both = RendererWithGeometry(4, 4);
        SubChunkRenderer[] visible = [empty, solidOnly, translucentOnly, both];
        List<SubChunkRenderer> solid = [];
        List<SubChunkRenderer> translucent = [];

        ChunkRenderer.BuildLayerVisibleLists(visible, solid, translucent);

        Assert.Equal([solidOnly, both], solid);
        Assert.Equal([translucentOnly, both], translucent);
        Assert.Contains(empty, visible);
    }

    private static SubChunkRenderer RendererWithGeometry(int solidVertices, int translucentVertices)
    {
        var renderer = new SubChunkRenderer(default);
        renderer.InstallPresentation(SectionPresentation.MetadataOnly(
            solidVertexCount: solidVertices,
            translucentVertexCount: translucentVertices));
        return renderer;
    }

    private static ChunkVisibilityStore Visible(ChunkDirection from, ChunkDirection to)
    {
        ChunkVisibilityStore visibility = new();
        visibility.SetVisible(from, to);
        return visibility;
    }
}
