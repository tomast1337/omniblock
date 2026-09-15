using OmniBlock.Client.Rendering;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class ResidentSectionSpatialIndexTests
{
    [Fact]
    public void Membership_and_layer_summaries_follow_replacement_and_eviction()
    {
        var index = new ResidentSectionSpatialIndex();
        using var renderer = Node(0, 64, 0, solid: true);

        index.AddOrUpdate(renderer);
        Assert.Equal(1, index.Count);
        Assert.Equal(1, index.SolidLayerCount);
        Assert.Equal(0, index.TranslucentLayerCount);
        Assert.True(index.Contains(renderer));

        renderer.InstallPresentation(SectionPresentation.MetadataOnly(
            epoch: 1,
            translucentVertexCount: 4));
        index.AddOrUpdate(renderer);

        Assert.Equal(
            new SpatialLayerSummary(1, 0, 1, 1, 0, 1),
            index.GetSummary(renderer.Position));
        Assert.Equal(0, index.SolidLayerCount);
        Assert.Equal(1, index.TranslucentLayerCount);

        Assert.True(index.Remove(renderer));
        Assert.Equal(0, index.Count);
        Assert.Equal(0, index.RegionCount);
        Assert.False(index.Contains(renderer));
    }

    [Fact]
    public void Query_rejects_distant_regions_before_columns_and_sections()
    {
        var index = new ResidentSectionSpatialIndex();
        using var near = Node(0, 64, 0);
        using var far = Node(16 * 40, 64, 0);
        index.AddOrUpdate(near);
        index.AddOrUpdate(far);
        List<SubChunkRenderer> candidates = [];

        var diagnostics = index.Query(
            new IntersectingFrustum(new Box(-32, 0, -32, 64, 128, 64)),
            new Vector3D<double>(8, 72, 8),
            256,
            candidates);

        Assert.Equal([near], candidates);
        Assert.Equal(2, diagnostics.RegionTests);
        Assert.Equal(1, diagnostics.ColumnTests);
        Assert.Equal(1, diagnostics.SectionTests);
    }

    [Fact]
    public void Query_orders_near_to_far_with_coordinate_tie_breakers()
    {
        var index = new ResidentSectionSpatialIndex();
        using var east = Node(16, 64, 0);
        using var west = Node(-16, 64, 0);
        using var farther = Node(48, 64, 0);
        index.AddOrUpdate(east);
        index.AddOrUpdate(farther);
        index.AddOrUpdate(west);
        List<SubChunkRenderer> candidates = [];

        index.Query(new IntersectingFrustum(), new Vector3D<double>(8, 72, 8), 256, candidates);

        Assert.Equal([west, east, farther], candidates);
    }

    [Fact]
    public void Query_can_skip_ordering_for_order_independent_opaque_visibility()
    {
        var index = new ResidentSectionSpatialIndex();
        using var first = Node(48, 64, 0);
        using var second = Node(0, 64, 0);
        index.AddOrUpdate(first);
        index.AddOrUpdate(second);
        List<SubChunkRenderer> candidates = [];

        var diagnostics = index.Query(
            new IntersectingFrustum(),
            new Vector3D<double>(8, 72, 8),
            256,
            candidates,
            orderNearToFar: false);

        Assert.Equal(0, diagnostics.SortComparisons);
        Assert.Equal(0, diagnostics.SortMs);
        Assert.Equal(2, diagnostics.Candidates);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Indexed_candidates_match_the_full_scan_conservative_result(bool useOcclusion)
    {
        using var camera = Node(0, 64, 0, sealedNeighbors: true);
        using var east = Node(16, 64, 0, sealedNeighbors: true);
        using var northeast = Node(16, 64, -16, sealedNeighbors: true);
        using var distant = Node(16 * 24, 64, 0, sealedNeighbors: true);
        camera.AdjacentEast = east;
        east.AdjacentWest = camera;
        east.AdjacentNorth = northeast;
        northeast.AdjacentSouth = east;
        SetVisible(camera, ChunkDirection.West, ChunkDirection.East);
        SetVisible(east, ChunkDirection.West, ChunkDirection.North);
        SubChunkRenderer[] all = [camera, east, northeast, distant];
        var frustum = new IntersectingFrustum(new Box(-32, 0, -48, 80, 128, 48));

        var full = Find(all, camera, frustum, useOcclusion, frame: 1, knownInFrustum: false);

        var index = new ResidentSectionSpatialIndex();
        foreach (var renderer in all) index.AddOrUpdate(renderer);
        List<SubChunkRenderer> candidates = [];
        index.Query(frustum, new Vector3D<double>(8, 72, 8), 256, candidates);
        var indexed = Find([.. candidates], camera, frustum, useOcclusion, frame: 2, knownInFrustum: true);

        Assert.Equal(
            full.Select(static renderer =>
                    (renderer.Position.X, renderer.Position.Y, renderer.Position.Z))
                .Order().ToArray(),
            indexed.Select(static renderer =>
                    (renderer.Position.X, renderer.Position.Y, renderer.Position.Z))
                .Order().ToArray());
    }

    [Fact]
    public void Query_reports_frustum_candidates_sorted_beyond_render_distance()
    {
        var index = new ResidentSectionSpatialIndex();
        using var near = Node(0, 64, 0);
        using var outside = Node(16 * 12, 64, 0);
        index.AddOrUpdate(outside);
        index.AddOrUpdate(near);
        List<SubChunkRenderer> candidates = [];

        var diagnostics = index.Query(
            new IntersectingFrustum(),
            new Vector3D<double>(8, 72, 8),
            4 * 16,
            candidates);

        Assert.Equal(2, diagnostics.Candidates);
        Assert.Equal(1, diagnostics.OutsideRenderDistance);
        Assert.True(diagnostics.SortComparisons > 0);
        Assert.True(diagnostics.CullMs >= 0);
        Assert.True(diagnostics.SortMs >= 0);
    }

    private static List<SubChunkRenderer> Find(
        SubChunkRenderer[] nodes,
        SubChunkRenderer camera,
        ICuller frustum,
        bool occlusion,
        int frame,
        bool knownInFrustum)
    {
        var visitor = new Collector();
        new SectionVisibilityGraph().FindVisible(
            visitor, nodes, camera, new Vector3D<double>(8, 72, 8), frustum, 256,
            occlusion, frame, knownInFrustum);
        return visitor.Nodes;
    }

    private static SubChunkRenderer Node(
        int x,
        int y,
        int z,
        bool solid = false,
        bool sealedNeighbors = false)
    {
        var node = new SubChunkRenderer(new Vector3D<int>(x, y, z));
        node.InstallPresentation(SectionPresentation.MetadataOnly(
            solidVertexCount: solid ? 4 : 0));
        if (sealedNeighbors)
        {
            node.AdjacentDown = node.AdjacentUp = node.AdjacentNorth = node.AdjacentSouth =
                node.AdjacentWest = node.AdjacentEast = node;
        }
        return node;
    }

    private static void SetVisible(
        SubChunkRenderer renderer,
        ChunkDirection from,
        ChunkDirection to)
    {
        var visibility = renderer.VisibilityData;
        visibility.SetVisible(from, to);
        renderer.InstallPresentation(SectionPresentation.MetadataOnly(
            renderer.PresentedEpoch + 1,
            visibility));
    }

    private sealed class Collector : IChunkVisibilityVisitor
    {
        public List<SubChunkRenderer> Nodes { get; } = [];
        public void Visit(SubChunkRenderer renderer) => Nodes.Add(renderer);
    }

    private sealed class IntersectingFrustum(Box? visible = null) : ICuller
    {
        public bool IsBoundingBoxInFrustum(Box aabb) => !visible.HasValue || aabb.Intersects(visible.Value);
        public void SetPosition(double x, double y, double z)
        {
        }
    }
}
