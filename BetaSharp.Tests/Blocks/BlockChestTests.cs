using BetaSharp.Blocks;
using BetaSharp.Textures;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockChestTests
{
    private static readonly AtlasTileMap s_terrain = AtlasTileMap.Load("textures/atlas/terrain.json");

    [Fact]
    public void GetTextureId_NorthSouthDoubleChestFacingEast_UsesCorrectFrontHalves()
    {
        FakeWorldContext world = new();
        PlaceNorthSouthDoubleChest(world);

        Assert.Equal(s_terrain.IndexOf("chest_double_front_right"), BlockRegistry.Get("chest").GetTextureId(world.Reader, 0, 64, 0, Side.East));
        Assert.Equal(s_terrain.IndexOf("chest_double_front_left"), BlockRegistry.Get("chest").GetTextureId(world.Reader, 0, 64, 1, Side.East));
    }

    [Fact]
    public void GetTextureId_NorthSouthDoubleChestFacingWest_UsesCorrectFrontHalves()
    {
        FakeWorldContext world = new();
        PlaceNorthSouthDoubleChest(world);
        world.ReaderWriter.SetInitial(1, 64, 0, BlockRegistry.Get("stone").Id);
        world.ReaderWriter.SetInitial(1, 64, 1, BlockRegistry.Get("stone").Id);

        Assert.Equal(s_terrain.IndexOf("chest_double_front_left"), BlockRegistry.Get("chest").GetTextureId(world.Reader, 0, 64, 0, Side.West));
        Assert.Equal(s_terrain.IndexOf("chest_double_front_right"), BlockRegistry.Get("chest").GetTextureId(world.Reader, 0, 64, 1, Side.West));
    }

    private static void PlaceNorthSouthDoubleChest(FakeWorldContext world)
    {
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("chest").Id);
        world.ReaderWriter.SetInitial(0, 64, 1, BlockRegistry.Get("chest").Id);
    }
}
