using BetaSharp.Blocks;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockTntTests
{
    [Fact]
    public void NeighborUpdate_WhenPoweredByEmitter_PrimesAndClearsBlock()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("tnt").Id);
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("lit_redstone_torch").Id); // powers TNT

        BlockRegistry.Get("tnt").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, BlockRegistry.Get("lit_redstone_torch").Id));

        Assert.Equal(0, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void NeighborUpdate_NonEmitterTrigger_DoesNotPrimeTnt()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("tnt").Id);
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").Id);

        BlockRegistry.Get("tnt").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, BlockRegistry.Get("stone").Id));

        Assert.Equal(BlockRegistry.Get("tnt").Id, world.Reader.GetBlockId(0, 64, 0));
    }
}
