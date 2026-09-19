using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodSpatialSeamMeshBuilderTests
{
    [Fact]
    public void Coarse_to_refined_boundary_uses_the_finer_sample_partition()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var coarse = Parent(materials, new TerrainLodTileKey(1, 0, 0), stone);
        var refinedAir = Leaf(materials, 2, 0, 16, (_, _, _) => 0);
        TerrainLodTileSelection[] selection =
        [
            new(coarse.Key, coarse.HorizontalSampleLevel, 8),
            new(refinedAir.Key, refinedAir.HorizontalSampleLevel, 8)
        ];
        var segment = Assert.Single(TerrainLodSpatialSeamPlanner.Plan(
            selection, includeExterior: false));

        var seam = TerrainLodSpatialSeamMeshBuilder.Build(
            segment, coarse, refinedAir, world.Content.Blocks);

        Assert.Equal(16, seam.SolidQuadCount);
        Assert.Equal(0, seam.TranslucentQuadCount);
        Assert.All(seam.Pages, page =>
        {
            Assert.Equal(page.Vertices.Length, page.Lights.Length);
            Assert.Equal(page.SolidRanges.AvailableQuadCount,
                page.SolidRanges.East.QuadCount);
        });
    }

    [Fact]
    public void Matching_liquid_on_both_sides_does_not_create_a_coplanar_seam()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var water = checked((byte)world.Content.Blocks.Get("omniblock:flowing_water").Id);
        var west = Leaf(materials, 0, 0, 16,
            (_, y, _) => y < 8 ? water : (byte)0);
        var east = Leaf(materials, 1, 0, 16,
            (_, y, _) => y < 8 ? water : (byte)0);
        TerrainLodTileSelection[] selection =
        [
            new(west.Key, 0, 8),
            new(east.Key, 0, 8)
        ];
        var segment = Assert.Single(TerrainLodSpatialSeamPlanner.Plan(
            selection, includeExterior: false));

        var seam = TerrainLodSpatialSeamMeshBuilder.Build(
            segment, west, east, world.Content.Blocks);

        Assert.Equal(0, seam.SolidQuadCount);
        Assert.Equal(0, seam.TranslucentQuadCount);
        Assert.Empty(seam.Pages);
    }

    [Fact]
    public void Exterior_liquid_boundary_preserves_translucency_and_lowered_surface()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var water = checked((byte)world.Content.Blocks.Get("omniblock:flowing_water").Id);
        var tile = Leaf(materials, 0, 0, 16,
            (_, y, _) => y < 8 ? water : (byte)0);
        var selection = new TerrainLodTileSelection(tile.Key, 0, 8);
        var segment = TerrainLodSpatialSeamPlanner.Plan([selection])
            .Single(static seam => seam.OwnerSide == TerrainLodSpatialBoundarySide.East);

        var seam = TerrainLodSpatialSeamMeshBuilder.Build(
            segment, tile, neighbor: null, world.Content.Blocks);

        Assert.Equal(0, seam.SolidQuadCount);
        Assert.Equal(16, seam.TranslucentQuadCount);
        var maximumY = seam.Pages.SelectMany(static page => page.TranslucentVertices)
            .Max(vertex => vertex.Y / (32767f / 64f) + seam.Pages[0].OriginY);
        Assert.InRange(maximumY, 7.0f, 8.0f);
    }

    private static TerrainLodColumnTile Parent(
        TerrainLodMaterialCatalog materials,
        TerrainLodTileKey key,
        byte block)
    {
        var children = Enumerable.Range(0, 4)
            .Select(index =>
            {
                var child = key.Child(index);
                return Leaf(materials, child.X, child.Z, 16,
                    (_, y, _) => y < 8 ? block : (byte)0);
            })
            .ToArray();
        return TerrainLodColumnTile.BuildParent(
            key, children, horizontalSampleLevel: 1);
    }

    private static TerrainLodColumnTile Leaf(
        TerrainLodMaterialCatalog materials,
        int chunkX,
        int chunkZ,
        int height,
        Func<int, int, int, byte> block)
    {
        var blocks = new byte[checked(16 * height * 16)];
        var metadata = new byte[blocks.Length];
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        for (var y = 0; y < height; y++)
            blocks[(x * 16 + z) * height + y] = block(x, y, z);
        return TerrainLodColumnTile.BuildLeaf(
            new TerrainLodSourceSnapshot(
                chunkX, chunkZ, 16, height, 16, blocks, metadata, terrainRevision: 1),
            materials);
    }
}
