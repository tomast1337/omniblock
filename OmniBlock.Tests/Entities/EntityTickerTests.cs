using System.Linq;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;

namespace OmniBlock.Tests.Entities;

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
        EntityCreature first = (EntityCreature)TestEntityCatalog.ByName("zombie").Create(world);
        EntityCreature second = (EntityCreature)TestEntityCatalog.ByName("zombie").Create(world);

        Assert.NotNull(first.Attack);
        Assert.Same(first.Attack, second.Attack);
        Assert.Same(first.Loot, second.Loot);
        Assert.Same(TestEntityCatalog.ByName("zombie").Behaviors.Attack, first.Attack);
    }

    [Fact]
    public void Shared_tickers_keep_per_entity_state_separate()
    {
        FakeWorldContext world = new();
        EntityCreature first = (EntityCreature)TestEntityCatalog.ByName("chicken").Create(world);
        EntityCreature second = (EntityCreature)TestEntityCatalog.ByName("chicken").Create(world);

        LayEggsBehavior ticker = Assert.IsType<LayEggsBehavior>(TestEntityCatalog.ByName("chicken").Behaviors.Ticker);

        ticker.Reset(first);
        ticker.Reset(second);

        // Same behavior object, independent countdowns — the property that makes sharing safe.
        Assert.Same(ticker, TestEntityCatalog.ByName("chicken").Behaviors.Ticker);
        Assert.True(first.State != second.State);
    }

    [Fact]
    public void Zombie_and_skeleton_share_one_daylight_burn_behavior_type()
    {
        Assert.NotNull(TestEntityCatalog.ByName("zombie").Behaviors.Find<BurnInDaylightBehavior>());
        Assert.NotNull(TestEntityCatalog.ByName("skeleton").Behaviors.Find<BurnInDaylightBehavior>());
    }

    [Fact]
    public void Daylight_burn_sets_fire_only_under_open_sky()
    {
        FakeWorldContext world = new();
        BurnInDaylightBehavior ticker = new();
        EntityCreature zombie = (EntityCreature)TestEntityCatalog.ByName("zombie").Create(world);
        zombie.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        // FakeWorldContext reports darkness, so the brightness gate keeps the mob unlit.
        for (int i = 0; i < 50; i++) ticker.OnTickMovement(zombie);
        Assert.False(zombie.IsOnFire);
    }

    [Fact]
    public void Chicken_lays_an_egg_when_its_countdown_expires()
    {
        FakeWorldContext world = new();
        EntityCreature chicken = (EntityCreature)TestEntityCatalog.ByName("chicken").Create(world);
        chicken.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(chicken));

        LayEggsBehavior ticker = Assert.IsType<LayEggsBehavior>(TestEntityCatalog.ByName("chicken").Behaviors.Ticker);
        int eggId = ContentRuntime.Current.Items.Get("omniblock:egg").Id;

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
        // Every non-living entity ticks through a behavior, and every monster ticks through the
        // shared hostile rules, so the examples left are farm animals.
        Assert.Null(TestEntityCatalog.ByName("cow").Behaviors.Ticker);
        Assert.Null(TestEntityCatalog.ByName("pig").Behaviors.Ticker);
    }
}
