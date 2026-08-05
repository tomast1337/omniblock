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

    [Theory]
    [MemberData(nameof(Atlases))]
    public void Every_tile_has_its_own_layer(string name, AtlasTileMap atlas)
    {
        int[] layers = [.. atlas.Tiles.Select(t => atlas.LayerOf(t.Name))];

        Assert.True(layers.Length == layers.Distinct().Count(), $"{name} atlas has tiles sharing a layer.");
        Assert.DoesNotContain(AtlasTileMap.MissingLayer, layers);
        Assert.All(layers, layer => Assert.InRange(layer, 1, atlas.LayerCount - 1));
    }

    [Theory]
    [MemberData(nameof(Atlases))]
    public void A_tile_resolves_to_the_same_layer_by_name_and_by_grid_index(string name, AtlasTileMap atlas)
    {
        foreach (AtlasTile tile in atlas.Tiles)
        {
            Assert.True(atlas.LayerOf(tile.Name) == atlas.LayerOfGridIndex(atlas.IndexOf(tile.Name)),
                $"{name} atlas tile '{tile.Name}' resolves to a different layer by name than by index.");
        }
    }

    /// <summary>
    ///     An index nothing claims has to land somewhere, and landing on a real tile is the failure
    ///     mode worth ruling out — a block with a stale index would render as some unrelated texture
    ///     instead of as visibly broken.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(256)]
    public void An_index_outside_the_grid_resolves_to_the_reserved_layer(int gridIndex)
    {
        Assert.Equal(AtlasTileMap.MissingLayer, BetaSharp.Textures.Atlases.Terrain.LayerOfGridIndex(gridIndex));
    }

    [Fact]
    public void An_unclaimed_cell_inside_the_grid_resolves_to_the_reserved_layer()
    {
        AtlasTileMap atlas = BetaSharp.Textures.Atlases.Terrain;
        var claimed = atlas.Tiles.Select(t => t.X + t.Y * atlas.GridWidth).ToHashSet();

        int unclaimed = Enumerable.Range(0, atlas.GridWidth * atlas.GridHeight).First(i => !claimed.Contains(i));

        Assert.Equal(AtlasTileMap.MissingLayer, atlas.LayerOfGridIndex(unclaimed));
    }
}
