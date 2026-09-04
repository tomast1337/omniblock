using OmniBlock.Blocks;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockLeverTests
{
    [Fact]
    public void OnUse_TogglesPoweredBitInMetadata()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(-1, 64, 0, TestBlocks.Get("stone").Id); // support for facing=1
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("lever").Id, 1);

        bool firstUse = TestBlocks.Get("lever").OnUse(new OnUseEvent(world, null!, 0, 64, 0));
        int poweredMeta = world.Reader.GetBlockMeta(0, 64, 0);

        bool secondUse = TestBlocks.Get("lever").OnUse(new OnUseEvent(world, null!, 0, 64, 0));
        int unpoweredMeta = world.Reader.GetBlockMeta(0, 64, 0);

        Assert.True(firstUse);
        Assert.True(secondUse);
        Assert.Equal(9, poweredMeta); // facing 1 + powered bit
        Assert.Equal(1, unpoweredMeta); // toggled back
    }

    [Fact]
    public void PoweredLever_StrongPowersAttachedSide()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(-1, 64, 0, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("lever").Id, 9); // facing=1, powered

        bool strongPowerOnAttachedSide = TestBlocks.Get("lever").IsStrongPoweringSide(world.Reader, 0, 64, 0, 5);
        bool strongPowerOnOtherSide = TestBlocks.Get("lever").IsStrongPoweringSide(world.Reader, 0, 64, 0, 4);

        Assert.True(strongPowerOnAttachedSide);
        Assert.False(strongPowerOnOtherSide);
    }

    [Fact]
    public void NeighborUpdate_WhenSupportRemoved_DropsLever()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(-1, 64, 0, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("lever").Id, 1); // attached to west block

        world.ReaderWriter.SetBlock(-1, 64, 0, 0);
        TestBlocks.Get("lever").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 1, TestBlocks.Get("stone").Id));

        Assert.Equal(0, world.Reader.GetBlockId(0, 64, 0));
    }
}
