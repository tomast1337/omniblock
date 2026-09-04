using OmniBlock.Blocks;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockTntTests
{
    [Fact]
    public void NeighborUpdate_WhenPoweredByEmitter_PrimesAndClearsBlock()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("tnt").Id);
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("lit_redstone_torch").Id); // powers TNT

        TestBlocks.Get("tnt").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, TestBlocks.Get("lit_redstone_torch").Id));

        Assert.Equal(0, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void NeighborUpdate_NonEmitterTrigger_DoesNotPrimeTnt()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("tnt").Id);
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("stone").Id);

        TestBlocks.Get("tnt").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, TestBlocks.Get("stone").Id));

        Assert.Equal(TestBlocks.Get("tnt").Id, world.Reader.GetBlockId(0, 64, 0));
    }
}
