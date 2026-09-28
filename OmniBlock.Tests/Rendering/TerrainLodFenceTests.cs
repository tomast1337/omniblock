using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodFenceTests
{
    [Fact]
    public void Every_visual_connection_mask_has_a_post_and_only_the_requested_half_rails()
    {
        for (var mask = 0; mask < 16; mask++)
        {
            var boxes = FenceShape.GetBounds(mask).ToArray();
            Assert.Equal(1 + 2 * System.Numerics.BitOperations.PopCount((uint)mask), boxes.Length);
            Assert.Equal(.375, boxes[0].MinX);
            Assert.Equal(.625, boxes[0].MaxX);
            Assert.Equal(1, boxes[0].MaxY);
            foreach (var box in boxes.Skip(1))
            {
                Assert.InRange(box.MinY, .375, .75);
                Assert.InRange(box.MaxY, .5625, .9375);
                if (box.MinX == 0) Assert.NotEqual(0, mask & FenceShape.West);
                if (box.MaxX == 1) Assert.NotEqual(0, mask & FenceShape.East);
                if (box.MinZ == 0) Assert.NotEqual(0, mask & FenceShape.North);
                if (box.MaxZ == 1) Assert.NotEqual(0, mask & FenceShape.South);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Split_fences_match_internal_connections_and_neighbor_edits_replace_only_the_seam(bool south)
    {
        var world = new FakeWorldContext();
        var catalog = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var id = (byte)world.Content.Blocks.Get("omniblock:fence").Id;
        var material = catalog.Resolve(id, 0);
        Assert.Equal(TerrainLodGeometryClass.Fence, material.Geometry);
        Assert.False(material.OccludesFaces);
        Assert.Equal(1, material.MaxSampleSize);
        Assert.True(TerrainLodFenceGeometry.Connects(material, material with { Metadata = 7 }));
        Assert.False(TerrainLodFenceGeometry.Connects(material, material with { BlockId = "example:other_fence" }));
        var owner = Source(-1, -1, (x,y,z) => y is 63 or 64 && (south ? x == 7 && z == 15 : x == 15 && z == 7));
        var neighbor = Source(south ? -1 : 0, south ? 0 : -1,
            (x,y,z) => y is 63 or 64 && (south ? x == 7 && z == 0 : x == 0 && z == 7));
        var reference = Source(0, 0, (x,y,z) => y is 63 or 64 &&
            (south ? x == 7 && z is 7 or 8 : x is 7 or 8 && z == 7));
        var empty = Source(neighbor.ChunkX, neighbor.ChunkZ, (_,_,_) => false, revision: 2);

        var oh = Hierarchy(owner); var nh = Hierarchy(neighbor);
        var ob = TerrainLodBoundarySummary.Capture(oh, 0, 1);
        var nb = TerrainLodBoundarySummary.Capture(nh, 0, 1);
        var eb = TerrainLodBoundarySummary.Capture(Hierarchy(empty), 0, 1);
        var side = south ? Side.South : Side.East;
        var seam = TerrainLodSeamMeshBuilder.BuildSolid(ob, 0, nb, 0, side, world.Content.Blocks, true);
        Assert.Equal(48 * 4, seam.Vertices.Length); // two heights, both halves, two rails
        Assert.Empty(TerrainLodSeamMeshBuilder.BuildSolid(ob, 0, eb, 0, side, world.Content.Blocks, true).Vertices);
        Assert.Empty(TerrainLodSeamMeshBuilder.BuildSolid(ob, 0, nb, 1, side, world.Content.Blocks, true).Vertices);
        Assert.NotEqual(nb.Identity, eb.Identity);
        var ownerHalf = TerrainLodSeamMeshBuilder.BuildSolid(ob, 0, nb, 0, side, world.Content.Blocks, true,
            materialSide: TerrainLodSeamMaterialSide.Owner);
        var neighborHalf = TerrainLodSeamMeshBuilder.BuildSolid(ob, 0, nb, 0, side, world.Content.Blocks, true,
            materialSide: TerrainLodSeamMaterialSide.Neighbor);
        Assert.Equal(seam.Vertices.Length, ownerHalf.Vertices.Length + neighborHalf.Vertices.Length);

        var local = Local(oh, owner.ChunkX, owner.ChunkZ)
            .Concat(Local(nh, neighbor.ChunkX, neighbor.ChunkZ))
            .Concat(seam.Vertices.Select(v => Point(v, -16, 64, -16))).Order().ToArray();
        var referenceMesh = TerrainLodMeshBuilder.Build(Hierarchy(reference), 0, world.Content.Blocks, true);
        var expected = referenceMesh.Vertices.Select(v => Point(v, south ? -16 : -8, 64, south ? -8 : -16))
            .Order().ToArray();
        Assert.Equal(expected, local);

        var ot = Tile(owner); var nt = Tile(neighbor); var et = Tile(empty);
        var os = new TerrainLodTileSelection(ot.Key, 0, 16);
        var ns = new TerrainLodTileSelection(nt.Key, 0, 16);
        var segment = TerrainLodSpatialSeamPlanner.Plan([os, ns]).Single(s => !s.IsExterior);
        var spatialSeam = TerrainLodSpatialSeamMeshBuilder.Build(segment, ot, nt, world.Content.Blocks);
        var removed = TerrainLodSpatialSeamMeshBuilder.Build(segment, ot, et, world.Content.Blocks);
        Assert.Empty(removed.Pages);
        Assert.NotEqual(spatialSeam.CanonicalHash, removed.CanonicalHash);
        var partial = Tile(Source(neighbor.ChunkX, neighbor.ChunkZ,
            (x,y,z) => y == 63 && (south ? x == 7 && z == 0 : x == 0 && z == 7), revision: 3));
        Assert.Equal(24, TerrainLodSpatialSeamMeshBuilder.Build(
            segment, ot, partial, world.Content.Blocks).SolidQuadCount);
        var exterior = segment with { Neighbor = null };
        var unknown = TerrainLodSpatialSeamMeshBuilder.Build(exterior, ot, null, world.Content.Blocks);
        Assert.Empty(unknown.Pages); // unresolved boundary, no invented connection or wall
        Assert.NotEqual(unknown.CanonicalHash, removed.CanonicalHash);
        var reversed = segment with { Owner = ns, Neighbor = os,
            OwnerSide = south ? TerrainLodSpatialBoundarySide.North : TerrainLodSpatialBoundarySide.West };
        Assert.Equal(Pages(spatialSeam.Pages).Order(), Pages(TerrainLodSpatialSeamMeshBuilder.Build(
            reversed, nt, ot, world.Content.Blocks).Pages).Order());
        var spatial = Pages(TerrainLodSpatialMeshBuilder.Build(ot, world.Content.Blocks, 16,
                emitTileBoundaryFaces: false).Pages)
            .Concat(Pages(TerrainLodSpatialMeshBuilder.Build(nt, world.Content.Blocks, 16,
                emitTileBoundaryFaces: false).Pages)).Concat(Pages(spatialSeam.Pages)).Order().ToArray();
        Assert.Equal(expected, spatial);

        // No giant rails at coarse resolution, nor phantom boundary material in local L1.
        Assert.Empty(TerrainLodMeshBuilder.Build(oh, 1, world.Content.Blocks, true).Vertices);
        Assert.False(material.CanRepresentAt(2));

        TerrainLodSourceSnapshot Source(int cx, int cz, Func<int,int,int,bool> present, long revision = 1)
        {
            var blocks = new byte[ChuckFormat.ChunkSize];
            for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            for (var y = 0; y < ChuckFormat.WorldHeight; y++)
                if (present(x,y,z)) blocks[(x * 16 + z) * ChuckFormat.WorldHeight + y] = id;
            return new(cx, cz, 16, ChuckFormat.WorldHeight, 16, blocks, new byte[blocks.Length], revision);
        }
        TerrainLodHierarchy Hierarchy(TerrainLodSourceSnapshot s) =>
            TerrainLodReducer.Build(s, catalog, TerrainLodReductionStrategy.SurfacePreserving);
        TerrainLodColumnTile Tile(TerrainLodSourceSnapshot s)
        {
            var tile = TerrainLodColumnTile.BuildLeaf(s, catalog);
            var received = TerrainLodColumnTileCacheStore.DecodePortable(TerrainLodColumnTileCacheStore.EncodePortable(tile));
            Assert.Equal(tile.CanonicalHash, received.CanonicalHash);
            return received;
        }
        IEnumerable<(int,int,int,ushort,ushort)> Local(TerrainLodHierarchy h, int cx, int cz) =>
            TerrainLodMeshBuilder.Build(h, 0, world.Content.Blocks, true).Vertices
                .Select(v => Point(v, cx * 16, 64, cz * 16));
    }

    [Fact]
    public void Coarse_spatial_reduction_does_not_inflate_fence_posts_into_columns()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var id = (byte)world.Content.Blocks.Get("omniblock:fence").Id;
        var key = new TerrainLodTileKey(1, 0, 0);
        var data = Enumerable.Repeat(id, 16 * 16 * 16).ToArray();
        var children = Enumerable.Range(0, 4).Select(index =>
        {
            var child = key.Child(index);
            return TerrainLodColumnTile.BuildLeaf(new TerrainLodSourceSnapshot(child.X, child.Z,
                16, 16, 16, data, new byte[data.Length], 1), materials);
        }).ToArray();
        var coarse = TerrainLodColumnTile.BuildParent(key, children, 1);
        Assert.Empty(TerrainLodSpatialMeshBuilder.Build(coarse, world.Content.Blocks, 16).Pages);
        Assert.All(coarse[0, 0].Spans, span => Assert.True(span.IsAir));
    }

    private static IEnumerable<(int,int,int,ushort,ushort)> Pages(IEnumerable<TerrainLodSpatialMeshPage> pages) =>
        pages.SelectMany(page => page.Vertices.Select(v => Point(v, page.OriginX, page.OriginY, page.OriginZ)));
    private static (int,int,int,ushort,ushort) Point(ChunkVertex v, int x, int y, int z) =>
        ((int)MathF.Round(16 * (x + v.X * 64f / 32767)),
         (int)MathF.Round(16 * (y + v.Y * 64f / 32767)),
         (int)MathF.Round(16 * (z + v.Z * 64f / 32767)), v.U, v.V);
}
