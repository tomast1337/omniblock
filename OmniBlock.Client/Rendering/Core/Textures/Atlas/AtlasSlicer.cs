using OmniBlock.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace OmniBlock.Client.Rendering.Core.Textures.Atlas;

/// <summary>
///     Crops the named tiles out of a legacy grid atlas image, per <see cref="AtlasTileMap" />.
/// </summary>
/// <remarks>
///     Tiles the map doesn't name (the classic magenta placeholders, genuinely unused cells) are
///     never cropped, so nothing ends up holding an array layer no name will ever resolve to.
/// </remarks>
public static class AtlasSlicer
{
    /// <summary>
    ///     Reads the per-cell pixel size from <paramref name="source" />'s own width rather than
    ///     trusting <see cref="AtlasTileMap.TileSize" /> literally, so a higher-resolution default
    ///     atlas — still laid out on the same 16x16 grid — slices correctly without the JSON needing
    ///     to know about it.
    /// </summary>
    public static Dictionary<string, Image<Rgba32>> Slice(Image<Rgba32> source, AtlasTileMap map)
    {
        int pixelTileSize = source.Width / map.GridWidth;
        var tiles = new Dictionary<string, Image<Rgba32>>(map.Tiles.Count);

        foreach (AtlasTile tile in map.Tiles)
        {
            var rect = new Rectangle(tile.X * pixelTileSize, tile.Y * pixelTileSize, pixelTileSize, pixelTileSize);
            tiles[tile.Name] = source.Clone(ctx => ctx.Crop(rect));
        }

        return tiles;
    }
}
