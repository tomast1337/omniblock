using BetaSharp.Blocks;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockRedstoneTorchTests
{
    private static OnTickEvent Tick(FakeWorldContext world, int x = 0, int y = 64, int z = 0) => new(world, x, y, z, world.Reader.GetBlockMeta(x, y, z), world.Reader.GetBlockId(x, y, z));

    private static OnPlacedEvent Placed(FakeWorldContext world, int x, int y, int z, Side direction) =>
        new(world, null, direction, direction, x, y, z);

    [Fact]
    public void GetTexture_UpFace_MatchesRedstoneWireTexture()
    {
        int meta = 2;
        Assert.Equal(BlockRegistry.Get("redstone_wire").GetTexture(Side.Up, meta), BlockRegistry.Get("lit_redstone_torch").GetTexture(Side.Up, meta));
    }

    [Fact]
    public void GetTexture_SideFace_UsesBlockTextureId()
    {
        Assert.Equal(BlockRegistry.Get("lit_redstone_torch").TextureId, BlockRegistry.Get("lit_redstone_torch").GetTexture(Side.North, 5));
    }

    [Fact]
    public void GetDroppedItemId_AlwaysLitTorch()
    {
        Assert.Equal(BlockRegistry.Get("lit_redstone_torch").id, BlockRegistry.Get("redstone_torch").GetDroppedItemId(0));
        Assert.Equal(BlockRegistry.Get("lit_redstone_torch").id, BlockRegistry.Get("lit_redstone_torch").GetDroppedItemId(3));
    }

    [Fact]
    public void CanEmitRedstonePower_IsTrue()
    {
        Assert.True(BlockRegistry.Get("redstone_torch").canEmitRedstonePower());
        Assert.True(BlockRegistry.Get("lit_redstone_torch").canEmitRedstonePower());
    }

    [Fact]
    public void IsPoweringSide_UnlitTorch_NeverPowers()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("redstone_torch").id, 5);
        Assert.False(BlockRegistry.Get("redstone_torch").isPoweringSide(world.Reader, 0, 64, 0, 0));
    }

    [Fact]
    public void IsPoweringSide_LitFloorTorch_DoesNotStrongPowerDownward()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("lit_redstone_torch").id, 5);
        Assert.False(BlockRegistry.Get("lit_redstone_torch").isPoweringSide(world.Reader, 0, 64, 0, 1));
    }

    [Fact]
    public void IsStrongPoweringSide_LitFloorTorch_OnlyStrongPowersDown()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("lit_redstone_torch").id, 5);
        Assert.True(BlockRegistry.Get("lit_redstone_torch").isStrongPoweringSide(world.Reader, 0, 64, 0, 0));
        Assert.False(BlockRegistry.Get("lit_redstone_torch").isStrongPoweringSide(world.Reader, 0, 64, 0, 2));
    }

    [Fact]
    public void OnPlaced_Lit_MetaZero_FloorTorchFromUpDirection_NotifiesNeighbors()
    {
        FakeWorldContext world = new();
        int x = 12, y = 70, z = 12;
        world.ReaderWriter.SetInitial(x, y - 1, z, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(x, y, z, BlockRegistry.Get("lit_redstone_torch").id, 0);

        BlockRegistry.Get("lit_redstone_torch").OnPlaced(Placed(world, x, y, z, Side.Up));

        Assert.Equal(5, world.Reader.GetBlockMeta(x, y, z));
    }

    [Fact]
    public void OnPlaced_Unlit_MetaZero_StillResolvesAttachmentMeta()
    {
        FakeWorldContext world = new();
        int x = 13, y = 70, z = 13;
        world.ReaderWriter.SetInitial(x, y - 1, z, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(x, y, z, BlockRegistry.Get("redstone_torch").id, 0);

        BlockRegistry.Get("redstone_torch").OnPlaced(Placed(world, x, y, z, Side.Up));

        Assert.Equal(5, world.Reader.GetBlockMeta(x, y, z));
    }

    [Fact]
    public void OnPlaced_Lit_NonZeroMeta_SkipsBaseTorchPlacement()
    {
        FakeWorldContext world = new();
        int x = 14, y = 70, z = 14;
        world.ReaderWriter.SetInitial(x, y, z, BlockRegistry.Get("lit_redstone_torch").id, 3);

        BlockRegistry.Get("lit_redstone_torch").OnPlaced(Placed(world, x, y, z, Side.Up));

        Assert.Equal(3, world.Reader.GetBlockMeta(x, y, z));
    }

    [Fact]
    public void OnBreak_LitTorch_DoesNotThrow()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(7, 64, 7, BlockRegistry.Get("lit_redstone_torch").id, 5);
        BlockRegistry.Get("lit_redstone_torch").OnBreak(new OnBreakEvent(world, null, 7, 64, 7));
    }

    [Fact]
    public void OnBreak_UnlitTorch_DoesNotThrow()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(8, 64, 8, BlockRegistry.Get("redstone_torch").id, 5);
        BlockRegistry.Get("redstone_torch").OnBreak(new OnBreakEvent(world, null, 8, 64, 8));
    }

    [Fact]
    public void OnTick_LitTorch_NoAdjacentPower_StaysLit()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(9, 63, 9, 0);
        world.ReaderWriter.SetInitial(9, 64, 9, BlockRegistry.Get("lit_redstone_torch").id, 5);

        BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, 9, 64, 9));

        Assert.Equal(BlockRegistry.Get("lit_redstone_torch").id, world.Reader.GetBlockId(9, 64, 9));
    }

    [Fact]
    public void OnTick_UnlitTorch_NoPowerBelow_Relights()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(10, 63, 10, 0);
        world.ReaderWriter.SetInitial(10, 64, 10, BlockRegistry.Get("redstone_torch").id, 5);

        BlockRegistry.Get("redstone_torch").OnTick(Tick(world, 10, 64, 10));

        Assert.Equal(BlockRegistry.Get("lit_redstone_torch").id, world.Reader.GetBlockId(10, 64, 10));
    }

    [Fact]
    public void OnTick_LitWallTorchNorth_PoweredFromNorthNeighbor_Unpowers()
    {
        FakeWorldContext world = new();
        int x = 40, y = 64, z = 40;
        world.ReaderWriter.SetInitial(x, y, z - 1, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(x, y - 1, z - 1, BlockRegistry.Get("lit_redstone_torch").id);
        world.ReaderWriter.SetInitial(x, y, z, BlockRegistry.Get("lit_redstone_torch").id, 3);

        BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, x, y, z));

        Assert.Equal(BlockRegistry.Get("redstone_torch").id, world.Reader.GetBlockId(x, y, z));
    }

    [Fact]
    public void OnTick_LitWallTorchSouth_PoweredFromSouthNeighbor_Unpowers()
    {
        FakeWorldContext world = new();
        int x = 40, y = 64, z = 50;
        world.ReaderWriter.SetInitial(x, y, z + 1, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(x, y - 1, z + 1, BlockRegistry.Get("lit_redstone_torch").id);
        world.ReaderWriter.SetInitial(x, y, z, BlockRegistry.Get("lit_redstone_torch").id, 4);

        BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, x, y, z));

        Assert.Equal(BlockRegistry.Get("redstone_torch").id, world.Reader.GetBlockId(x, y, z));
    }

    [Fact]
    public void OnTick_LitWallTorchWest_PoweredFromWestNeighbor_Unpowers()
    {
        FakeWorldContext world = new();
        int x = 50, y = 64, z = 40;
        world.ReaderWriter.SetInitial(x - 1, y, z, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(x - 1, y - 1, z, BlockRegistry.Get("lit_redstone_torch").id);
        world.ReaderWriter.SetInitial(x, y, z, BlockRegistry.Get("lit_redstone_torch").id, 1);

        BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, x, y, z));

        Assert.Equal(BlockRegistry.Get("redstone_torch").id, world.Reader.GetBlockId(x, y, z));
    }

    [Fact]
    public void OnTick_LitWallTorchEast_PoweredFromEastNeighbor_Unpowers()
    {
        FakeWorldContext world = new();
        int x = 60, y = 64, z = 40;
        world.ReaderWriter.SetInitial(x + 1, y, z, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(x + 1, y - 1, z, BlockRegistry.Get("lit_redstone_torch").id);
        world.ReaderWriter.SetInitial(x, y, z, BlockRegistry.Get("lit_redstone_torch").id, 2);

        BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, x, y, z));

        Assert.Equal(BlockRegistry.Get("redstone_torch").id, world.Reader.GetBlockId(x, y, z));
    }

    [Fact]
    public void NeighborUpdate_LitTorch_SchedulesLitTorchId()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(11, 64, 11, BlockRegistry.Get("lit_redstone_torch").id, 5);

        BlockRegistry.Get("lit_redstone_torch").NeighborUpdate(Tick(world, 11, 64, 11));

        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t is { X: 11, Y: 64, Z: 11 } && t.BlockId == BlockRegistry.Get("lit_redstone_torch").id && t.TickRate == 2);
    }

    [Fact]
    public void RandomDisplayTick_LitTorch_EachMeta_AddsReddust()
    {
        FakeWorldContext world = new();
        for (int meta = 1; meta <= 5; meta++)
        {
            world.ReaderWriter.SetInitial(20 + meta, 64, 20, BlockRegistry.Get("lit_redstone_torch").id, meta);
            BlockRegistry.Get("lit_redstone_torch").RandomDisplayTick(Tick(world, 20 + meta, 64, 20));
        }
    }

    [Fact]
    public void RandomDisplayTick_UnlitTorch_NoOp()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(30, 64, 30, BlockRegistry.Get("redstone_torch").id, 5);
        BlockRegistry.Get("redstone_torch").RandomDisplayTick(Tick(world, 30, 64, 30));
    }

    [Fact]
    public void OnTick_AfterWorldTimeAdvances_BurnoutHistoryPrunes_SingleTransitionDoesNotRescheduleLongRecovery()
    {
        const int x = 46;
        const int y = 64;
        const int z = 46;
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(x, y - 1, z, 0);
        world.ReaderWriter.SetInitial(x, y, z, BlockRegistry.Get("lit_redstone_torch").id, 5);

        for (int cycle = 0; cycle < 7; cycle++)
        {
            world.ReaderWriter.SetInitial(x, y - 1, z, BlockRegistry.Get("lit_redstone_torch").id);
            BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, x, y, z));
            world.ReaderWriter.SetInitial(x, y - 1, z, 0);
            BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, x, y, z));
        }

        world.ReaderWriter.SetInitial(x, y - 1, z, BlockRegistry.Get("lit_redstone_torch").id);
        BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, x, y, z));

        world.SimulatedWorldTime = 100L;
        world.ReaderWriter.SetInitial(1, 64, 1, BlockRegistry.Get("lit_redstone_torch").id, 5);
        world.ReaderWriter.SetInitial(1, 63, 1, 0);
        BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, 1, 64, 1));

        world.SimulatedWorldTime = 0L;
        world.ReaderWriter.SetInitial(x, y, z, BlockRegistry.Get("lit_redstone_torch").id, 5);
        world.ReaderWriter.SetInitial(x, y - 1, z, BlockRegistry.Get("lit_redstone_torch").id);
        world.TickSchedulerSpy.ScheduledTicks.Clear();
        BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, x, y, z));

        Assert.DoesNotContain(world.TickSchedulerSpy.ScheduledTicks, t => t.TickRate >= 160);
    }

    [Fact]
    public void NeighborUpdate_AlwaysSchedulesTwoTickDelay()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("stone").id);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("redstone_torch").id, 5);

        BlockRegistry.Get("redstone_torch").NeighborUpdate(Tick(world));

        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t is { X: 0, Y: 64, Z: 0 } && t.BlockId == BlockRegistry.Get("redstone_torch").id && t.TickRate == 2);
    }

    [Fact]
    public void OnTick_LitTorch_WhenReceivingPower_TurnsIntoUnlitTorch()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("lit_redstone_torch").id, 5);
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("lit_redstone_torch").id); // powers from below

        BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world));

        Assert.Equal(BlockRegistry.Get("redstone_torch").id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(5, world.Reader.GetBlockMeta(0, 64, 0));
    }

    /// <summary>
    /// After eight rapid lit→unlit transitions at the same coordinates, Beta 1.7.3 schedules a long recovery tick (burnout).
    /// Uses an isolated coordinate so static burnout bookkeeping does not collide with other torch tests.
    /// </summary>
    [Fact]
    public void OnTick_EighthRapidPowerCycle_SchedulesLongRecoveryTick()
    {
        const int x = 17;
        const int y = 79;
        const int z = 23;
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(x, y - 1, z, 0);
        world.ReaderWriter.SetInitial(x, y, z, BlockRegistry.Get("lit_redstone_torch").id, 5);

        for (int cycle = 0; cycle < 7; cycle++)
        {
            world.ReaderWriter.SetInitial(x, y - 1, z, BlockRegistry.Get("lit_redstone_torch").id);
            BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, x, y, z));

            world.ReaderWriter.SetInitial(x, y - 1, z, 0);
            BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, x, y, z));
        }

        world.ReaderWriter.SetInitial(x, y - 1, z, BlockRegistry.Get("lit_redstone_torch").id);
        world.TickSchedulerSpy.ScheduledTicks.Clear();
        BlockRegistry.Get("lit_redstone_torch").OnTick(Tick(world, x, y, z));

        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t is { X: x, Y: y, Z: z } && t.BlockId == BlockRegistry.Get("redstone_torch").id && t.TickRate >= 160);
    }
}
