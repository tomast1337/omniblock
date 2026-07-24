using System.Text.Json;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Behaviors;
using BetaSharp.Blocks.Entities;
using BetaSharp.Entities;

namespace BetaSharp.Tests.Blocks;

public sealed class BlockFluidTests
{
    [Fact]
    public void BehaviorRegistry_Build_StationaryFluid_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"stationary_fluid","source_solidified":"betasharp:obsidian","flow_solidified":"betasharp:cobblestone"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("stationary_fluid", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_StationaryFluid_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"stationary_fluid","ignition_target":"betasharp:fire","source_solidified":"not_a_real_block","flow_solidified":"betasharp:cobblestone"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("stationary_fluid", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_FlowingFluid_MissingRequiredProperty_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"flowing_fluid","source_solidified":"betasharp:obsidian","flow_solidified":"betasharp:cobblestone"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("flowing_fluid", json.RootElement));
    }

    [Fact]
    public void BehaviorRegistry_Build_FlowingFluid_UnknownBlockName_Throws()
    {
        using JsonDocument json = JsonDocument.Parse("""{"Type":"flowing_fluid","passable":["not_a_real_block"],"source_solidified":"betasharp:obsidian","flow_solidified":"betasharp:cobblestone"}""");
        Assert.Throws<KeyNotFoundException>(() => BehaviorRegistry.Build("flowing_fluid", json.RootElement));
    }

    [Fact]
    public void LavaNeighborUpdate_WithMetaZeroAndAdjacentWater_HardensToObsidian()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("lava").id, 0);
        world.ReaderWriter.SetInitial(1, 64, 0, BlockRegistry.Get("water").id, 0);

        BlockRegistry.Get("lava").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, BlockRegistry.Get("water").id));

        Assert.Equal(BlockRegistry.Get("obsidian").id, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void FlowingLavaNeighborUpdate_WithMetaBetweenOneAndFourAndAdjacentWater_HardensToCobblestone()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("flowing_lava").id, 3);
        world.ReaderWriter.SetInitial(1, 64, 0, BlockRegistry.Get("water").id, 0);

        BlockRegistry.Get("flowing_lava").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 3, BlockRegistry.Get("water").id));

        Assert.Equal(BlockRegistry.Get("cobblestone").id, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void FlowingLavaNeighborUpdate_WithMetaFourAndAdjacentWater_HardensToCobblestone()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("flowing_lava").id, 4);
        world.ReaderWriter.SetInitial(1, 64, 0, BlockRegistry.Get("water").id, 0);

        BlockRegistry.Get("flowing_lava").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 4, BlockRegistry.Get("water").id));

        Assert.Equal(BlockRegistry.Get("cobblestone").id, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void FlowingLavaNeighborUpdate_WithMetaAboveFourAndAdjacentWater_DoesNotHarden()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("flowing_lava").id, 5);
        world.ReaderWriter.SetInitial(1, 64, 0, BlockRegistry.Get("water").id, 0);

        BlockRegistry.Get("flowing_lava").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 5, BlockRegistry.Get("water").id));

        Assert.Equal(BlockRegistry.Get("flowing_lava").id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(5, world.Reader.GetBlockMeta(0, 64, 0));
    }

    [Fact]
    public void LavaNeighborUpdate_WithOnlyWaterBelow_DoesNotHarden()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("lava").id, 0);
        world.ReaderWriter.SetInitial(0, 63, 0, BlockRegistry.Get("water").id, 0);

        BlockRegistry.Get("lava").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, BlockRegistry.Get("water").id));

        // Water below is not adjacent for lava/water hardening; still lava may convert to flowing on neighbor tick.
        Assert.NotEqual(BlockRegistry.Get("obsidian").id, world.Reader.GetBlockId(0, 64, 0));
        Assert.NotEqual(BlockRegistry.Get("cobblestone").id, world.Reader.GetBlockId(0, 64, 0));
        Assert.Equal(0, world.Reader.GetBlockMeta(0, 64, 0));
    }

    [Fact]
    public void LavaNeighborUpdate_WithWaterAbove_HardensToObsidian()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("lava").id, 0);
        world.ReaderWriter.SetInitial(0, 65, 0, BlockRegistry.Get("water").id, 0);

        BlockRegistry.Get("lava").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 0, BlockRegistry.Get("water").id));

        Assert.Equal(BlockRegistry.Get("obsidian").id, world.Reader.GetBlockId(0, 64, 0));
    }

    [Fact]
    public void FlowingLavaNeighborUpdate_WhenHardening_EmitsFizzWorldEventAndSmokeParticles()
    {
        FakeWorldContext world = new();
        RecordingWorldEventListener listener = new();
        world.Broadcaster.AddWorldAccess(listener);
        world.ReaderWriter.SetInitial(0, 64, 0, BlockRegistry.Get("flowing_lava").id, 3);
        world.ReaderWriter.SetInitial(1, 64, 0, BlockRegistry.Get("water").id, 0);

        BlockRegistry.Get("flowing_lava").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 3, BlockRegistry.Get("water").id));

        Assert.Contains(listener.WorldEvents, evt => evt.EventId == 1004 && evt.X == 0 && evt.Y == 64 && evt.Z == 0);
        Assert.Equal(8, listener.LargeSmokeParticles);
    }

    private sealed class RecordingWorldEventListener : IWorldEventListener
    {
        public List<(int EventId, int X, int Y, int Z, int Data)> WorldEvents { get; } = [];
        public int LargeSmokeParticles { get; private set; }

        public void BlockUpdate(int x, int y, int z)
        {
        }

        public void SetBlocksDirty(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
        {
        }

        public void PlaySound(string var1, double var2, double var4, double var6, float var8, float var9)
        {
        }

        public void SpawnParticle(string var1, double var2, double var4, double var6, double var8, double var10, double var12)
        {
            if (var1 == "largesmoke")
            {
                LargeSmokeParticles++;
            }
        }

        public void NotifyEntityAdded(Entity var1)
        {
        }

        public void NotifyEntityRemoved(Entity var1)
        {
        }

        public void NotifyAmbientDarknessChanged()
        {
        }

        public void PlayNote(int x, int y, int z, int soundType, int pitch)
        {
        }

        public void PlayStreaming(string var1, int var2, int var3, int var4)
        {
        }

        public void UpdateBlockEntity(int var1, int var2, int var3, BlockEntity var4)
        {
        }

        public void WorldEvent(EntityPlayer? player, int @event, int x, int y, int z, int data)
        {
            WorldEvents.Add((@event, x, y, z, data));
        }

        public void BroadcastEntityEvent(Entity entity, byte @event)
        {
        }
    }
}
