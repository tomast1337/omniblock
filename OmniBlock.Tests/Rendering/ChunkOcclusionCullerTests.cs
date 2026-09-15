using OmniBlock.Client.Rendering;
using OmniBlock.Client.Rendering.Chunks;
using OmniBlock.Client.Rendering.Chunks.Occlusion;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;

namespace OmniBlock.Tests.Rendering;

public sealed class ChunkOcclusionCullerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Missing_camera_mesh_still_draws_available_terrain(bool occlusion)
    {
        using var terrain = Node(32);
        var drawn = Find([terrain], null, occlusion);
        Assert.Equal([terrain], drawn);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Gap_between_meshes_does_not_hide_finished_terrain(bool occlusion)
    {
        using var camera = Node(0);
        using var disconnected = Node(48);
        Assert.Equal(2, Find([camera, disconnected], camera, occlusion).Count);
    }

    [Fact]
    public void Traversal_can_cross_a_mesh_outside_the_frustum()
    {
        using var camera = Node(0, true);
        using var bridge = Node(16, true);
        using var terrain = Node(32, true);
        camera.AdjacentEast = bridge;
        bridge.AdjacentWest = camera;
        bridge.AdjacentEast = terrain;
        terrain.AdjacentWest = bridge;
        SetVisible(camera, ChunkDirection.West, ChunkDirection.East);
        SetVisible(bridge, ChunkDirection.West, ChunkDirection.East);

        var drawn = Find([camera, bridge, terrain], camera, true, new TestFrustum(bridge.BoundingBox));

        Assert.Contains(terrain, drawn);
        Assert.DoesNotContain(bridge, drawn);
        Assert.Equal(drawn.Count, drawn.Distinct().Count());
    }

    [Fact]
    public void New_incoming_face_reopens_a_previously_visited_mesh()
    {
        using var camera = Node(0, true);
        using var junction = Node(16, true);
        using var detour = Node(32, true);
        using var returnPath = Node(64, true);
        using var terrain = Node(48, true);
        camera.AdjacentEast = junction;
        camera.AdjacentSouth = detour;
        detour.AdjacentEast = returnPath;
        returnPath.AdjacentDown = junction;
        junction.AdjacentEast = terrain;
        SetVisible(camera, ChunkDirection.West, ChunkDirection.East);
        SetVisible(camera, ChunkDirection.West, ChunkDirection.South);
        SetVisible(detour, ChunkDirection.North, ChunkDirection.East);
        SetVisible(returnPath, ChunkDirection.West, ChunkDirection.Down);
        SetVisible(junction, ChunkDirection.Up, ChunkDirection.East);

        Assert.Contains(terrain, Find([camera, junction, detour, returnPath, terrain], camera, true));
    }

    [Fact]
    public void Enclosed_terrain_is_still_occluded()
    {
        using var camera = Node(0, true);
        using var wall = Node(16, true);
        using var terrain = Node(32, true);
        camera.AdjacentEast = wall;
        wall.AdjacentEast = terrain;
        SetVisible(camera, ChunkDirection.West, ChunkDirection.East);

        var drawn = Find([camera, wall, terrain], camera, true);

        Assert.Contains(wall, drawn);
        Assert.DoesNotContain(terrain, drawn);
    }

    [Fact]
    public void Draw_selection_obeys_frustum_and_distance_in_fallback()
    {
        using var visible = Node(16);
        using var rejected = Node(32);
        using var distant = Node(1024);
        Assert.Equal([visible], Find([visible, rejected, distant], null, false,
            new TestFrustum(rejected.BoundingBox)));
    }

    [Fact]
    public void Portal_connectivity_is_not_removed_by_camera_angle()
    {
        using var node = Node(0);
        foreach (var from in Enum.GetValues<ChunkDirection>())
        foreach (var to in Enum.GetValues<ChunkDirection>())
        {
            ChunkVisibilityStore store = new();
            store.SetVisible(from, to);
            var incoming = (ChunkDirectionMask)(1 << (int)from);
            var expected = (ChunkDirectionMask)(1 << (int)to);
            Assert.Equal(expected, store.GetVisibleFrom(incoming, new Vector3D<double>(100, 200, 300), node));
        }
    }

    [Fact]
    public void Occlusion_walk_does_not_visit_the_entire_resident_cache()
    {
        var nodes = Enumerable.Range(0, 128).Select(index => Node(index * 16)).ToArray();
        try
        {
            var frustum = new NearOriginFrustum();
            Find(nodes, nodes[0], true, frustum);

            // Every resident section is classified once. Only the few near the exact or expanded
            // frustum may be revisited by the portal walk; distant missing-neighbour sections must
            // not seed work of their own.
            Assert.InRange(frustum.Calls, nodes.Length, nodes.Length + 16);
        }
        finally
        {
            foreach (var node in nodes) node.Dispose();
        }
    }

    [Fact]
    public void Visibility_result_accounts_for_candidates_and_frustum_work()
    {
        using var visible = Node(0);
        using var rejected = Node(16);
        using var other = Node(32);
        var visitor = new Collector();

        var result = new ChunkOcclusionCuller().FindVisible(
            visitor,
            [visible, rejected, other],
            null,
            new Vector3D<double>(8, 72, 8),
            new TestFrustum(rejected.BoundingBox),
            256,
            false,
            1);

        Assert.Equal(3, result.ResidentCandidates);
        Assert.Equal(3, result.FrustumTests);
        Assert.Equal(2, result.FrustumCandidates);
        Assert.Equal(0, result.PortalVisited);
        Assert.Equal([visible, other], visitor.Nodes);
    }

    [Fact]
    public void Disconnected_rescue_seeds_are_bounded_and_reported()
    {
        using var camera = Node(0, true);
        using var disconnected = Node(48);
        var visitor = new Collector();

        var result = new ChunkOcclusionCuller().FindVisible(
            visitor,
            [camera, disconnected],
            camera,
            new Vector3D<double>(8, 72, 8),
            new TestFrustum(),
            256,
            true,
            1,
            candidatesKnownInFrustum: true);

        Assert.Equal(1, result.DisconnectedSeeds);
        Assert.InRange(result.DisconnectedSeeds, 0, result.ResidentCandidates);
        Assert.Contains(disconnected, visitor.Nodes);
    }

    [Fact]
    public void Portal_diagnostics_distinguish_successful_and_duplicate_edges()
    {
        using var camera = Node(0, true);
        using var east = Node(16, true);
        camera.AdjacentEast = east;
        east.AdjacentWest = camera;
        SetVisible(camera, ChunkDirection.West, ChunkDirection.East);
        SetVisible(east, ChunkDirection.West, ChunkDirection.West);
        var visitor = new Collector();

        var result = new ChunkOcclusionCuller().FindVisible(
            visitor,
            [camera, east],
            camera,
            new Vector3D<double>(8, 72, 8),
            new TestFrustum(),
            256,
            true,
            1,
            candidatesKnownInFrustum: true);

        Assert.Equal(2, result.PortalVisited);
        Assert.Equal(2, result.PortalQueuePops);
        Assert.Equal(2, result.PortalDrawFrustumTests);
        Assert.Equal(2, result.PortalEdgeAttempts);
        Assert.Equal(0, result.PortalMissingNeighbors);
        Assert.Equal(2, result.PortalMarginFrustumTests);
        Assert.Equal(0, result.PortalMarginRejected);
        Assert.Equal(1, result.PortalSuccessfulReaches);
        Assert.Equal(1, result.PortalDuplicateReaches);
        Assert.Equal(
            result.PortalDrawFrustumTests + result.PortalMarginFrustumTests,
            result.FrustumTests);
    }

    [Fact]
    public void Complete_near_field_graph_needs_no_rescue_after_install_grace()
    {
        using var state = ResidentState(frame: 10, sealedNeighbors: true);

        Assert.Equal(
            NearFieldRescueReason.NewPresentation,
            ChunkRenderer.GetNearFieldRescueReasons(state, 11));
        Assert.Equal(
            NearFieldRescueReason.None,
            ChunkRenderer.GetNearFieldRescueReasons(state, 12));
    }

    [Fact]
    public void Missing_adjacency_and_recent_graph_regression_are_explicit_rescue_reasons()
    {
        using var state = ResidentState(frame: 1, sealedNeighbors: false);
        state.NoteAdjacencyChanged(20);
        state.Renderer!.LastVisibleFrame = 20;

        var reasons = ChunkRenderer.GetNearFieldRescueReasons(state, 21);

        Assert.True((reasons & NearFieldRescueReason.IncompleteAdjacency) != 0);
        Assert.True((reasons & NearFieldRescueReason.PresentationRegression) != 0);
        Assert.True((reasons & NearFieldRescueReason.NewPresentation) == 0);
    }

    [Fact]
    public void World_vertical_boundaries_do_not_require_out_of_world_neighbors()
    {
        using var bottom = new SubChunkRenderer(new Vector3D<int>(0, 0, 0));
        bottom.AdjacentUp = bottom.AdjacentNorth = bottom.AdjacentSouth =
            bottom.AdjacentWest = bottom.AdjacentEast = bottom;
        Assert.True(ChunkRenderer.HasCompleteAdjacency(bottom));

        using var top = new SubChunkRenderer(new Vector3D<int>(0, 112, 0));
        top.AdjacentDown = top.AdjacentNorth = top.AdjacentSouth =
            top.AdjacentWest = top.AdjacentEast = top;
        Assert.True(ChunkRenderer.HasCompleteAdjacency(top));
    }

    private static SectionRenderState ResidentState(int frame, bool sealedNeighbors)
    {
        var state = new SectionRenderState(new Vector3D<int>(0, 64, 0));
        state.Version.MarkDirty();
        var epoch = state.Version.SnapshotIfNeeded()!.Value;
        var renderer = Node(0, sealedNeighbors);
        state.CommitPresentation(renderer, SectionPresentation.MetadataOnly(epoch));
        state.NotePresentationInstalled(frame);
        return state;
    }

    private static SubChunkRenderer Node(int x, bool sealedNeighbors = false)
    {
        var node = new SubChunkRenderer(new Vector3D<int>(x, 64, 0));
        // Self-links close unused graph edges, preventing missing-neighbor seeds in portal tests.
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

    private static List<SubChunkRenderer> Find(SubChunkRenderer[] nodes, SubChunkRenderer? camera,
        bool occlusion, ICuller? frustum = null)
    {
        var visitor = new Collector();
        new ChunkOcclusionCuller().FindVisible(visitor, nodes, camera, new Vector3D<double>(8, 72, 8),
            frustum ?? new TestFrustum(), 256, occlusion, 1);
        return visitor.Nodes;
    }

    private sealed class Collector : IChunkVisibilityVisitor
    {
        public List<SubChunkRenderer> Nodes { get; } = [];
        public void Visit(SubChunkRenderer renderer) => Nodes.Add(renderer);
    }

    private sealed class TestFrustum(Box? rejected = null) : ICuller
    {
        public bool IsBoundingBoxInFrustum(Box aabb) => !rejected.HasValue || !aabb.Equals(rejected.Value);

        public void SetPosition(double x, double y, double z)
        {
        }
    }

    private sealed class NearOriginFrustum : ICuller
    {
        public int Calls { get; private set; }

        public bool IsBoundingBoxInFrustum(Box aabb)
        {
            Calls++;
            return aabb.MinX < 48;
        }

        public void SetPosition(double x, double y, double z)
        {
        }
    }
}
