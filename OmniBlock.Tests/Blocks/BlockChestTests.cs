using OmniBlock.Blocks;
using OmniBlock.Textures;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockChestTests
{
    private static readonly AtlasTileMap s_terrain = AtlasTileMap.Load("textures/atlas/terrain.json");

    [Fact]
    public void GetTextureId_NorthSouthDoubleChestFacingEast_UsesCorrectFrontHalves()
    {
        FakeWorldContext world = new();
        PlaceNorthSouthDoubleChest(world);

        Assert.Equal(s_terrain.IndexOf("chest_double_front_right"), TestBlocks.Get("chest").GetTextureId(world.Reader, 0, 64, 0, Side.East));
        Assert.Equal(s_terrain.IndexOf("chest_double_front_left"), TestBlocks.Get("chest").GetTextureId(world.Reader, 0, 64, 1, Side.East));
    }

    [Fact]
    public void GetTextureId_NorthSouthDoubleChestFacingWest_UsesCorrectFrontHalves()
    {
        FakeWorldContext world = new();
        PlaceNorthSouthDoubleChest(world);
        world.ReaderWriter.SetInitial(1, 64, 0, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(1, 64, 1, TestBlocks.Get("stone").Id);

        Assert.Equal(s_terrain.IndexOf("chest_double_front_left"), TestBlocks.Get("chest").GetTextureId(world.Reader, 0, 64, 0, Side.West));
        Assert.Equal(s_terrain.IndexOf("chest_double_front_right"), TestBlocks.Get("chest").GetTextureId(world.Reader, 0, 64, 1, Side.West));
    }

    private static void PlaceNorthSouthDoubleChest(FakeWorldContext world)
    {
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("chest").Id);
        world.ReaderWriter.SetInitial(0, 64, 1, TestBlocks.Get("chest").Id);
    }
}
