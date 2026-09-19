using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodSpatialPublicationTests
{
    private static readonly TerrainLodTileSelection Selection = new(new(0, 0, 0), 0, 16);
    private static readonly TerrainLodSpatialSeamSegment Edge =
        TerrainLodSpatialSeamPlanner.Plan([Selection])[0];

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Incomplete_replacement_preserves_displayed_bodies_and_seams(bool bodiesReady, bool seamsReady)
    {
        using var publication = new TerrainLodSpatialPublication<Resource, Resource>();
        var body = new Resource();
        var seam = new Resource();
        var displayed = Frame(body);
        Assert.True(publication.TryPublish(displayed, Seams(seam), true));
        // Catalog ownership can end before the next partition is ready.
        body.Dispose();
        seam.Dispose();
        using var candidateBody = new Resource();
        using var candidateSeam = new Resource();

        Assert.False(publication.TryPublish(Frame(candidateBody, bodiesReady), Seams(candidateSeam), seamsReady));
        Assert.Same(displayed, publication.Frame);
        Assert.Same(seam, publication.Seams[Edge].Presentation);
        Assert.Equal(0, body.Disposals);
        Assert.Equal(0, seam.Disposals);

        publication.Dispose();
        publication.Dispose();
        Assert.Equal(1, body.Disposals);
        Assert.Equal(1, seam.Disposals);
    }

    [Fact]
    public void Complete_replacement_retires_previous_snapshot_only_after_publication()
    {
        using var publication = new TerrainLodSpatialPublication<Resource, Resource>();
        using var nextBody = new Resource();
        using var nextSeam = new Resource();
        var nextFrame = Frame(nextBody);
        var oldBody = new Resource(() => Assert.Same(nextFrame, publication.Frame));
        var oldSeam = new Resource(() => Assert.Same(nextSeam, publication.Seams[Edge].Presentation));
        publication.TryPublish(Frame(oldBody), Seams(oldSeam), true);
        oldBody.Dispose();
        oldSeam.Dispose();

        Assert.True(publication.TryPublish(nextFrame, Seams(nextSeam), true));
        Assert.Equal(1, oldBody.Disposals);
        Assert.Equal(1, oldSeam.Disposals);
        Assert.Equal(0, nextBody.Disposals);
        Assert.Equal(0, nextSeam.Disposals);
    }

    [Fact]
    public void Catalog_replacement_cannot_free_a_body_used_by_the_displayed_snapshot()
    {
        using var catalog = new TerrainLodSpatialPresentationSet<Resource>();
        using var publication = new TerrainLodSpatialPublication<Resource, Resource>();
        using var seam = new Resource();
        var oldBody = new Resource();
        catalog.TryInstall(Selection.Tile, "old", () => oldBody, out _);
        var policy = new TerrainLodSpatialPolicy(8, 2, [0], [16]);
        var displayed = catalog.Update(Selection.Tile, 0, 0, policy, 1, fadeEnabled: false);
        publication.TryPublish(displayed, Seams(seam), true);
        var nextBody = new Resource();

        Assert.True(catalog.TryInstall(Selection.Tile, "new", () => nextBody, out _));
        Assert.Equal(0, oldBody.Disposals);
        var nextFrame = catalog.Update(Selection.Tile, 0, 0, policy, 1, fadeEnabled: false);
        Assert.False(publication.TryPublish(nextFrame, Seams(seam), seamsReady: false));
        Assert.Same(oldBody, Assert.Single(publication.Frame!.Draws).Presentation);
        Assert.True(publication.TryPublish(nextFrame, Seams(seam), seamsReady: true));
        Assert.Equal(1, oldBody.Disposals);
        Assert.Same(nextBody, Assert.Single(publication.Frame!.Draws).Presentation);
    }

    [Fact]
    public void Failed_acquisition_rolls_back_candidate_leases_without_changing_displayed_snapshot()
    {
        using var publication = new TerrainLodSpatialPublication<Resource, Resource>();
        using var oldBody = new Resource();
        using var oldSeam = new Resource();
        var displayed = Frame(oldBody);
        publication.TryPublish(displayed, Seams(oldSeam), true);
        var candidateBody = new Resource();
        var deadSeam = new Resource();
        deadSeam.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            publication.TryPublish(Frame(candidateBody), Seams(deadSeam), true));
        Assert.Same(displayed, publication.Frame);
        Assert.Same(oldSeam, publication.Seams[Edge].Presentation);
        candidateBody.Dispose();
        Assert.Equal(1, candidateBody.Disposals);
    }

    [Fact]
    public void Shared_resources_survive_successive_frames_without_duplicate_disposal()
    {
        using var publication = new TerrainLodSpatialPublication<Resource, Resource>();
        var shared = new Resource();
        publication.TryPublish(Frame(shared), Seams(shared), true);
        shared.Dispose();
        Assert.True(publication.TryPublish(Frame(shared), Seams(shared), true));
        Assert.Equal(0, shared.Disposals);
        publication.Dispose();
        Assert.Equal(1, shared.Disposals);
        Assert.Throws<ObjectDisposedException>(() => shared.Retain());
    }

    private static TerrainLodSpatialPresentationFrame<Resource> Frame(Resource body, bool complete = true) =>
        new([new(Selection, body, new(1, 0, 0))], complete, false);

    private static Dictionary<TerrainLodSpatialSeamSegment, PublishedTerrainSeam<Resource>> Seams(Resource seam) =>
        new() { [Edge] = new(seam, new(1, 0, 0)) };

    private sealed class Resource(Action? onDispose = null) : RetainedTerrainResource
    {
        public int Disposals { get; private set; }
        protected override void DisposeResources()
        {
            Disposals++;
            onDispose?.Invoke();
        }
    }
}
