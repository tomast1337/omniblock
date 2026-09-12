using OmniBlock.Entities;
using OmniBlock.Blocks.Entities;
using OmniBlock.Network.Messages;
using OmniBlock.Network;
using OmniBlock.NBT;
using OmniBlock.Registries;
using OmniBlock.Server;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Tests.Worlds;

public sealed class SimulationDistanceTests
{
    [Theory]
    [InlineData(8, 32, 8)]
    [InlineData(32, 8, 8)]
    [InlineData(1, 32, 2)]
    [InlineData(64, 64, 32)]
    public void Effective_distance_is_bounded_independently_but_never_exceeds_streaming(
        int requested,
        int renderDistance,
        int expected) =>
        Assert.Equal(expected, OmniBlockServer.GetEffectiveSimulationDistance(requested, renderDistance));

    [Fact]
    public void Scheduled_tick_pauses_outside_simulation_and_keeps_its_original_entry()
    {
        FakeWorldContext world = new()
        {
            SimulatedWorldTime = 100,
            SimulationActive = static (_, _) => false
        };
        var stone = world.Content.Blocks.Get("stone");
        world.ReaderWriter.SetBlock(160, 64, 0, stone.Id);
        WorldTickScheduler scheduler = new(world);
        scheduler.ScheduleBlockUpdateFromChunkLoad(160, 64, 0, stone.Id, 0);
        var original = Assert.Single(scheduler.GetPendingTicksInChunk(10, 0));

        scheduler.Tick();
        Assert.Equal(1, scheduler.Count);
        Assert.Equal(original, Assert.Single(scheduler.GetPendingTicksInChunk(10, 0)));

        world.SimulationActive = static (_, _) => true;
        scheduler.Tick();
        Assert.Equal(0, scheduler.Count);
    }

    [Fact]
    public void Ordinary_entities_pause_outside_simulation_and_resume_in_place()
    {
        FakeWorldContext world = new()
        {
            SimulationActive = static (_, _) => false
        };
        CountingEntity entity = new(world);
        entity.SetPosition(160.5, 64, 0.5);
        world.Entities.SpawnEntity(entity);

        world.Entities.TickEntities();
        Assert.Equal(0, entity.TickCount);

        world.SimulationActive = static (_, _) => true;
        world.Entities.TickEntities();
        Assert.Equal(1, entity.TickCount);
    }

    [Fact]
    public void Block_entities_pause_outside_simulation_and_resume_in_place()
    {
        FakeWorldContext world = new()
        {
            SimulationActive = static (_, _) => false
        };
        var chest = world.Content.Blocks.Get("chest");
        world.ReaderWriter.SetBlock(160, 64, 0, chest.Id);
        CountingBlockEntity entity = new()
        {
            World = world,
            X = 160,
            Y = 64,
            Z = 0
        };
        world.Entities.SetBlockEntity(entity.X, entity.Y, entity.Z, entity);

        world.Entities.TickEntities();
        Assert.Equal(0, entity.TickCount);

        world.SimulationActive = static (_, _) => true;
        world.Entities.TickEntities();
        Assert.Equal(1, entity.TickCount);
    }

    [Fact]
    public void Session_distance_message_round_trips_both_authoritative_radii()
    {
        SessionDistanceMessage sent = new()
        {
            RenderDistance = 32,
            SimulationDistance = 8
        };
        using MemoryStream stream = new();
        sent.Write(stream);
        Assert.Equal(stream.Length, sent.Size());
        stream.Position = 0;

        SessionDistanceMessage received = new();
        received.Read(stream);

        Assert.Equal(32, received.RenderDistance);
        Assert.Equal(8, received.SimulationDistance);
        Assert.Equal(stream.Length, stream.Position);

        MessageRegistry registry = new();
        DefaultMessages.RegisterAll(registry, ContentRuntime.Current.Items);
        registry.NegotiateAsServer();
        var id = registry.GetId(SessionDistanceMessage.Id);
        Assert.True(id >= 0);
        Assert.IsType<SessionDistanceMessage>(registry.Create(id));
    }

    private sealed class CountingEntity(IWorldContext world) : Entity(world, null)
    {
        public int TickCount { get; private set; }

        public override void Tick() => TickCount++;

        protected override void ReadNbt(NBTTagCompound nbt)
        {
        }

        protected override void WriteNbt(NBTTagCompound nbt)
        {
        }
    }

    private sealed class CountingBlockEntity : BlockEntity
    {
        protected override BlockEntityType Type => Generic;
        public int TickCount { get; private set; }

        public override void Tick(EntityManager entities) => TickCount++;
    }
}
