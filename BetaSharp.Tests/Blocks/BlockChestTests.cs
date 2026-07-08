using BetaSharp.Blocks;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockChestTests
{
    [Fact]
    public void GetTextureId_NorthSouthDoubleChestFacingEast_UsesCorrectFrontHalves()
    {
        FakeWorldContext world = new();
        PlaceNorthSouthDoubleChest(world);

        Assert.Equal(BlockTextures.ChestDoubleFrontRight, BlockRegistry.Get("chest").GetTextureId(world.Reader, 0, 64, 0, Side.East));
        Assert.Equal(BlockTextures.ChestDoubleFrontLeft, BlockRegistry.Get("chest").GetTextureId(world.Reader, 0, 64, 1, Side.East));
    }

    [Fact]
    public void GetTextureId_NorthSouthDoubleChestFacingWest_UsesCorrectFrontHalves()
    {
        FakeWorldContext world = new();
        PlaceNorthSouthDoubleChest(world);
        world.ReaderWriter.SetInitial(1, 64, 0, BlockRegistry.Get("stone").Id);
        world.ReaderWriter.SetInitial(1, 64, 1, BlockRegistry.Get("stone").Id);

        Assert.Equal(BlockTextures.ChestDoubleFrontLeft, BlockRegistry.Get("chest").GetTextureId(world.Reader, 0, 64, 0, Side.West));
        Assert.Equal(BlockTextures.ChestDoubleFrontRight, BlockRegistry.Get("chest").GetTextureId(world.Reader, 0, 64, 1, Side.West));
    }

    private static void PlaceNorthSouthDoubleChest(FakeWorldContext world)
    {
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("chest").Id);
        world.ReaderWriter.SetInitial(0, 64, 1, BlockRegistry.Get("chest").Id);
    }
}
