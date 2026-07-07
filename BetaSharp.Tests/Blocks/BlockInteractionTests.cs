using BetaSharp.Blocks;
using BetaSharp.Blocks.Entities;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockInteractionTests
{
    private static void EnableNeighborPropagation(FakeWorldContext world, SimulationRunner simulation) =>
        world.ReaderWriter.OnBlockChanged += (x, y, z, blockId) =>
        {
            simulation.EnqueueInstantUpdate(x - 1, y, z, blockId);
            simulation.EnqueueInstantUpdate(x + 1, y, z, blockId);
            simulation.EnqueueInstantUpdate(x, y - 1, z, blockId);
            simulation.EnqueueInstantUpdate(x, y + 1, z, blockId);
            simulation.EnqueueInstantUpdate(x, y, z - 1, blockId);
            simulation.EnqueueInstantUpdate(x, y, z + 1, blockId);
        };

    [Fact]
    public void LeverToTntViaWireAndRepeater_ActivatesOnlyAfterDelay()
    {
        // Arrange
        FakeWorldContext world = new();
        SimulationRunner simulation = new(world);
        EnableNeighborPropagation(world, simulation);

        world.ReaderWriter.SetInitial(-1, 64, 2, Block.Stone.Id); // lever support
        world.ReaderWriter.SetInitial(0, 63, 1, Block.Stone.Id); // wire support
        world.ReaderWriter.SetInitial(0, 63, 0, Block.Stone.Id); // repeater support

        world.ReaderWriter.SetInitial(0, 64, 2, Block.Lever.Id, 1); // unpowered lever
        world.ReaderWriter.SetInitial(0, 64, 1, Block.RedstoneWire.Id);
        world.ReaderWriter.SetInitial(0, 64, 0, Block.Repeater.Id); // 2-tick delay, facing 0
        world.ReaderWriter.SetInitial(0, 64, -1, Block.TNT.Id);

        // Act 1
        Block.Lever.OnUse(new OnUseEvent(world, null!, 0, 64, 2));
        simulation.EnqueueInstantUpdate(0, 64, 1, Block.Lever.Id);
        simulation.EnqueueInstantUpdate(0, 64, 0, Block.RedstoneWire.Id);
        simulation.ProcessInstantQueue();

        // Assert 1
        Assert.True(world.Reader.GetBlockMeta(0, 64, 1) > 0);
        Assert.Equal(Block.Repeater.Id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(Block.TNT.Id, world.Reader.GetBlockId(0, 64, -1));

        // Act 2
        simulation.AdvanceTime(2);

        // Assert 2
        Assert.Equal(Block.PoweredRepeater.Id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(0, world.Reader.GetBlockId(0, 64, -1)); // primed TNT clears block
    }

    [Fact]
    public void ScheduledUpdateCancellation_RemovedRepeaterTickIsDiscarded()
    {
        // Arrange
        FakeWorldContext world = new();
        SimulationRunner simulation = new(world);
        EnableNeighborPropagation(world, simulation);

        world.ReaderWriter.SetInitial(0, 63, 1, Block.Stone.Id);
        world.ReaderWriter.SetInitial(0, 63, 0, Block.Stone.Id);
        world.ReaderWriter.SetInitial(0, 64, 1, Block.RedstoneWire.Id, 15); // powered input
        world.ReaderWriter.SetInitial(0, 64, 0, Block.Repeater.Id, 4); // delay index 1 => 4 ticks
        world.ReaderWriter.SetInitial(0, 64, -1, Block.TNT.Id);

        // Act 1
        simulation.EnqueueInstantUpdate(0, 64, 0, Block.RedstoneWire.Id);
        simulation.ProcessInstantQueue();

        // Act 2
        world.ReaderWriter.SetBlock(0, 64, 0, 0);

        // Act 3 + Assert
        Exception? ex = Record.Exception(() => simulation.AdvanceTime(4));
        Assert.Null(ex);
        Assert.Equal(Block.TNT.Id, world.Reader.GetBlockId(0, 64, -1)); // never triggered
    }

    [Fact]
    public void ButtonPulse_LatchesAndUnlatchesNoteBlockAcross20Ticks()
    {
        // Arrange
        FakeWorldContext world = new();
        SimulationRunner simulation = new(world);
        EnableNeighborPropagation(world, simulation);

        world.ReaderWriter.SetInitial(-1, 64, 0, Block.Stone.Id); // button support
        world.ReaderWriter.SetInitial(1, 63, 0, Block.Stone.Id); // wire support
        world.ReaderWriter.SetInitial(2, 63, 0, Block.Stone.Id); // note instrument base
        world.ReaderWriter.SetInitial(2, 65, 0, 0); // air above note

        world.ReaderWriter.SetInitial(0, 64, 0, Block.Button.Id, 1);
        world.ReaderWriter.SetInitial(1, 64, 0, Block.RedstoneWire.Id);
        world.ReaderWriter.SetInitial(2, 64, 0, Block.Noteblock.Id);

        BlockEntityNote note = new();
        world.Entities.SetBlockEntity(2, 64, 0, note);

        // Act 1
        Block.Button.OnUse(new OnUseEvent(world, null!, 0, 64, 0));
        simulation.EnqueueInstantUpdate(1, 64, 0, Block.Button.Id);
        simulation.EnqueueInstantUpdate(2, 64, 0, Block.RedstoneWire.Id);
        simulation.ProcessInstantQueue();

        // Assert 1
        Assert.True((world.Reader.GetBlockMeta(0, 64, 0) & 8) != 0);
        Assert.True(world.Reader.GetBlockMeta(1, 64, 0) > 0);
        Assert.True(note.powered);

        // Act 2
        simulation.AdvanceTime(19);

        // Assert 2
        Assert.True((world.Reader.GetBlockMeta(0, 64, 0) & 8) != 0);

        // Act 3
        simulation.AdvanceTime(1);
        simulation.EnqueueInstantUpdate(1, 64, 0, Block.Button.Id);
        simulation.EnqueueInstantUpdate(2, 64, 0, Block.RedstoneWire.Id);
        simulation.ProcessInstantQueue();

        // Assert 3
        Assert.True((world.Reader.GetBlockMeta(0, 64, 0) & 8) == 0);
        Assert.Equal(0, world.Reader.GetBlockMeta(1, 64, 0));
        Assert.False(note.powered);
    }
}
