using BetaSharp.Textures;

namespace BetaSharp.Tests.Textures;

public sealed class AtlasTileMapTests
{
    public static TheoryData<string, AtlasTileMap> Atlases => new()
    {
        { "terrain", BetaSharp.Textures.Atlases.Terrain },
        { "items", BetaSharp.Textures.Atlases.Items },
    };

    /// <summary>
    ///     Two names on one cell is how the atlas drifted out of step with the pixels: a name added
    ///     for a tile that already had one reads as correct, resolves to a real index, and renders
    ///     something else entirely. Nothing else catches it — <c>IndexOf</c> is happy to hand out the
    ///     same index twice.
    /// </summary>
    [Theory]
    [MemberData(nameof(Atlases))]
    public void No_two_tiles_share_a_cell(string name, AtlasTileMap atlas)
    {
        var shared = atlas.Tiles
            .GroupBy(t => (t.X, t.Y))
            .Where(g => g.Count() > 1)
            .Select(g => $"({g.Key.X},{g.Key.Y}): {string.Join(", ", g.Select(t => t.Name))}")
            .ToList();

        Assert.True(shared.Count == 0, $"{name} atlas has tiles sharing a cell: {string.Join(" | ", shared)}");
    }

    [Theory]
    [MemberData(nameof(Atlases))]
    public void Every_tile_is_inside_the_grid(string name, AtlasTileMap atlas)
    {
        foreach (AtlasTile tile in atlas.Tiles)
        {
            Assert.True(tile.X >= 0 && tile.X < atlas.GridWidth && tile.Y >= 0 && tile.Y < atlas.GridHeight,
                $"{name} atlas tile '{tile.Name}' at ({tile.X},{tile.Y}) is outside the {atlas.GridWidth}x{atlas.GridHeight} grid.");
        }
    }
}
