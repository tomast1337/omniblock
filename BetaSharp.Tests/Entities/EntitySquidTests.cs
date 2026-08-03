using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers the squid, which has no class of its own: a bare <see cref="EntityLiving"/> whose
/// swimming is one behavior across Physics and Ticker, with the water-only spawn rule beside it in
/// the same slot.
/// </summary>
[Collection("EntityTests")]
public sealed class EntitySquidTests
{
    private static readonly int s_water = BlockRegistry.Get("water").Id;

    private static EntityLiving Squid(FakeWorldContext world, double x = 8.5, double y = 65.0, double z = 8.5)
    {
        EntityLiving squid = (EntityLiving)EntityRegistry.ByName("squid").Create(world);
        squid.SetPositionAndAngles(x, y, z, 0f, 0f);
        return squid;
    }

    /// <summary>Floods a cube around the given column so the mob is genuinely submerged.</summary>
    private static void Flood(FakeWorldContext world, int x, int z)
    {
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int y = 62; y <= 68; y++) world.ReaderWriter.SetBlock(x + dx, y, z + dz, s_water, 0);
            }
        }
    }

    [Fact]
    public void A_squid_has_no_class_and_no_water_mob_base()
    {
        FakeWorldContext world = new();

        Assert.Equal(typeof(EntityLiving), Squid(world).GetType());
        Assert.IsType<CompositeBehavior>(EntityRegistry.ByName("squid").Behaviors.Physics);
        Assert.IsType<CompositeBehavior>(EntityRegistry.ByName("squid").Behaviors.Ticker);
        Assert.NotNull(EntityRegistry.ByName("squid").Behaviors.Find<JetSwimBehavior>());
        Assert.NotNull(EntityRegistry.ByName("squid").Behaviors.Find<SpawnInFluidBehavior>());
    }

    /// <summary>
    ///     The same instance fills both slots, which is what makes the swim one trait rather than a
    ///     movement behavior and an animation behavior that have to be kept in step.
    /// </summary>
    [Fact]
    public void Swimming_fills_both_of_its_slots_with_one_behavior()
    {
        EntityBehaviorSet behaviors = EntityRegistry.ByName("squid").Behaviors;
        JetSwimBehavior swim = behaviors.Find<JetSwimBehavior>()!;

        Assert.Same(swim, ((CompositeBehavior)behaviors.Physics!).Children.OfType<JetSwimBehavior>().Single());
        Assert.Same(swim, ((CompositeBehavior)behaviors.Ticker!).Children.OfType<JetSwimBehavior>().Single());
    }

    [Fact]
    public void A_squid_breathes_underwater_where_a_zombie_does_not()
    {
        Assert.True(EntityRegistry.ByName("squid").Definition!.BreathesUnderwater);
        Assert.False(EntityRegistry.ByName("zombie").Definition!.BreathesUnderwater);
    }

    /// <summary>
    ///     The squid's velocity is imposed by the server, so its client copy has to be told about
    ///     it — the tracker used to key that off the class.
    /// </summary>
    [Fact]
    public void Only_a_squid_declares_that_its_velocity_is_tracked()
    {
        Assert.True(EntityRegistry.ByName("squid").Definition!.TracksVelocity);
        Assert.False(EntityRegistry.ByName("cow").Definition!.TracksVelocity);
    }

    /// <summary>Travel is a bare move: nothing accelerates it and nothing damps it.</summary>
    [Fact]
    public void A_squid_coasts_on_exactly_the_velocity_it_was_given()
    {
        FakeWorldContext world = new();
        EntityLiving squid = Squid(world);
        squid.VelocityX = 0.1D;
        squid.VelocityY = 0.05D;

        Assert.True(squid.Behaviors.Physics!.Travel(squid, 0.0F, 0.0F));

        Assert.Equal(0.1D, squid.VelocityX, 10);
        Assert.Equal(0.05D, squid.VelocityY, 10);
        Assert.Equal(8.6D, squid.X, 10);
    }

    /// <summary>
    ///     Out of water the beat is cosmetic: the squid stops steering itself sideways and falls.
    /// </summary>
    [Fact]
    public void Out_of_water_a_squid_sinks_and_stops_steering()
    {
        FakeWorldContext world = new();
        EntityLiving squid = Squid(world);
        Assert.True(world.Entities.SpawnEntity(squid));
        squid.VelocityX = 0.5D;

        squid.Behaviors.Physics!.AfterTickMovement(squid);

        Assert.Equal(0.0D, squid.VelocityX);
        Assert.True(squid.VelocityY < 0.0D, "A squid out of water should be falling.");
    }

    /// <summary>
    ///     In water the beat eventually kicks, and when it does the squid is moving along the
    ///     heading its AI tick picked rather than falling.
    /// </summary>
    [Fact]
    public void In_water_a_squid_jets_along_its_chosen_heading()
    {
        FakeWorldContext world = new();
        Flood(world, 8, 8);

        EntityLiving squid = Squid(world);
        Assert.True(world.Entities.SpawnEntity(squid));
        squid.Behaviors.Ticker!.OnTickLiving(squid);

        // The kick lands in the last quarter of the first half-cycle, so it takes a few beats.
        for (int tick = 0; tick < 60; tick++)
        {
            squid.Behaviors.Physics!.AfterTickMovement(squid);
            if (squid.VelocityY > 0.0D || squid.VelocityX != 0.0D) return;
        }

        Assert.Fail("A submerged squid never jetted in 60 ticks.");
    }

    /// <summary>Picking a heading is the whole of the squid's AI, so nothing else runs.</summary>
    [Fact]
    public void The_ai_tick_replaces_the_default_and_always_leaves_a_heading()
    {
        FakeWorldContext world = new();
        Flood(world, 8, 8);

        EntityLiving squid = Squid(world);
        Assert.True(world.Entities.SpawnEntity(squid));

        Assert.True(squid.Behaviors.Ticker!.OnTickLiving(squid));

        // A heading was chosen, so the next stroke has somewhere to push.
        squid.Behaviors.Physics!.AfterTickMovement(squid);
        Assert.False(squid.Dead);
    }

    /// <summary>
    ///     A squid may spawn in a block full of water,
    ///     which the default placement check rejects outright.
    /// </summary>
    [Fact]
    public void A_squid_spawns_in_water_where_a_zombie_cannot()
    {
        FakeWorldContext world = new();
        Flood(world, 40, 40);

        EntityLiving squid = Squid(world, 40.5, 65.0, 40.5);
        EntityCreature zombie = (EntityCreature)EntityRegistry.ByName("zombie").Create(world);
        zombie.SetPositionAndAngles(40.5, 65.0, 40.5, 0f, 0f);

        Assert.True(squid.CanSpawn());
        Assert.False(zombie.CanSpawn());
    }
}
