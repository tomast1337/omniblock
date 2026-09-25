using OmniBlock.Client.Rendering.Core.Textures.Atlas;
using OmniBlock.Client.Rendering.Chunks.Lod;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Tests.Rendering.Textures;

public sealed class TerrainTileMipmapsTests
{
    [Fact]
    public void Only_opaque_aggregate_cells_request_filtered_levels()
    {
        var world = new FakeWorldContext();
        var materials = TerrainLodMaterialCatalog.FromRuntime(world.Content);
        var stone = materials.Resolve(world.Content.Blocks.Get("omniblock:stone").Id, 0);
        var water = materials.Resolve(world.Content.Blocks.Get("omniblock:flowing_water").Id, 0);
        var leaves = materials.Resolve(world.Content.Blocks.Get("omniblock:leaves").Id, 0);

        Assert.Equal(0, TerrainLodTextureDetail.MipLevel(stone, 1));
        Assert.Equal(1, TerrainLodTextureDetail.MipLevel(stone, 2));
        Assert.Equal(2, TerrainLodTextureDetail.MipLevel(stone, 4));
        Assert.Equal(4, TerrainLodTextureDetail.MipLevel(stone, 16));
        Assert.Equal(4, TerrainLodTextureDetail.MipLevel(stone, 64));
        Assert.Equal(0, TerrainLodTextureDetail.MipLevel(water, 16));
        Assert.Equal(0, TerrainLodTextureDetail.MipLevel(leaves, 16));
        Assert.Equal(TerrainLodTextureDetail.RepresentativeColorFlag,
            TerrainLodTextureDetail.Pack(stone, 1));
        Assert.Equal(TerrainLodTextureDetail.RepresentativeColorFlag | 2,
            TerrainLodTextureDetail.Pack(stone, 4));
        Assert.Equal(0, TerrainLodTextureDetail.Pack(water, 16));
        Assert.Equal(0, TerrainLodTextureDetail.Pack(leaves, 16));
    }

    [Fact]
    public void Opaque_black_and_white_average_in_linear_light()
    {
        byte[] pixels =
        [
            0, 0, 0, 255, 255, 255, 255, 255,
            255, 255, 255, 255, 0, 0, 0, 255
        ];

        var levels = TerrainTileMipmaps.Build(pixels, 2, 2);

        Assert.Equal(2, levels.Length);
        Assert.Equal(pixels, levels[0]);
        Assert.Equal([188, 188, 188, 255], levels[1]);
    }

    [Fact]
    public void Transparent_texels_do_not_darken_the_filtered_color()
    {
        byte[] pixels =
        [
            255, 0, 0, 255, 0, 0, 0, 0,
            0, 0, 0, 0, 255, 0, 0, 255
        ];

        var levels = TerrainTileMipmaps.Build(pixels, 2, 2);

        Assert.Equal([255, 0, 0, 128], levels[1]);
    }

    [Fact]
    public void Odd_sized_tiles_include_the_last_row_and_column()
    {
        var pixels = new byte[3 * 3 * 4];
        for (var index = 0; index < 9; index++) pixels[index * 4 + 3] = 255;
        pixels[8 * 4] = 255;

        var levels = TerrainTileMipmaps.Build(pixels, 3, 3);

        Assert.Equal(2, levels.Length);
        Assert.InRange(levels[1][0], 90, 100);
        Assert.Equal(255, levels[1][3]);
    }
}
