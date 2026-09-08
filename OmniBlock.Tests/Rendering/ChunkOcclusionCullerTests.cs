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
        using var camera = Node(0, sealedNeighbors: true);
        using var bridge = Node(16, sealedNeighbors: true);
        using var terrain = Node(32, sealedNeighbors: true);
        camera.AdjacentEast = bridge;
        bridge.AdjacentWest = camera;
        bridge.AdjacentEast = terrain;
        terrain.AdjacentWest = bridge;
        camera.VisibilityData.SetVisible(ChunkDirection.West, ChunkDirection.East);
        bridge.VisibilityData.SetVisible(ChunkDirection.West, ChunkDirection.East);

        var drawn = Find([camera, bridge, terrain], camera, true, new TestFrustum(bridge.BoundingBox));

        Assert.Contains(terrain, drawn);
        Assert.DoesNotContain(bridge, drawn);
        Assert.Equal(drawn.Count, drawn.Distinct().Count());
    }

    [Fact]
    public void New_incoming_face_reopens_a_previously_visited_mesh()
    {
        using var camera = Node(0, sealedNeighbors: true);
        using var junction = Node(16, sealedNeighbors: true);
        using var detour = Node(32, sealedNeighbors: true);
        using var returnPath = Node(64, sealedNeighbors: true);
        using var terrain = Node(48, sealedNeighbors: true);
        camera.AdjacentEast = junction;
        camera.AdjacentSouth = detour;
        detour.AdjacentEast = returnPath;
        returnPath.AdjacentDown = junction;
        junction.AdjacentEast = terrain;
        camera.VisibilityData.SetVisible(ChunkDirection.West, ChunkDirection.East);
        camera.VisibilityData.SetVisible(ChunkDirection.West, ChunkDirection.South);
        detour.VisibilityData.SetVisible(ChunkDirection.North, ChunkDirection.East);
        returnPath.VisibilityData.SetVisible(ChunkDirection.West, ChunkDirection.Down);
        junction.VisibilityData.SetVisible(ChunkDirection.Up, ChunkDirection.East);

        Assert.Contains(terrain, Find([camera, junction, detour, returnPath, terrain], camera, true));
    }

    [Fact]
    public void Enclosed_terrain_is_still_occluded()
    {
        using var camera = Node(0, sealedNeighbors: true);
        using var wall = Node(16, sealedNeighbors: true);
        using var terrain = Node(32, sealedNeighbors: true);
        camera.AdjacentEast = wall;
        wall.AdjacentEast = terrain;
        camera.VisibilityData.SetVisible(ChunkDirection.West, ChunkDirection.East);

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

    private static SubChunkRenderer Node(int x, bool sealedNeighbors = false)
    {
        var node = new SubChunkRenderer(new Vector3D<int>(x, 64, 0));
        // Self-links close unused graph edges, preventing missing-neighbor seeds in portal tests.
        if (sealedNeighbors)
            node.AdjacentDown = node.AdjacentUp = node.AdjacentNorth = node.AdjacentSouth =
                node.AdjacentWest = node.AdjacentEast = node;
        return node;
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
        public void SetPosition(double x, double y, double z) { }
    }
}
