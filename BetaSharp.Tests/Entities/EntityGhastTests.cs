using System.Linq;
using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers the ghast, the first mob to lose both its class and its abstract base. Flight moved into
/// Physics, the AI it used to replace wholesale into a Ticker composite, and its rare spawn onto the
/// same spawn behavior the zombie pigman already used.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityGhastTests
{
    private static EntityLiving Ghast(FakeWorldContext world, double x = 8.5, double z = 8.5)
    {
        EntityLiving ghast = (EntityLiving)EntityRegistry.ByName("ghast").Create(world);
        ghast.SetPositionAndAngles(x, 65.0, z, 0f, 0f);
        return ghast;
    }

    private static TestEntityPlayer Player(FakeWorldContext world, double x, double z)
    {
        TestEntityPlayer player = new(world) { Name = "tester" };
        player.SetPositionAndAngles(x, 65.0, z, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));
        return player;
    }

    [Fact]
    public void A_ghast_has_no_class_and_no_flying_base()
    {
        FakeWorldContext world = new();

        Assert.Equal(typeof(EntityLiving), Ghast(world).GetType());
        Assert.IsType<CompositeBehavior>(EntityRegistry.ByName("ghast").Behaviors.Physics);
        Assert.IsType<CompositeBehavior>(EntityRegistry.ByName("ghast").Behaviors.Ticker);
    }

    /// <summary>
    ///     A composite is not a wall: the capabilities it holds are still reachable by asking for
    ///     them, which is how the renderer finds the charge counter it draws from.
    /// </summary>
    [Fact]
    public void Behaviors_nested_in_a_composite_are_still_found_by_capability()
    {
        EntityBehaviorSet behaviors = EntityRegistry.ByName("ghast").Behaviors;

        Assert.NotNull(behaviors.Find<FlyingMovementBehavior>());
        Assert.NotNull(behaviors.Find<FlyingWanderBehavior>());
        Assert.NotNull(behaviors.Find<FireballAttackBehavior>());
        Assert.NotNull(behaviors.Find<SpawnIgnoringLightBehavior>());
        Assert.Null(behaviors.Find<WallClimbBehavior>());
    }

    /// <summary>
    ///     Flight is the absence of gravity, so the vertical speed is only damped and never has the
    ///     0.08 fall step taken out of it.
    /// </summary>
    [Fact]
    public void A_ghast_is_damped_rather_than_pulled_down()
    {
        FakeWorldContext world = new();
        EntityLiving ghast = Ghast(world);
        ghast.VelocityY = 1.0D;

        Assert.True(ghast.Behaviors.Physics!.Travel(ghast, 0.0F, 0.0F));
        Assert.Equal(0.91D, ghast.VelocityY, 3);
    }

    [Fact]
    public void A_ghast_never_takes_a_landing_and_never_climbs()
    {
        FakeWorldContext world = new();
        EntityLiving ghast = Ghast(world);

        Assert.True(ghast.Behaviors.Physics!.OnLanding(ghast, 100.0F));
        Assert.False(ghast.Behaviors.Physics.IsClimbing(ghast));
    }

    /// <summary>
    ///     The wander behavior answers that it is the mob's AI, which is what stops the default idle
    ///     logic from running, and it steers towards its waypoint on the first tick.
    /// </summary>
    [Fact]
    public void Wandering_replaces_the_default_ai_and_steers()
    {
        FakeWorldContext world = new();
        EntityLiving ghast = Ghast(world);
        Assert.True(world.Entities.SpawnEntity(ghast));

        Assert.True(ghast.Behaviors.Ticker!.OnTickLiving(ghast));

        double speed = Math.Abs(ghast.VelocityX) + Math.Abs(ghast.VelocityY) + Math.Abs(ghast.VelocityZ);
        Assert.True(speed > 0.0D, "A ghast that has just picked a waypoint should be moving towards it.");
    }

    [Fact]
    public void A_ghast_charges_up_and_fires_at_a_player_it_can_see()
    {
        FakeWorldContext world = new();
        EntityLiving ghast = Ghast(world);
        Assert.True(world.Entities.SpawnEntity(ghast));
        Player(world, 20.5, 8.5);

        FireballAttackBehavior attack = ghast.Behaviors.Find<FireballAttackBehavior>()!;

        // Halfway through the wind-up the mob is visibly charging, and swaps to the lit texture.
        for (int tick = 0; tick < 11; tick++) attack.OnTickLiving(ghast);
        attack.OnTickEnd(ghast);

        Assert.True(ghast.Synced<bool>("charging")!.Value);
        Assert.Equal("/mob/ghast_fire.png", ghast.GetTexture());
        Assert.DoesNotContain(world.Entities.Entities, e => e.Behaviors.Find<FireballBehavior>() is not null);

        for (int tick = 11; tick < 20; tick++) attack.OnTickLiving(ghast);

        Assert.Single(world.Entities.Entities, e => e.Behaviors.Find<FireballBehavior>() is not null);

        // Firing drops the mob into its reload, so it reads as not charging again.
        attack.OnTickEnd(ghast);
        Assert.False(ghast.Synced<bool>("charging")!.Value);
        Assert.Equal("/mob/ghast.png", ghast.GetTexture());
    }

    /// <summary>A wall is enough: the wind-up needs an unbroken line, not just a nearby player.</summary>
    [Fact]
    public void A_ghast_hidden_behind_a_wall_never_charges()
    {
        FakeWorldContext world = new();
        EntityLiving ghast = Ghast(world);
        Assert.True(world.Entities.SpawnEntity(ghast));
        Player(world, 20.5, 8.5);

        int stone = BlockRegistry.Get("stone").id;
        for (int y = 60; y < 72; y++) world.ReaderWriter.SetBlock(14, y, 8, stone, 0);

        FireballAttackBehavior attack = ghast.Behaviors.Find<FireballAttackBehavior>()!;
        for (int tick = 0; tick < 40; tick++) attack.OnTickLiving(ghast);

        Assert.DoesNotContain(world.Entities.Entities, e => e.Behaviors.Find<FireballBehavior>() is not null);
        Assert.False(ghast.Synced<bool>("charging")!.Value);
    }

    [Fact]
    public void A_ghast_is_removed_when_the_world_turns_peaceful()
    {
        FakeWorldContext world = new();
        EntityLiving ghast = Ghast(world);
        Assert.True(world.Entities.SpawnEntity(ghast));

        ghast.Behaviors.Ticker!.OnTickLiving(ghast);
        Assert.False(ghast.Dead);

        world.Difficulty = 0;
        ghast.Behaviors.Ticker.OnTickLiving(ghast);

        Assert.True(ghast.Dead);
    }

    /// <summary>
    ///     Rare, and never on peaceful. Built without being added to the world so the mob does not
    ///     collide with itself while asking whether it fits.
    /// </summary>
    [Fact]
    public void A_ghast_spawns_rarely_and_not_on_peaceful()
    {
        FakeWorldContext world = new() { Difficulty = 0 };
        EntityLiving peaceful = Ghast(world, 40.5, 40.5);

        for (int attempt = 0; attempt < 200; attempt++) Assert.False(peaceful.CanSpawn());

        FakeWorldContext dangerous = new();
        EntityLiving ghast = Ghast(dangerous, 40.5, 40.5);
        int allowed = 0;
        for (int attempt = 0; attempt < 2000; attempt++)
        {
            if (ghast.CanSpawn()) allowed++;
        }

        // One in twenty, so a couple of hundred out of two thousand — loose bounds, but far from
        // both "always" and "never".
        Assert.InRange(allowed, 20, 400);
    }
}
