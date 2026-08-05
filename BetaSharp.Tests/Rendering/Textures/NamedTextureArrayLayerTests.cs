using BetaSharp.Client.Rendering.Core.Textures.Atlas;
using BetaSharp.Textures;
using Xunit;

namespace BetaSharp.Tests.Rendering.Textures;

/// <summary>
///     Layer assignment only — no GL. Constructing the array builds the name and grid tables;
///     nothing touches the GPU until <c>Rebuild</c>, which these never call.
/// </summary>
public class NamedTextureArrayLayerTests
{
    private static NamedTextureArray Build(AtlasTileMap map) =>
        new("test", map, () => throw new InvalidOperationException("no rebuild in these tests"), () => null!);

    private static readonly AtlasTileMap s_map = new()
    {
        TileSize = 16,
        GridWidth = 16,
        GridHeight = 16,
        Tiles =
        [
            new AtlasTile("stone", 1, 0),
            new AtlasTile("dirt", 2, 0),
            new AtlasTile("bedrock", 1, 1),
        ]
    };

    [Fact]
    public void LayerZeroIsReservedForTheCheckerboard()
    {
        NamedTextureArray array = Build(s_map);

        Assert.Equal(0, NamedTextureArray.MissingLayer);
        Assert.DoesNotContain(0, s_map.Tiles.Select(t => array.GetLayer(t.Name)!.Value));
        Assert.Equal(s_map.Tiles.Count + 1, array.LayerCount);
    }

    [Fact]
    public void ATileResolvesToTheSameLayerByNameAndByGridIndex()
    {
        NamedTextureArray array = Build(s_map);

        Assert.Equal(array.GetLayer("stone"), array.LayerOfGridIndex(s_map.IndexOf("stone")));
        Assert.Equal(array.GetLayer("dirt"), array.LayerOfGridIndex(s_map.IndexOf("dirt")));
        Assert.Equal(array.GetLayer("bedrock"), array.LayerOfGridIndex(s_map.IndexOf("bedrock")));
    }

    [Theory]
    [InlineData(0)] // an in-grid cell no tile is named after
    [InlineData(255)]
    [InlineData(256)] // past the grid entirely
    [InlineData(-1)]
    public void AnUnclaimedGridCellResolvesToTheCheckerboard(int gridIndex)
    {
        NamedTextureArray array = Build(s_map);

        Assert.Equal(NamedTextureArray.MissingLayer, array.LayerOfGridIndex(gridIndex));
    }

    [Fact]
    public void EveryTerrainAndItemTileHasItsOwnLayer()
    {
        foreach (AtlasTileMap map in new[] { Atlases.Terrain, Atlases.Items })
        {
            NamedTextureArray array = Build(map);
            int[] layers = [.. array.Names.Select(n => array.GetLayer(n)!.Value)];

            Assert.Equal(layers.Length, layers.Distinct().Count());
            Assert.DoesNotContain(NamedTextureArray.MissingLayer, layers);
        }
    }
}
