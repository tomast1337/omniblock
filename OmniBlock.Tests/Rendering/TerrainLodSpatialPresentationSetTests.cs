using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodSpatialPresentationSetTests
{
    private static readonly TerrainLodSpatialPolicy RefiningPolicy = new(
        distanceUnitChunks: 1000,
        distanceGrowth: 2,
        horizontalSampleLevelBySpatialLevel: [0, 0],
        verticalSliceBudgetBySpatialLevel: [16, 8]);

    [Fact]
    public void Failed_candidate_keeps_the_published_predecessor()
    {
        var key = new TerrainLodTileKey(0, 2, -3);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        var predecessor = new FakePresentation("old");
        Assert.True(presentations.TryInstall(key, "old", () => predecessor, out var firstFailure));
        Assert.Null(firstFailure);

        Assert.False(presentations.TryInstall(
            key, "new", static () => throw new InvalidOperationException("upload failed"),
            out var failure));
        var frame = presentations.Update(
            key, 2, -3, new TerrainLodSpatialPolicy(8, 2, [0], [16]),
            1, fadeEnabled: false);

        Assert.IsType<InvalidOperationException>(failure);
        Assert.Single(frame.Draws);
        Assert.Same(predecessor, frame.Draws[0].Presentation);
        Assert.False(predecessor.Disposed);
    }

    [Fact]
    public void Successful_candidate_is_published_before_predecessor_retires()
    {
        var key = new TerrainLodTileKey(0, 0, 0);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        var old = new FakePresentation("old");
        var replacement = new FakePresentation("replacement");
        Assert.True(presentations.TryInstall(key, "old", () => old, out _));

        Assert.True(presentations.TryInstall(key, "replacement", () => replacement, out var failure));
        var frame = presentations.Update(
            key, 0, 0, new TerrainLodSpatialPolicy(8, 2, [0], [16]),
            1, fadeEnabled: false);

        Assert.Null(failure);
        Assert.True(old.Disposed);
        Assert.False(replacement.Disposed);
        Assert.Same(replacement, Assert.Single(frame.Draws).Presentation);
    }

    [Fact]
    public void Partial_child_quartet_cannot_replace_ready_parent()
    {
        var root = new TerrainLodTileKey(1, 0, 0);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        Install(presentations, root);
        for (var index = 0; index < 3; index++) Install(presentations, root.Child(index));

        var frame = presentations.Update(
            root, 1, 1, RefiningPolicy, 1, fadeEnabled: true);

        Assert.True(frame.CompleteCoverage);
        Assert.False(frame.Transitioning);
        Assert.Equal(root, Assert.Single(frame.Draws).Selection.Tile);
    }

    [Fact]
    public void Complete_child_quartet_fades_as_one_partition()
    {
        var root = new TerrainLodTileKey(1, 0, 0);
        using var presentations = new TerrainLodSpatialPresentationSet<FakePresentation>();
        Install(presentations, root);
        var initial = presentations.Update(
            root, 1, 1, RefiningPolicy, 0, fadeEnabled: true);
        Assert.Equal(root, Assert.Single(initial.Draws).Selection.Tile);

        for (var index = 0; index < 4; index++) Install(presentations, root.Child(index));
        var transition = presentations.Update(
            root, 1, 1, RefiningPolicy,
            TerrainLodSpatialPresentationSet<FakePresentation>.TransitionDurationSeconds / 2,
            fadeEnabled: true);

        Assert.True(transition.CompleteCoverage);
        Assert.True(transition.Transitioning);
        Assert.Equal(5, transition.Draws.Count);
        Assert.Equal(root, transition.Draws[0].Selection.Tile);
        Assert.Equal(2u, transition.Draws[0].Fade.Mode);
        Assert.All(transition.Draws.Skip(1), draw =>
        {
            Assert.Equal(0, draw.Selection.Tile.Level);
            Assert.Equal(1u, draw.Fade.Mode);
            Assert.Equal(transition.Draws[0].Fade.Progress, draw.Fade.Progress);
            Assert.Equal(transition.Draws[0].Fade.Seed, draw.Fade.Seed);
        });

        var completed = presentations.Update(
            root, 1, 1, RefiningPolicy,
            TerrainLodSpatialPresentationSet<FakePresentation>.TransitionDurationSeconds,
            fadeEnabled: true);
        Assert.False(completed.Transitioning);
        Assert.Equal(4, completed.Draws.Count);
        Assert.DoesNotContain(completed.Draws, draw => draw.Selection.Tile == root);
        Assert.All(completed.Draws, draw => Assert.Equal(0u, draw.Fade.Mode));
    }

    private static void Install(
        TerrainLodSpatialPresentationSet<FakePresentation> presentations,
        TerrainLodTileKey key)
    {
        Assert.True(presentations.TryInstall(
            key, key.ToString(), () => new FakePresentation(key.ToString()), out var failure));
        Assert.Null(failure);
    }

    private sealed class FakePresentation(string name) : IDisposable
    {
        public string Name { get; } = name;
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
