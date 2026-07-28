using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers the creeper's fuse, now a single behavior across the Attack, Ticker and Lifecycle slots
/// rather than a class. The countdown is the risky part, so it is driven directly here.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityFuseTests
{
    private static Entity Bolt(FakeWorldContext world, double x, double y, double z)
    {
        Entity bolt = EntityRegistry.ByName("lightningbolt").Create(world);
        bolt.SetPositionAndAnglesKeepPrevAngles(x, y, z, 0.0F, 0.0F);
        return bolt;
    }

    private static (EntityMonster Creeper, FuseBehavior Fuse) Creeper(FakeWorldContext world)
    {
        EntityMonster creeper = (EntityMonster)EntityRegistry.ByName("creeper").Create(world);
        creeper.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(creeper));
        return (creeper, Assert.IsType<FuseBehavior>(creeper.Behaviors.Attack));
    }

    private static TestEntityPlayer Player(FakeWorldContext world)
    {
        TestEntityPlayer player = new(world) { Name = "tester" };
        player.SetPositionAndAngles(9.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));
        return player;
    }

    [Fact]
    public void A_creeper_has_no_class_and_fills_three_slots_from_one_entry()
    {
        FakeWorldContext world = new();
        (EntityMonster creeper, FuseBehavior fuse) = Creeper(world);

        Assert.Equal(typeof(EntityMonster), creeper.GetType());

        // One instance, three slots — the same object, not three copies.
        Assert.Same(fuse, creeper.Behaviors.Ticker);
        Assert.Same(fuse, creeper.Behaviors.Lifecycle);
    }

    [Fact]
    public void A_creeper_in_range_winds_its_fuse_up_and_detonates()
    {
        FakeWorldContext world = new();
        (EntityMonster creeper, FuseBehavior fuse) = Creeper(world);
        TestEntityPlayer player = Player(world);

        Assert.Equal(0.0F, fuse.FlashTime(creeper, 1.0F));

        // 30 ticks in range is a full fuse; the creeper dies to its own explosion.
        for (int i = 0; i < 30; i++)
        {
            fuse.AttackEntity(creeper, player, 2.0F);
        }

        Assert.True(creeper.Dead);
    }

    /// <summary>
    ///     Winding up is the only thing that arms the wider range: an unlit creeper has to be closed
    ///     with to 3 blocks, but once lit it stays lit out to 7.
    /// </summary>
    [Fact]
    public void An_unlit_creeper_ignores_a_target_that_is_merely_close()
    {
        FakeWorldContext world = new();
        (EntityMonster creeper, FuseBehavior fuse) = Creeper(world);
        TestEntityPlayer player = Player(world);

        fuse.AttackEntity(creeper, player, 5.0F);
        Assert.Equal(0.0F, fuse.FlashTime(creeper, 1.0F));

        fuse.AttackEntity(creeper, player, 2.0F);
        Assert.True(fuse.FlashTime(creeper, 1.0F) > 0.0F);

        // Now lit, five blocks keeps it burning rather than putting it out.
        fuse.AttackEntity(creeper, player, 5.0F);
        Assert.True(fuse.FlashTime(creeper, 1.0F) > 0.0F);
    }

    [Fact]
    public void A_target_behind_cover_lets_the_fuse_burn_down()
    {
        FakeWorldContext world = new();
        (EntityMonster creeper, FuseBehavior fuse) = Creeper(world);
        TestEntityPlayer player = Player(world);

        fuse.AttackEntity(creeper, player, 2.0F);
        fuse.AttackEntity(creeper, player, 2.0F);
        float lit = fuse.FlashTime(creeper, 1.0F);

        fuse.AttackBlockedEntity(creeper, player, 2.0F);

        Assert.True(fuse.FlashTime(creeper, 1.0F) < lit);
    }

    [Fact]
    public void A_creeper_that_loses_its_target_winds_back_down()
    {
        FakeWorldContext world = new();
        (EntityMonster creeper, FuseBehavior fuse) = Creeper(world);
        TestEntityPlayer player = Player(world);

        fuse.AttackEntity(creeper, player, 2.0F);
        fuse.AttackEntity(creeper, player, 2.0F);
        float lit = fuse.FlashTime(creeper, 1.0F);

        creeper.Target = null;
        fuse.OnTickEnd(creeper);

        Assert.True(fuse.FlashTime(creeper, 1.0F) < lit);
    }

    /// <summary>Lightning doubles the blast, and the flag persists as a declared synced property.</summary>
    [Fact]
    public void Lightning_supercharges_a_creeper_without_consuming_the_strike()
    {
        FakeWorldContext world = new();
        (EntityMonster creeper, FuseBehavior fuse) = Creeper(world);

        // False, so the default strike response (fire, damage) still runs afterwards.
        Assert.False(fuse.OnStruckByLightning(creeper, Bolt(world, creeper.X, creeper.Y, creeper.Z)));
        Assert.True(creeper.Synced<bool>("powered")!.Value);
    }

    /// <summary>
    ///     Each creeper carries its own countdown, since the behavior is shared by every creeper of
    ///     the type. A second creeper must not inherit the first one's charge.
    /// </summary>
    [Fact]
    public void Two_creepers_burn_independently()
    {
        FakeWorldContext world = new();
        (EntityMonster first, FuseBehavior fuse) = Creeper(world);
        (EntityMonster second, FuseBehavior secondFuse) = Creeper(world);
        TestEntityPlayer player = Player(world);

        Assert.Same(fuse, secondFuse);

        fuse.AttackEntity(first, player, 2.0F);
        fuse.AttackEntity(first, player, 2.0F);

        Assert.True(fuse.FlashTime(first, 1.0F) > 0.0F);
        Assert.Equal(0.0F, fuse.FlashTime(second, 1.0F));
    }
}
