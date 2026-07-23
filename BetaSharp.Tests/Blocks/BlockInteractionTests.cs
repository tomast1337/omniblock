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

        world.ReaderWriter.SetInitial(-1, 64, 2, BlockRegistry.Get("stone").id); // lever support
        world.ReaderWriter.SetInitial(0, 63, 1, BlockRegistry.Get("stone").id); // wire support
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id); // repeater support

        world.ReaderWriter.SetInitial(0, 64, 2, BlockRegistry.Get("lever").id, 1); // unpowered lever
        world.ReaderWriter.SetInitial(0, 64, 1, BlockRegistry.Get("redstone_wire").id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("repeater").id); // 2-tick delay, facing 0
        world.ReaderWriter.SetInitial(0, 64, -1, BlockRegistry.Get("tnt").id);

        // Act 1
        BlockRegistry.Get("lever").onUse(new OnUseEvent(world, null!, 0, 64, 2));
        simulation.EnqueueInstantUpdate(0, 64, 1, BlockRegistry.Get("lever").id);
        simulation.EnqueueInstantUpdate(0, 64, 0, BlockRegistry.Get("redstone_wire").id);
        simulation.ProcessInstantQueue();

        // Assert 1
        Assert.True(world.Reader.GetBlockMeta(0, 64, 1) > 0);
        Assert.Equal(BlockRegistry.Get("repeater").id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(BlockRegistry.Get("tnt").id, world.Reader.GetBlockId(0, 64, -1));

        // Act 2
        simulation.AdvanceTime(2);

        // Assert 2
        Assert.Equal(BlockRegistry.Get("powered_repeater").id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(0, world.Reader.GetBlockId(0, 64, -1)); // primed TNT clears block
    }

    [Fact]
    public void ScheduledUpdateCancellation_RemovedRepeaterTickIsDiscarded()
    {
        // Arrange
        FakeWorldContext world = new();
        SimulationRunner simulation = new(world);
        EnableNeighborPropagation(world, simulation);

        world.ReaderWriter.SetInitial(0, 63, 1, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(0, 64, 1, BlockRegistry.Get("redstone_wire").id, 15); // powered input
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("repeater").id, 4); // delay index 1 => 4 ticks
        world.ReaderWriter.SetInitial(0, 64, -1, BlockRegistry.Get("tnt").id);

        // Act 1
        simulation.EnqueueInstantUpdate(0, 64, 0, BlockRegistry.Get("redstone_wire").id);
        simulation.ProcessInstantQueue();

        // Act 2
        world.ReaderWriter.SetBlock(0, 64, 0, 0);

        // Act 3 + Assert
        Exception? ex = Record.Exception(() => simulation.AdvanceTime(4));
        Assert.Null(ex);
        Assert.Equal(BlockRegistry.Get("tnt").id, world.Reader.GetBlockId(0, 64, -1)); // never triggered
    }

    [Fact]
    public void ButtonPulse_LatchesAndUnlatchesNoteBlockAcross20Ticks()
    {
        // Arrange
        FakeWorldContext world = new();
        SimulationRunner simulation = new(world);
        EnableNeighborPropagation(world, simulation);

        world.ReaderWriter.SetInitial(-1, 64, 0, BlockRegistry.Get("stone").id); // button support
        world.ReaderWriter.SetInitial(1, 63, 0, BlockRegistry.Get("stone").id); // wire support
        world.ReaderWriter.SetInitial(2, 63, 0, BlockRegistry.Get("stone").id); // note instrument base
        world.ReaderWriter.SetInitial(2, 65, 0, 0); // air above note

        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("button").id, 1);
        world.ReaderWriter.SetInitial(1, 64, 0, BlockRegistry.Get("redstone_wire").id);
        world.ReaderWriter.SetInitial(2, 64, 0, BlockRegistry.Get("noteblock").id);

        BlockEntityNote note = new();
        world.Entities.SetBlockEntity(2, 64, 0, note);

        // Act 1
        BlockRegistry.Get("button").onUse(new OnUseEvent(world, null!, 0, 64, 0));
        simulation.EnqueueInstantUpdate(1, 64, 0, BlockRegistry.Get("button").id);
        simulation.EnqueueInstantUpdate(2, 64, 0, BlockRegistry.Get("redstone_wire").id);
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
        simulation.EnqueueInstantUpdate(1, 64, 0, BlockRegistry.Get("button").id);
        simulation.EnqueueInstantUpdate(2, 64, 0, BlockRegistry.Get("redstone_wire").id);
        simulation.ProcessInstantQueue();

        // Assert 3
        Assert.True((world.Reader.GetBlockMeta(0, 64, 0) & 8) == 0);
        Assert.Equal(0, world.Reader.GetBlockMeta(1, 64, 0));
        Assert.False(note.powered);
    }
}
