using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Tests.TestSupport;
using BetaSharp.Util.Maths;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers the lightning bolt, the third non-living entity to lose its class — and with it the
/// EntityWeatherEffect base, which held nothing. The flash countdown, the fire it strikes, and who
/// it electrocutes are one behavior across three slots; visibility comes through the new
/// Physics.ShouldRender hook.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityLightningTests
{
    private static LightningStrikeBehavior Strike => EntityRegistry.ByName("lightningbolt").Behaviors.Find<LightningStrikeBehavior>()!;

    private static Entity Bolt(FakeWorldContext world, double x = 8.5, double y = 64.0, double z = 8.5)
    {
        Entity bolt = EntityRegistry.ByName("lightningbolt").Create(world);
        bolt.SetPositionAndAnglesKeepPrevAngles(x, y, z, 0.0F, 0.0F);
        Assert.True(world.Entities.SpawnEntity(bolt));
        return bolt;
    }

    [Fact]
    public void Lightning_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();
        Entity bolt = Bolt(world);

        Assert.Equal(typeof(EntityObject), bolt.GetType());
        Assert.Same(EntityRegistry.ByName("lightningbolt").Behaviors.Ticker, EntityRegistry.ByName("lightningbolt").Behaviors.Physics);
    }

    /// <summary>Each bolt draws its own jagged path: the seed rolls at creation and again per flash.</summary>
    [Fact]
    public void Every_bolt_carries_its_own_render_seed()
    {
        FakeWorldContext world = new();
        Entity first = Bolt(world);
        Entity second = Bolt(world, x: 12.5);

        Assert.NotEqual(0L, Strike.RenderSeed(first));
        Assert.NotEqual(Strike.RenderSeed(first), Strike.RenderSeed(second));
    }

    /// <summary>
    /// The strike happens on the first tick, once the bolt has a position: fire at the impact
    /// point on hard-enough difficulty.
    /// </summary>
    [Fact]
    public void A_strike_sets_the_ground_on_fire()
    {
        FakeWorldContext world = new() { Difficulty = 2 };
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);
        Entity bolt = Bolt(world, y: 64.0);

        bolt.Tick();

        Assert.Equal(BlockRegistry.Get("fire").id, world.Reader.GetBlockId(8, 64, 8));
    }

    [Fact]
    public void On_peaceful_the_strike_starts_no_fire()
    {
        FakeWorldContext world = new() { Difficulty = 0 };
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);
        Entity bolt = Bolt(world, y: 64.0);

        bolt.Tick();

        Assert.Equal(0, world.Reader.GetBlockId(8, 64, 8));
    }

    /// <summary>Anything close enough is struck — which is what powers a creeper and converts a pig.</summary>
    [Fact]
    public void Nearby_entities_are_electrocuted()
    {
        FakeWorldContext world = new();
        EntityMonster creeper = (EntityMonster)EntityRegistry.ByName("creeper").Create(world);
        creeper.SetPositionAndAngles(9.5, 64.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(creeper));
        Entity bolt = Bolt(world, y: 64.0);

        bolt.Tick();

        Assert.True(creeper.Synced<bool>("powered")!.Value);
    }

    /// <summary>The bolt flashes, lingers a random moment, and removes itself — no cleanup needed.</summary>
    [Fact]
    public void A_bolt_burns_itself_out()
    {
        FakeWorldContext world = new();
        Entity bolt = Bolt(world);

        for (int tick = 0; tick < 60 && !bolt.Dead; tick++) bolt.Tick();

        Assert.True(bolt.Dead);
    }

    /// <summary>Visibility follows the flash, not the camera distance.</summary>
    [Fact]
    public void It_renders_only_while_flashing()
    {
        FakeWorldContext world = new();
        Entity bolt = Bolt(world);

        Assert.True(bolt.ShouldRender(new Vec3D(1000.0, 1000.0, 1000.0)));

        for (int tick = 0; tick < 60 && !bolt.Dead; tick++) bolt.Tick();

        Assert.False(bolt.ShouldRender(new Vec3D(8.5, 64.0, 8.5)));
    }

    [Fact]
    public void The_global_spawn_id_is_pinned()
    {
        EntityType type = EntityRegistry.ByName("lightningbolt");

        Assert.Equal(65, type.RequireDefinition().ProtocolId);
        Assert.Equal(1, type.RequireDefinition().GlobalSpawnId);
        Assert.Same(type, EntityRegistry.ByGlobalSpawnId(1));
        Assert.Null(EntityRegistry.ByGlobalSpawnId(2));
    }
}
