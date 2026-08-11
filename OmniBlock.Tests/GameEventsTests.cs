using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Tests.TestSupport;

namespace OmniBlock.Tests;

[Collection("EntityTests")]
public sealed class GameEventsTests
{
    [Fact]
    public void BlockPlaced_publishes_position_id_and_meta()
    {
        FakeWorldContext world = new();
        List<BlockPlacedEvent> captured = [];
        Action<BlockPlacedEvent> handler = e => captured.Add(e);
        GameEvents.BlockPlaced += handler;
        try
        {
            world.ReaderWriter.SetInitial(101, 64, 202, BlockRegistry.Get("stone").Id, meta: 5);
            BlockRegistry.Get("stone").OnPlaced(new OnPlacedEvent(world, null, Side.Up, Side.Up, 101, 64, 202));

            Assert.Contains(captured, e => e is { X: 101, Y: 64, Z: 202, Meta: 5 } && e.BlockId == BlockRegistry.Get("stone").Id);
        }
        finally
        {
            GameEvents.BlockPlaced -= handler;
        }
    }

    [Fact]
    public void BlockBreak_publishes_position_and_id()
    {
        FakeWorldContext world = new();
        List<BlockBreakEvent> captured = [];
        Action<BlockBreakEvent> handler = e => captured.Add(e);
        GameEvents.BlockBreak += handler;
        try
        {
            world.ReaderWriter.SetInitial(103, 64, 204, BlockRegistry.Get("dirt").Id);
            BlockRegistry.Get("dirt").OnBreak(new OnBreakEvent(world, null, 103, 64, 204));

            Assert.Contains(captured, e => e is { X: 103, Y: 64, Z: 204 } && e.BlockId == BlockRegistry.Get("dirt").Id);
        }
        finally
        {
            GameEvents.BlockBreak -= handler;
        }
    }

    [Fact]
    public void BlockPlaced_supports_multiple_independent_subscribers()
    {
        FakeWorldContext world = new();
        int firstCount = 0;
        int secondCount = 0;
        Action<BlockPlacedEvent> first = _ => firstCount++;
        Action<BlockPlacedEvent> second = _ => secondCount++;
        GameEvents.BlockPlaced += first;
        GameEvents.BlockPlaced += second;
        try
        {
            world.ReaderWriter.SetInitial(107, 64, 208, BlockRegistry.Get("stone").Id);
            BlockRegistry.Get("stone").OnPlaced(new OnPlacedEvent(world, null, Side.Up, Side.Up, 107, 64, 208));

            Assert.True(firstCount > 0);
            Assert.True(secondCount > 0);
        }
        finally
        {
            GameEvents.BlockPlaced -= first;
            GameEvents.BlockPlaced -= second;
        }
    }

    [Fact]
    public void EntityHurt_publishes_entity_attacker_and_amount()
    {
        FakeWorldContext world = new();
        EntityCreature wolf = (EntityCreature)EntityRegistry.ByName("wolf").Create(world);
        wolf.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(wolf));

        var player = new TestEntityPlayer(world) { Name = "hurt-tester" };
        player.SetPositionAndAngles(9.0, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));

        List<EntityHurtEvent> captured = [];
        Action<EntityHurtEvent> handler = e => captured.Add(e);
        GameEvents.EntityHurt += handler;
        try
        {
            Assert.True(wolf.Damage(player, 4));

            Assert.Contains(captured, e => e.EntityId == wolf.ID && e.AttackerId == player.ID && e.Amount == 4);
        }
        finally
        {
            GameEvents.EntityHurt -= handler;
        }
    }

    [Fact]
    public void EntityHurt_reports_null_attacker_for_environmental_damage()
    {
        FakeWorldContext world = new();
        EntityCreature wolf = (EntityCreature)EntityRegistry.ByName("wolf").Create(world);
        wolf.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(wolf));

        List<EntityHurtEvent> captured = [];
        Action<EntityHurtEvent> handler = e => captured.Add(e);
        GameEvents.EntityHurt += handler;
        try
        {
            Assert.True(wolf.Damage(null, 2));

            Assert.Contains(captured, e => e.EntityId == wolf.ID && e.AttackerId == null && e.Amount == 2);
        }
        finally
        {
            GameEvents.EntityHurt -= handler;
        }
    }
}
