using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class TerrainLodOmissionTests
{
    [Theory]
    [InlineData("fire")]
    [InlineData("torch")]
    [InlineData("button")]
    [InlineData("rail")]
    public void Omitted_details_do_not_change_local_or_spatial_meshes_at_any_sample_level(string name)
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = (byte)world.Content.Blocks.Get("omniblock:stone").Id;
        var detail = (byte)world.Content.Blocks.Get("omniblock:" + name).Id;
        var baseline = Source(0, 0, 0);
        var decorated = Source(detail, 0, 0);
        var localBaseline = TerrainLodReducer.Build(baseline, materials,
            TerrainLodReductionStrategy.SurfacePreserving);
        var localDecorated = TerrainLodReducer.Build(decorated, materials,
            TerrainLodReductionStrategy.SurfacePreserving);

        for (var sampleLevel = 0; sampleLevel <= 4; sampleLevel++)
        {
            var expected = TerrainLodMeshBuilder.Build(localBaseline, sampleLevel,
                world.Content.Blocks, true);
            var actual = TerrainLodMeshBuilder.Build(localDecorated, sampleLevel,
                world.Content.Blocks, true);
            Assert.NotEmpty(expected.Vertices);
            Assert.Equal(expected.Vertices, actual.Vertices);
            Assert.Empty(actual.TranslucentVertices);

            var key = new TerrainLodTileKey(sampleLevel, 0, 0);
            var expectedTile = Tile(key, 0);
            var actualTile = Tile(key, detail);
            var expectedMesh = TerrainLodSpatialMeshBuilder.Build(expectedTile, world.Content.Blocks, 16);
            var actualMesh = TerrainLodSpatialMeshBuilder.Build(actualTile, world.Content.Blocks, 16);
            Assert.True(expectedMesh.SolidQuadCount > 0);
            Assert.Equal(expectedMesh.Pages.SelectMany(page => page.Vertices),
                actualMesh.Pages.SelectMany(page => page.Vertices));
            Assert.Equal(0, actualMesh.TranslucentQuadCount);
        }

        TerrainLodColumnTile Tile(TerrainLodTileKey key, byte decoration)
        {
            if (key.Level == 0)
                return TerrainLodColumnTile.BuildLeaf(Source(decoration, key.X, key.Z), materials);
            return TerrainLodColumnTile.BuildParent(key,
                Enumerable.Range(0, 4).Select(index => Tile(key.Child(index), decoration)).ToArray(),
                key.Level);
        }

        TerrainLodSourceSnapshot Source(byte decoration, int chunkX, int chunkZ)
        {
            var blocks = new byte[16 * 16 * 16];
            for (var x = 0; x < 16; x++)
            for (var z = 0; z < 16; z++)
            for (var y = 0; y < 16; y++)
                blocks[(x * 16 + z) * 16 + y] = y < 8 ? stone : decoration;
            return new TerrainLodSourceSnapshot(chunkX, chunkZ, 16, 16, 16,
                blocks, new byte[blocks.Length], 1);
        }
    }
}
