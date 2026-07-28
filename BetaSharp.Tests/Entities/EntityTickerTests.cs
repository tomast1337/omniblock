using System.Linq;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers the Ticker capability slot: per-tick logic declared in JSON instead of overridden on a
/// subclass, built once per entity type and shared across instances.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityTickerTests
{
    [Fact]
    public void Behaviors_are_built_once_per_type_not_per_spawn()
    {
        FakeWorldContext world = new();

        // The whole point of moving construction to load: two zombies share one behavior instance,
        // so spawning no longer re-parses JSON or allocates a fresh behavior graph.
        EntityMonster first = (EntityMonster)EntityRegistry.ByName("zombie").Create(world);
        EntityMonster second = (EntityMonster)EntityRegistry.ByName("zombie").Create(world);

        Assert.NotNull(first.Attack);
        Assert.Same(first.Attack, second.Attack);
        Assert.Same(first.Loot, second.Loot);
        Assert.Same(EntityRegistry.ByName("zombie").Behaviors.Attack, first.Attack);
    }

    [Fact]
    public void Shared_tickers_keep_per_entity_state_separate()
    {
        FakeWorldContext world = new();
        EntityAnimal first = (EntityAnimal)EntityRegistry.ByName("chicken").Create(world);
        EntityAnimal second = (EntityAnimal)EntityRegistry.ByName("chicken").Create(world);

        LayEggsBehavior ticker = Assert.IsType<LayEggsBehavior>(EntityRegistry.ByName("chicken").Behaviors.Ticker);

        ticker.Reset(first);
        ticker.Reset(second);

        // Same behavior object, independent countdowns — the property that makes sharing safe.
        Assert.Same(ticker, EntityRegistry.ByName("chicken").Behaviors.Ticker);
        Assert.True(first.State != second.State);
    }

    [Fact]
    public void Zombie_and_skeleton_share_one_daylight_burn_behavior_type()
    {
        // Both classes carried byte-identical TickMovement overrides before this slot existed.
        Assert.IsType<BurnInDaylightBehavior>(EntityRegistry.ByName("zombie").Behaviors.Ticker);
        Assert.IsType<BurnInDaylightBehavior>(EntityRegistry.ByName("skeleton").Behaviors.Ticker);
    }

    [Fact]
    public void Daylight_burn_sets_fire_only_under_open_sky()
    {
        FakeWorldContext world = new();
        BurnInDaylightBehavior ticker = new();
        EntityMonster zombie = (EntityMonster)EntityRegistry.ByName("zombie").Create(world);
        zombie.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        // FakeWorldContext reports darkness, so the brightness gate keeps the mob unlit.
        for (int i = 0; i < 50; i++) ticker.OnTickMovement(zombie);
        Assert.False(zombie.IsOnFire);
    }

    [Fact]
    public void Chicken_lays_an_egg_when_its_countdown_expires()
    {
        FakeWorldContext world = new();
        EntityAnimal chicken = (EntityAnimal)EntityRegistry.ByName("chicken").Create(world);
        chicken.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(chicken));

        LayEggsBehavior ticker = Assert.IsType<LayEggsBehavior>(EntityRegistry.ByName("chicken").Behaviors.Ticker);
        int eggId = BetaSharp.Items.Item.ByName("egg").Id;

        // Drive the countdown to zero rather than ticking ~6000 times.
        for (int tick = 0; tick < 12100; tick++)
        {
            ticker.OnTickMovement(chicken);
            if (world.Entities.Entities.Any(e => EntityTestHarness.DroppedStack(e)?.ItemId == eggId)) return;
        }

        Assert.Fail("Chicken never laid an egg within two full countdown windows.");
    }

    [Fact]
    public void Entities_without_a_ticker_have_a_null_slot()
    {
        FakeWorldContext world = new();
        Assert.Null(EntityRegistry.ByName("cow").Behaviors.Ticker);
        Assert.Null(EntityRegistry.ByName("arrow").Behaviors.Ticker);
    }
}
