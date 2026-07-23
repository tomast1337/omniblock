using BetaSharp.Blocks;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockTntTests
{
    [Fact]
    public void NeighborUpdate_WhenPoweredByEmitter_PrimesAndClearsBlock()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("tnt").id);
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("lit_redstone_torch").id); // powers TNT

        BlockRegistry.Get("tnt").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, BlockRegistry.Get("lit_redstone_torch").id));

        Assert.Equal(0, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void NeighborUpdate_NonEmitterTrigger_DoesNotPrimeTnt()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("tnt").id);
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id);

        BlockRegistry.Get("tnt").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, BlockRegistry.Get("stone").id));

        Assert.Equal(BlockRegistry.Get("tnt").id, world.Reader.GetBlockId(0, 64, 0));
    }
}
