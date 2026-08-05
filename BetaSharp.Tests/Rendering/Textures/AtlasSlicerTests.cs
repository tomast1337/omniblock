using BetaSharp.Client.Rendering.Core.Textures.Atlas;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BetaSharp.Tests.Rendering.Textures;

public class AtlasSlicerTests
{
    /// <summary>A 2x2 grid of 16px tiles, each cell a flat, distinct colour.</summary>
    private static Image<Rgba32> MakeGrid()
    {
        var image = new Image<Rgba32>(32, 32);
        Fill(image, 0, 0, Color.Red);
        Fill(image, 1, 0, Color.Lime);
        Fill(image, 0, 1, Color.Blue);
        Fill(image, 1, 1, Color.Yellow);
        return image;
    }

    private static void Fill(Image<Rgba32> image, int cellX, int cellY, Color color)
    {
        Rgba32 pixel = color.ToPixel<Rgba32>();
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                image[cellX * 16 + x, cellY * 16 + y] = pixel;
            }
        }
    }

    [Fact]
    public void SlicesEveryNamedTileAtItsGridPosition()
    {
        using Image<Rgba32> grid = MakeGrid();
        var map = new AtlasTileMap
        {
            TileSize = 16,
            GridWidth = 2,
            GridHeight = 2,
            Tiles =
            [
                new AtlasTile("top_left", 0, 0),
                new AtlasTile("top_right", 1, 0),
                new AtlasTile("bottom_left", 0, 1),
            ]
        };

        Dictionary<string, Image<Rgba32>> tiles = AtlasSlicer.Slice(grid, map);

        Assert.Equal(3, tiles.Count);
        Assert.Equal(Color.Red.ToPixel<Rgba32>(), tiles["top_left"][0, 0]);
        Assert.Equal(Color.Lime.ToPixel<Rgba32>(), tiles["top_right"][0, 0]);
        Assert.Equal(Color.Blue.ToPixel<Rgba32>(), tiles["bottom_left"][0, 0]);

        foreach (Image<Rgba32> tile in tiles.Values)
        {
            Assert.Equal(16, tile.Width);
            Assert.Equal(16, tile.Height);
        }

        foreach (Image<Rgba32> tile in tiles.Values) tile.Dispose();
    }

    [Fact]
    public void ATileNotNamedInTheMapIsNeverAllocated()
    {
        using Image<Rgba32> grid = MakeGrid();
        var map = new AtlasTileMap
        {
            TileSize = 16,
            GridWidth = 2,
            GridHeight = 2,
            Tiles = [new AtlasTile("top_left", 0, 0)]
        };

        Dictionary<string, Image<Rgba32>> tiles = AtlasSlicer.Slice(grid, map);

        // "bottom_right" (1,1) was never given a name, so nothing should stand in for it.
        Assert.Single(tiles);
        Assert.False(tiles.ContainsKey("bottom_right"));

        foreach (Image<Rgba32> tile in tiles.Values) tile.Dispose();
    }
}
