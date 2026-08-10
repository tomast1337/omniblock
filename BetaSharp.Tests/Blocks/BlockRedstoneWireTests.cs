using OmniBlock.Blocks;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockRedstoneWireTests
{
    private static OnTickEvent Tick(FakeWorldContext world, int x = 0, int y = 64, int z = 0) => new(world, x, y, z, world.Reader.GetBlockMeta(x, y, z), world.Reader.GetBlockId(x, y, z));

    [Fact]
    public void NeighborUpdate_WhenSupportMissing_BreaksWireIntoAir()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("redstone_wire").Id, 7);

        BlockRegistry.Get("redstone_wire").NeighborUpdate(Tick(world));

        Assert.Equal(0, world.Reader.GetBlockId(0, 64, 0));
        Assert.Contains(world.ReaderWriter.SetBlockCalls, c => c is { X: 0, Y: 64, Z: 0, BlockId: 0 });
    }

    [Fact]
    public void NeighborUpdate_WhenRemote_DoesNothing()
    {
        FakeWorldContext world = new()
        {
            IsRemote = true
        };
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("redstone_wire").Id, 4);
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").Id);

        BlockRegistry.Get("redstone_wire").NeighborUpdate(Tick(world));

        Assert.Equal(BlockRegistry.Get("redstone_wire").Id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(4, world.Reader.GetBlockMeta(0, 64, 0));
        Assert.Empty(world.ReaderWriter.SetBlockCalls);
        Assert.Empty(world.ReaderWriter.SetMetaCalls);
    }
}
