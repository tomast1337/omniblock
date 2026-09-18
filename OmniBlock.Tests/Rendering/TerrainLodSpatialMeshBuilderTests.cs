using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodSpatialMeshBuilderTests
{
    [Fact]
    public void Uniform_tile_culls_internal_faces_and_emits_paired_light_streams()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var tile = Leaf(materials, 0, 0, 128,
            (_, y, _) => y < 32 ? stone : (byte)0);

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8);

        Assert.Equal(tile.CanonicalHash, mesh.CanonicalHash);
        Assert.Equal(640, mesh.SolidQuadCount);
        Assert.Equal(0, mesh.TranslucentQuadCount);
        Assert.All(mesh.Pages, page =>
        {
            Assert.Equal(0, page.Vertices.Length % 4);
            Assert.Equal(page.Vertices.Length, page.Lights.Length);
            Assert.Equal(page.TranslucentVertices.Length, page.TranslucentLights.Length);
        });
    }

    [Fact]
    public void Liquid_geometry_is_kept_in_the_translucent_stream()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var water = checked((byte)world.Content.Blocks.Get("omniblock:flowing_water").Id);
        var tile = Leaf(materials, 0, 0, 128,
            (_, y, _) => y < 8 ? water : (byte)0);

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 8);

        Assert.Equal(0, mesh.SolidQuadCount);
        Assert.Equal(576, mesh.TranslucentQuadCount);
        Assert.All(mesh.Pages.SelectMany(static page => page.TranslucentLights), light =>
        {
            Assert.InRange(light.Block, (byte)0, (byte)60);
            Assert.InRange(light.Sky, (byte)0, (byte)60);
        });
    }

    [Fact]
    public void Vertical_budget_changes_only_the_presentation_copy()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var water = checked((byte)world.Content.Blocks.Get("omniblock:flowing_water").Id);
        var tile = Leaf(materials, 0, 0, 8,
            (_, y, _) => y switch
            {
                < 2 => stone,
                < 4 => 0,
                < 6 => water,
                _ => stone
            });
        var canonicalSpanCount = tile[0, 0].Spans.Count;

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            tile, world.Content.Blocks, verticalSliceBudget: 2);

        Assert.Equal(4, canonicalSpanCount);
        Assert.Equal(canonicalSpanCount, tile[0, 0].Spans.Count);
        Assert.InRange(mesh.MaximumRenderedSpans, 1, 2);
    }

    [Fact]
    public void Large_parent_is_partitioned_into_packed_vertex_safe_pages()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = checked((byte)world.Content.Blocks.Get("omniblock:stone").Id);
        var root = BuildUniformTree(
            new TerrainLodTileKey(3, 0, 0), materials, stone, height: 8);

        var mesh = TerrainLodSpatialMeshBuilder.Build(
            root, world.Content.Blocks, verticalSliceBudget: 4);

        Assert.Equal(4, mesh.Pages.Length);
        Assert.Equal(4, mesh.Pages.Select(static page => (page.Key.X, page.Key.Z)).Distinct().Count());
        Assert.All(mesh.Pages, page =>
        {
            Assert.Equal(0, page.OriginX % TerrainLodSpatialMeshBuilder.PageSize);
            Assert.Equal(0, page.OriginY % TerrainLodSpatialMeshBuilder.PageSize);
            Assert.Equal(0, page.OriginZ % TerrainLodSpatialMeshBuilder.PageSize);
            Assert.All(page.Vertices, vertex =>
            {
                Assert.InRange(vertex.X, (short)0, short.MaxValue);
                Assert.InRange(vertex.Y, (short)0, short.MaxValue);
                Assert.InRange(vertex.Z, (short)0, short.MaxValue);
                Assert.InRange(vertex.U, (ushort)0, ushort.MaxValue);
                Assert.InRange(vertex.V, (ushort)0, ushort.MaxValue);
            });
        });
    }

    private static TerrainLodColumnTile BuildUniformTree(
        TerrainLodTileKey key,
        TerrainLodMaterialCatalog materials,
        byte block,
        int height)
    {
        if (key.Level == 0)
            return Leaf(materials, key.X, key.Z, height,
                (_, y, _) => y < height / 2 ? block : (byte)0);
        var children = Enumerable.Range(0, 4)
            .Select(index => BuildUniformTree(key.Child(index), materials, block, height))
            .ToArray();
        return TerrainLodColumnTile.BuildParent(
            key, children, horizontalSampleLevel: key.Level);
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
        var source = new TerrainLodSourceSnapshot(
            chunkX, chunkZ, 16, height, 16, blocks, metadata, terrainRevision: 1);
        return TerrainLodColumnTile.BuildLeaf(source, materials);
    }
}
