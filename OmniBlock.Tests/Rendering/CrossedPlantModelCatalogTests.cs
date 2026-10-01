using System.Text;
using OmniBlock.Client.Rendering.Blocks.Models;
using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering;

public sealed class CrossedPlantModelCatalogTests
{
    [Fact]
    public void Builtin_and_pack_override_are_bound_by_block_without_changing_other_plants()
    {
        var blocks = new FakeWorldContext().Content.Blocks;
        var original = Load(blocks, null);
        var changed = Load(blocks, """
            {"default":{"inset":0.2},"overrides":{"omniblock:dandelion":{"inset":0.1}}}
            """);
        var dandelion = blocks.Get("dandelion").Id;
        var rose = blocks.Get("rose").Id;

        Assert.True(original.Get(dandelion)!.ContentEquals(CrossedPlantGeometry.Builtin));
        Assert.Equal(.1f, changed.Get(dandelion)!.Quads[0].A.X);
        Assert.Equal(.2f, changed.Get(rose)!.Quads[0].A.X);
        Assert.False(original.ContentEquals(changed));
        Assert.Null(changed.Get(blocks.Get("stone").Id));
        Assert.True(Load(blocks, null).ContentEquals(original));
    }

    [Theory]
    [InlineData("{\"overrides\":{\"example:missing\":{\"inset\":0.1}}}", "example:missing")]
    [InlineData("{\"overrides\":{\"omniblock:stone\":{\"inset\":0.1}}}", "omniblock:stone")]
    [InlineData("{\"default\":{\"inset\":0.5}}", "inset")]
    [InlineData("{\"default\":{\"inset\":0.1,\"quads\":[]}}", "exactly one")]
    [InlineData("{\"default\":{\"quads\":[[[0,0,0,0,0],[0,0,0,0,1],[0,0,0,1,1],[0,0,0,1,0]]]}}", "zero area")]
    [InlineData("{\"default\":{\"inset\":0.1},\"default\":{\"inset\":0.2}}", "duplicate")]
    [InlineData("{\"unsupported\":true}", "unsupported")]
    public void Invalid_pack_model_fails_with_resource_and_reference(string json, string detail)
    {
        var blocks = new FakeWorldContext().Content.Blocks;
        var error = Assert.Throws<InvalidDataException>(() => Load(blocks, json));
        Assert.Contains(CrossedPlantModelCatalog.Path, error.Message);
        Assert.Contains(detail, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Local_and_spatial_fine_lod_use_the_captured_plant_binding()
    {
        var world = new FakeWorldContext();
        var blocks = world.Content.Blocks;
        var stone = checked((byte)blocks.Get("stone").Id);
        var flower = checked((byte)blocks.Get("dandelion").Id);
        var data = new byte[16 * 16 * 16];
        for (var x = 0; x < 16; x++)
        for (var z = 0; z < 16; z++)
        {
            for (var y = 0; y < 8; y++) data[(x * 16 + z) * 16 + y] = stone;
            if (x == 8 && z == 8) data[(x * 16 + z) * 16 + 8] = flower;
        }
        var source = new TerrainLodSourceSnapshot(0, 0, 16, 16, 16, data,
            new byte[data.Length], 1);
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var hierarchy = TerrainLodReducer.Build(source, materials,
            TerrainLodReductionStrategy.SurfacePreserving);
        var tile = TerrainLodColumnTile.BuildLeaf(source, materials);
        var bindings = BlockModelBindingTests.Build(blocks, overrides: path =>
            path == CrossedPlantModelCatalog.Path ? Open("{\"default\":{\"inset\":0.2}}") : null);

        var localOld = TerrainLodMeshBuilder.Build(hierarchy, 0, blocks, true);
        var localNew = TerrainLodMeshBuilder.Build(hierarchy, 0, blocks, true, models: bindings);
        Assert.NotEqual(localOld.Vertices, localNew.Vertices);
        Assert.Equal(localOld.Vertices.Length, localNew.Vertices.Length);
        var spatialOld = TerrainLodSpatialMeshBuilder.Build(tile, blocks, 16);
        var spatialNew = TerrainLodSpatialMeshBuilder.Build(tile, blocks, 16, models: bindings);
        var oldVertices = spatialOld.Pages.SelectMany(page => page.Vertices).ToArray();
        var newVertices = spatialNew.Pages.SelectMany(page => page.Vertices).ToArray();
        Assert.NotEqual(oldVertices, newVertices);
        Assert.Equal(oldVertices.Length, newVertices.Length);
    }

    private static CrossedPlantModelCatalog Load(OmniBlock.Blocks.IBlockRuntimeView blocks, string? replacement) =>
        CrossedPlantModelCatalog.Load(blocks,
            path => path == CrossedPlantModelCatalog.Path && replacement is not null
                ? Open(replacement) : null,
            BlockModelBindingTests.OpenInstalled);

    private static Stream Open(string value) => new MemoryStream(Encoding.UTF8.GetBytes(value));
}
