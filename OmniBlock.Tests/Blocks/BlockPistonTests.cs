using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Blocks.Entities;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockPistonTests
{
    [Fact]
    public void NeighborUpdate_PushLimit12_ExtendsAndPushes()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("piston").Id, 5); // facing +X
        for (var x = 1; x <= 12; x++)
        {
            world.ReaderWriter.SetInitial(x, 64, 0, TestBlocks.Get("stone").Id);
        }

        world.ReaderWriter.SetInitial(0, 66, 0, TestBlocks.Get("lit_redstone_torch").Id); // quasi power

        TestBlocks.Get("piston").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 5, TestBlocks.Get("lit_redstone_torch").Id));

        Assert.Equal(13, world.Reader.GetBlockMeta(0, 64, 0)); // extended
        Assert.Equal(TestBlocks.Get("moving_piston").Id, world.Reader.GetBlockId(1, 64, 0)); // head extension mover
        Assert.Equal(TestBlocks.Get("moving_piston").Id, world.Reader.GetBlockId(13, 64, 0)); // farthest pushed mover
    }

    [Fact]
    public void NeighborUpdate_QuasiConnectivity_TwoBlocksAbovePower_ExtendsPiston()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("piston").Id, 5); // facing +X
        world.ReaderWriter.SetInitial(0, 66, 0, TestBlocks.Get("lit_redstone_torch").Id); // quasi power source at y+2

        TestBlocks.Get("piston").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 5, TestBlocks.Get("lit_redstone_torch").Id));

        Assert.Equal(13, world.Reader.GetBlockMeta(0, 64, 0)); // facing + extended
        Assert.Equal(TestBlocks.Get("moving_piston").Id, world.Reader.GetBlockId(1, 64, 0));
        Assert.NotNull(world.Entities.GetBlockEntity<BlockEntityPiston>(1, 64, 0));
    }

    [Fact]
    public void OnBlockAction_Retract_NonStickyConvertsBaseToMovingAndClearsHead()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("piston").Id, 13); // facing +X, extended
        world.ReaderWriter.SetInitial(1, 64, 0, TestBlocks.Get("piston_head").Id, 5);

        TestBlocks.Get("piston").OnBlockAction(new OnBlockActionEvent(world, 1, 5, 0, 64, 0));

        Assert.Equal(TestBlocks.Get("moving_piston").Id, world.Reader.GetBlockId(0, 64, 0));
        Assert.NotNull(world.Entities.GetBlockEntity<BlockEntityPiston>(0, 64, 0));
        Assert.Equal(0, world.Reader.GetBlockId(1, 64, 0));
    }

    [Fact]
    public void PistonExtension_OnBreak_RemovesExtendedBasePistonBehind()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("piston").Id, 13); // extended base
        world.ReaderWriter.SetInitial(1, 64, 0, TestBlocks.Get("piston_head").Id, 5); // extension facing +X

        TestBlocks.Get("piston_head").OnBreak(new OnBreakEvent(world, null, 1, 64, 0));

        Assert.Equal(0, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void PistonMoving_OnUse_WithoutBlockEntity_RemovesMovingBlock()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("moving_piston").Id);

        var handled = TestBlocks.Get("moving_piston").OnUse(new OnUseEvent(world, null!, 0, 64, 0));

        Assert.True(handled);
        Assert.Equal(0, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void StickyRetract_PullsRegularBlockIntoMovingState()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("sticky_piston").Id, 13); // facing +X, extended
        world.ReaderWriter.SetInitial(1, 64, 0, TestBlocks.Get("piston_head").Id, 5);
        world.ReaderWriter.SetInitial(2, 64, 0, TestBlocks.Get("stone").Id);

        TestBlocks.Get("sticky_piston").OnBlockAction(new OnBlockActionEvent(world, 1, 5, 0, 64, 0));

        Assert.Equal(TestBlocks.Get("moving_piston").Id, world.Reader.GetBlockId(0, 64, 0)); // retracting base
        Assert.Equal(0, world.Reader.GetBlockId(2, 64, 0)); // source cleared
        Assert.Equal(TestBlocks.Get("moving_piston").Id, world.Reader.GetBlockId(1, 64, 0)); // pulled block now moving at head

        var pulled = world.Entities.GetBlockEntity<BlockEntityPiston>(1, 64, 0);
        Assert.NotNull(pulled);
        Assert.Equal(TestBlocks.Get("stone").Id, pulled.PushedBlockId);
        Assert.False(pulled.IsExtending);
    }

    [Fact]
    public void StickyRetract_ShortPulseAbandonsExtension_LeavesBlockBehind()
    {
        FakeWorldContext world = new();
        world.IsRemote = true; // Avoid broadcaster world-context path during extension abandon finalization.
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("sticky_piston").Id, 13); // facing +X, extended
        world.ReaderWriter.SetInitial(1, 64, 0, TestBlocks.Get("piston_head").Id, 5);
        world.ReaderWriter.SetInitial(2, 64, 0, TestBlocks.Get("moving_piston").Id);
        world.Entities.SetBlockEntity(2, 64, 0, PistonMovingBehavior.CreatePistonBlockEntity(TestBlocks.Get("stone").Id, 0, 5, true, false));

        TestBlocks.Get("sticky_piston").OnBlockAction(new OnBlockActionEvent(world, 1, 5, 0, 64, 0));

        Assert.Equal(TestBlocks.Get("stone").Id, world.Reader.GetBlockId(2, 64, 0)); // extension finalized to static block
        Assert.Equal(0, world.Reader.GetBlockId(1, 64, 0)); // sticky spit clears head space
    }

    [Fact]
    public void NeighborUpdate_PushBlockedByObsidian_DoesNotExtend()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("piston").Id, 5); // facing +X
        world.ReaderWriter.SetInitial(1, 64, 0, TestBlocks.Get("obsidian").Id); // immovable in front
        world.ReaderWriter.SetInitial(0, 66, 0, TestBlocks.Get("lit_redstone_torch").Id); // quasi power

        TestBlocks.Get("piston").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 5, TestBlocks.Get("lit_redstone_torch").Id));

        Assert.Equal(5, world.Reader.GetBlockMeta(0, 64, 0)); // not extended
        Assert.NotEqual(TestBlocks.Get("moving_piston").Id, world.Reader.GetBlockId(1, 64, 0));
    }

    [Fact]
    public void NeighborUpdate_PushBlockedByBlockEntity_DoesNotExtend()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("piston").Id, 5); // facing +X
        world.ReaderWriter.SetInitial(1, 64, 0, TestBlocks.Get("chest").Id);
        world.Entities.SetBlockEntity(1, 64, 0, new BlockEntityChest()); // immovable because block entity present
        world.ReaderWriter.SetInitial(0, 66, 0, TestBlocks.Get("lit_redstone_torch").Id); // quasi power

        TestBlocks.Get("piston").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 5, TestBlocks.Get("lit_redstone_torch").Id));

        Assert.Equal(5, world.Reader.GetBlockMeta(0, 64, 0)); // stays unextended
        Assert.Equal(TestBlocks.Get("chest").Id, world.Reader.GetBlockId(1, 64, 0));
    }

    [Fact]
    public void NeighborUpdate_PushChainBeyondLimit_DoesNotExtend()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("piston").Id, 5); // facing +X
        for (var x = 1; x <= 13; x++)
        {
            world.ReaderWriter.SetInitial(x, 64, 0, TestBlocks.Get("stone").Id);
        }

        world.ReaderWriter.SetInitial(0, 66, 0, TestBlocks.Get("lit_redstone_torch").Id); // quasi power

        TestBlocks.Get("piston").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 5, TestBlocks.Get("lit_redstone_torch").Id));

        Assert.Equal(5, world.Reader.GetBlockMeta(0, 64, 0)); // not extended
        Assert.Equal(TestBlocks.Get("stone").Id, world.Reader.GetBlockId(1, 64, 0)); // front block unchanged
    }

    [Fact]
    public void StickyRetract_UnpullableBlockRetractsHeadButLeavesBlock()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("sticky_piston").Id, 13); // facing +X, extended
        world.ReaderWriter.SetInitial(1, 64, 0, TestBlocks.Get("piston_head").Id, 5);
        world.ReaderWriter.SetInitial(2, 64, 0, TestBlocks.Get("door").Id); // piston behavior 1 => not pullable

        TestBlocks.Get("sticky_piston").OnBlockAction(new OnBlockActionEvent(world, 1, 5, 0, 64, 0));

        Assert.Equal(TestBlocks.Get("moving_piston").Id, world.Reader.GetBlockId(0, 64, 0)); // base retract animation
        Assert.Equal(0, world.Reader.GetBlockId(1, 64, 0)); // head removed
        Assert.Equal(TestBlocks.Get("door").Id, world.Reader.GetBlockId(2, 64, 0)); // target left in place
    }

    [Fact]
    public void QuasiConnectivity_BudStyle_NoImmediateUpdateUntilNeighborEvent()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("piston").Id, 5); // facing +X
        world.ReaderWriter.SetInitial(1, 64, 0, 0);
        world.ReaderWriter.SetInitial(1, 65, 0, TestBlocks.Get("lit_redstone_torch").Id); // diagonal-above style power source

        // No neighborUpdate yet => no state change
        Assert.Equal(5, world.Reader.GetBlockMeta(0, 64, 0));

        // Adjacent block update event arrives (BUD-like trigger)
        TestBlocks.Get("piston").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 5, TestBlocks.Get("stone").Id));

        Assert.Equal(13, world.Reader.GetBlockMeta(0, 64, 0));
    }

    [Fact]
    public void OpposingPistons_RapidNeighborUpdates_DoesNotThrow()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("piston").Id, 5); // faces +X
        world.ReaderWriter.SetInitial(2, 64, 0, TestBlocks.Get("piston").Id, 4); // faces -X
        world.ReaderWriter.SetInitial(0, 66, 0, TestBlocks.Get("lit_redstone_torch").Id);
        world.ReaderWriter.SetInitial(2, 66, 0, TestBlocks.Get("lit_redstone_torch").Id);

        var ex = Record.Exception(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                TestBlocks.Get("piston").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, world.Reader.GetBlockMeta(0, 64, 0), TestBlocks.Get("stone").Id));
                TestBlocks.Get("piston").NeighborUpdate(new OnTickEvent(world, 2, 64, 0, world.Reader.GetBlockMeta(2, 64, 0), TestBlocks.Get("stone").Id));
            }
        });

        Assert.Null(ex);
    }
}
