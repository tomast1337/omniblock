using BetaSharp.Blocks;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockDoorTests
{
    [Fact]
    public void NeighborUpdate_TopHalfWithoutBottom_BreaksTopHalf()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 65, 0, BlockRegistry.Get("door").Id, 8); // top half only

        BlockRegistry.Get("door").NeighborUpdate(new OnTickEvent(world, 0, 65, 0, 8, BlockRegistry.Get("stone").Id));

        Assert.Equal(0, world.Reader.GetBlockId(0, 65, 0));
    }

    [Fact]
    public void NeighborUpdate_BottomHalfWithoutTop_BreaksBottomHalf()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").Id); // support below
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("door").Id); // bottom half only

        BlockRegistry.Get("door").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, BlockRegistry.Get("stone").Id));

        Assert.Equal(0, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void NeighborUpdate_RedstonePower_TogglesBottomAndTopOpenBits()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("door").Id); // bottom closed
        world.ReaderWriter.SetInitial(0, 65, 0, BlockRegistry.Get("door").Id, 8); // top closed
        world.ReaderWriter.SetInitial(-1, 64, 0, BlockRegistry.Get("lit_redstone_torch").Id); // powers door block

        BlockRegistry.Get("door").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, BlockRegistry.Get("lit_redstone_torch").Id));

        Assert.Equal(4, world.Reader.GetBlockMeta(0, 64, 0)); // bottom open
        Assert.Equal(12, world.Reader.GetBlockMeta(0, 65, 0)); // top open + top bit
    }

    [Fact]
    public void OnUse_ClickingBottomHalf_TogglesBothHalvesOpenBit()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("door").Id); // bottom closed
        world.ReaderWriter.SetInitial(0, 65, 0, BlockRegistry.Get("door").Id, 8); // top closed

        BlockRegistry.Get("door").OnUse(new OnUseEvent(world, null!, 0, 64, 0));

        Assert.Equal(4, world.Reader.GetBlockMeta(0, 64, 0)); // bottom open
        Assert.Equal(12, world.Reader.GetBlockMeta(0, 65, 0)); // top open + top bit
    }

    [Fact]
    public void OnUse_ClickingTopHalf_TogglesBothHalvesOpenBit()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("door").Id); // bottom closed
        world.ReaderWriter.SetInitial(0, 65, 0, BlockRegistry.Get("door").Id, 8); // top closed

        BlockRegistry.Get("door").OnUse(new OnUseEvent(world, null!, 0, 65, 0));

        Assert.Equal(4, world.Reader.GetBlockMeta(0, 64, 0)); // bottom open
        Assert.Equal(12, world.Reader.GetBlockMeta(0, 65, 0)); // top open + top bit
    }

    // Regression test: toggling a door must go through the notifying SetBlockMeta write (not
    // SetBlockMetaWithoutNotifyingNeighbors), or the resulting BlockUpdateS2C packet never
    // reaches other clients — the acting player's own client predicts the change locally and
    // looks correct regardless, masking the bug until a second observer (another client, or a
    // vanilla client) fails to see the door open.
    [Fact]
    public void OnUse_TogglingDoor_FiresOnBlockChangedForBothHalves()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").Id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("door").Id); // bottom closed
        world.ReaderWriter.SetInitial(0, 65, 0, BlockRegistry.Get("door").Id, 8); // top closed

        List<(int X, int Y, int Z)> changed = [];
        world.ReaderWriter.OnBlockChanged += (x, y, z, _) => changed.Add((x, y, z));

        BlockRegistry.Get("door").OnUse(new OnUseEvent(world, null!, 0, 64, 0));

        Assert.Contains((0, 64, 0), changed);
        Assert.Contains((0, 65, 0), changed);
    }
}
