using BetaSharp.Blocks;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockTntTests
{
    [Fact]
    public void NeighborUpdate_WhenPoweredByEmitter_PrimesAndClearsBlock()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, Block.TNT.Id);
        world.ReaderWriter.SetInitial(0, 63, 0, Block.LitRedstoneTorch.Id); // powers TNT

        Block.TNT.NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, Block.LitRedstoneTorch.Id));

        Assert.Equal(0, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void NeighborUpdate_NonEmitterTrigger_DoesNotPrimeTnt()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, Block.TNT.Id);
        world.ReaderWriter.SetInitial(0, 63, 0, Block.Stone.Id);

        Block.TNT.NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, Block.Stone.Id));

        Assert.Equal(Block.TNT.Id, world.Reader.GetBlockId(0, 64, 0));
    }
}
