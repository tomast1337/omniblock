using System.Numerics;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodStairTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Stair_surface_has_correct_orientation_volume_and_no_internal_faces(byte metadata)
    {
        var faces = TerrainLodStairGeometry.Get(metadata).ToArray();
        Assert.Equal(22, faces.Length);
        float volume = 0;
        foreach (var face in faces)
        {
            Vector3 a = V(face.A), b = V(face.B), c = V(face.C), d = V(face.D);
            var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            var center = (a + b + c + d) / 4;
            Assert.True(Occupied(center - normal * .01f));
            Assert.False(Occupied(center + normal * .01f));
            Assert.Equal(.25f, Vector3.Cross(b - a, c - a).Length());
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6;
            volume += Vector3.Dot(a, Vector3.Cross(c, d)) / 6;
            foreach (var point in new[] { face.A, face.B, face.C, face.D })
            {
                var uv = TerrainLodShapeGeometry.Uv(face.Side, point);
                Assert.InRange(uv.U, 0, 1);
                Assert.InRange(uv.V, 0, 1);
            }
        }
        Assert.InRange(volume, .74999f, .75001f);

        bool Occupied(Vector3 p)
        {
            if (p.X < 0 || p.X > 1 || p.Y < 0 || p.Y > 1 || p.Z < 0 || p.Z > 1)
                return false;
            if ((metadata & 4) == 0 ? p.Y < .5f : p.Y > .5f) return true;
            return (metadata & 3) switch
            {
                0 => p.X > .5f, 1 => p.X < .5f, 2 => p.Z > .5f, _ => p.Z < .5f
            };
        }
        static Vector3 V((float X, float Y, float Z) p) => new(p.X, p.Y, p.Z);
    }

    [Theory]
    [InlineData("wooden_stairs", 0)]
    [InlineData("wooden_stairs", 1)]
    [InlineData("wooden_stairs", 2)]
    [InlineData("wooden_stairs", 3)]
    [InlineData("cobblestone_stairs", 4)]
    [InlineData("cobblestone_stairs", 5)]
    [InlineData("cobblestone_stairs", 6)]
    [InlineData("cobblestone_stairs", 7)]
    public void Local_and_streamed_stairs_agree_across_chunk_and_page_edges(string name, byte metadata)
    {
        var world = new FakeWorldContext();
        var block = world.Content.Blocks.Get("omniblock:" + name);
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var material = materials.Resolve(block.Id, metadata);
        Assert.Equal(TerrainLodGeometryClass.Stairs, material.Geometry);
        Assert.False(material.OccludesFaces);
        world.Writer.SetBlock(4, 8, 4, block.Id, metadata);
        List<Box> collision = [];
        block.AddIntersectingBoundingBox(world.Reader, world.Entities, 4, 8, 4,
            new Box(4, 8, 4, 5, 9, 5), collision);
        var shape = StairShape.GetBounds(metadata);
        Assert.Equal(new[] { shape.Base.Offset(4, 8, 4), shape.Step.Offset(4, 8, 4) }, collision);
        Assert.InRange(collision.Sum(box =>
            (box.MaxX - box.MinX) * (box.MaxY - box.MinY) * (box.MaxZ - box.MinZ)), .74999, .75001);

        // Stacked stairs merge into one source span; straddle Y=64 and a negative chunk edge.
        var blocks = new byte[16 * ChuckFormat.WorldHeight * 16];
        var metas = new byte[blocks.Length];
        foreach (var y in new[] { 63, 64 })
        {
            var index = (15 * 16 + 15) * ChuckFormat.WorldHeight + y;
            blocks[index] = (byte)block.Id;
            metas[index] = metadata;
        }
        var source = new TerrainLodSourceSnapshot(-1, -1, 16, ChuckFormat.WorldHeight, 16, blocks, metas, 1);
        var hierarchy = TerrainLodReducer.Build(source, materials, TerrainLodReductionStrategy.SurfacePreserving);
        var local = TerrainLodMeshBuilder.Build(hierarchy, 0, world.Content.Blocks, true);
        var tile = TerrainLodColumnTile.BuildLeaf(source, materials);
        var received = TerrainLodColumnTileCacheStore.DecodePortable(
            TerrainLodColumnTileCacheStore.EncodePortable(tile));
        Assert.Equal(tile.CanonicalHash, received.CanonicalHash);
        Assert.Equal(material, received[15, 15].At(63).Material);
        var spatial = TerrainLodSpatialMeshBuilder.Build(received, world.Content.Blocks, 16,
            emitTileBoundaryFaces: false);

        Assert.Equal(44 * 4, local.Vertices.Length);
        Assert.Equal(44, spatial.SolidQuadCount);
        Assert.Equal(2, spatial.Pages.Length);
        Assert.Empty(local.TranslucentVertices);
        var localPoints = local.Vertices.Select(v => Point(v, -16, ChuckFormat.WorldHeight / 2, -16)).Order().ToArray();
        var spatialPoints = spatial.Pages.SelectMany(page => page.Vertices.Select(v =>
            Point(v, page.OriginX, page.OriginY, page.OriginZ))).Order().ToArray();
        Assert.Equal(localPoints, spatialPoints);

        // Coarse structural approximation stays available; it must not vanish like tiny details.
        Assert.NotEmpty(TerrainLodMeshBuilder.Build(hierarchy, 1, world.Content.Blocks, true).Vertices);

        var airSource = new TerrainLodSourceSnapshot(0, -1, 16, ChuckFormat.WorldHeight, 16,
            new byte[blocks.Length], new byte[blocks.Length], 1);
        var air = TerrainLodReducer.Build(airSource, materials, TerrainLodReductionStrategy.SurfacePreserving);
        var owner = TerrainLodBoundarySummary.Capture(hierarchy, 0, 1);
        var neighbor = TerrainLodBoundarySummary.Capture(air, 0, 1);
        Assert.Empty(TerrainLodSeamMeshBuilder.BuildSolid(owner, 0, neighbor, 0,
            Side.East, world.Content.Blocks, true).Vertices);
        Assert.Empty(TerrainLodSeamMeshBuilder.BuildSolid(owner, 0, neighbor, 1,
            Side.East, world.Content.Blocks, true).Vertices);
        Assert.NotEmpty(TerrainLodSeamMeshBuilder.BuildSolid(owner, 1, neighbor, 1,
            Side.East, world.Content.Blocks, true).Vertices);
        foreach (var segment in TerrainLodSpatialSeamPlanner.Plan(
                     [new TerrainLodTileSelection(tile.Key, 0, 16)]))
            Assert.Empty(TerrainLodSpatialSeamMeshBuilder.Build(
                segment, tile, null, world.Content.Blocks).Pages);

        static (int X, int Y, int Z, ushort U, ushort V) Point(ChunkVertex v, int x, int y, int z) =>
            ((int)MathF.Round(2 * (x + v.X * 64f / 32767)),
             (int)MathF.Round(2 * (y + v.Y * 64f / 32767)),
             (int)MathF.Round(2 * (z + v.Z * 64f / 32767)), v.U, v.V);
    }
}
