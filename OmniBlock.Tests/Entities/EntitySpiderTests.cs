using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;

namespace OmniBlock.Tests.Entities;

/// <summary>
///     Covers the spider, whose remaining overrides were split three ways: wall climbing and its ride
///     height into Physics, the skeleton jockey into Lifecycle, and daylight disinterest into a
///     decorator around its real attack.
/// </summary>
[Collection("EntityTests")]
public sealed class EntitySpiderTests
{
    private static EntityCreature Spider(FakeWorldContext world)
    {
        var spider = (EntityCreature)TestEntityCatalog.ByName("spider").Create(world);
        spider.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        return spider;
    }

    [Fact]
    public void A_spider_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();

        Assert.Equal(typeof(EntityCreature), Spider(world).GetType());
        Assert.NotNull(TestEntityCatalog.ByName("spider").Behaviors.Find<WallClimbBehavior>());
        Assert.NotNull(TestEntityCatalog.ByName("spider").Behaviors.Find<SpawnRiderBehavior>());
    }

    /// <summary>
    ///     Climbing replaces the ladder test rather than adding to it, so a spider against a wall is
    ///     climbing and one standing free is not — with no ladder involved either way.
    /// </summary>
    [Fact]
    public void A_spider_climbs_whatever_it_is_pressed_against()
    {
        FakeWorldContext world = new();
        var spider = Spider(world);
        var climb = spider.Behaviors.Find<WallClimbBehavior>()!;

        Assert.False(climb.IsClimbing(spider));

        // Walk it into a wall clear of its 1.4-wide box; the collision flag is what it reads.
        for (var y = 65; y < 68; y++)
        {
            world.ReaderWriter.SetBlock(11, y, 8, TestBlocks.Get("stone").Id, 0);
        }

        spider.Move(4.0D, 0.0D, 0.0D);

        Assert.True(climb.IsClimbing(spider));
    }

    [Fact]
    public void A_spider_makes_no_footstep_sounds_where_a_zombie_does()
    {
        Assert.False(TestEntityCatalog.ByName("spider").Definition!.MakesStepSounds);
        Assert.True(TestEntityCatalog.ByName("zombie").Definition!.MakesStepSounds);
    }

    [Fact]
    public void A_spider_carries_its_rider_lower_than_its_back()
    {
        Assert.Equal(-0.5D, TestEntityCatalog.ByName("spider").Definition!.PassengerRideOffset);
        Assert.Equal(0.0D, TestEntityCatalog.ByName("zombie").Definition!.PassengerRideOffset);
    }

    /// <summary>
    ///     The jockey is declared, so the pairing is data. One-in-a-hundred odds mean this only
    ///     asserts that the rider is reachable at all, not how often it appears.
    /// </summary>
    [Fact]
    public void A_spider_sometimes_spawns_a_skeleton_riding_it()
    {
        FakeWorldContext world = new();
        var jockey = TestEntityCatalog.ByName("spider").Behaviors.Find<SpawnRiderBehavior>()!;

        // Roll until the one-in-a-hundred fires rather than depending on a particular seed.
        for (var attempt = 0; attempt < 5000; attempt++)
        {
            var spider = Spider(world);
            jockey.OnPostSpawn(spider);

            if (spider.Passenger is null) continue;

            Assert.Equal("skeleton", TestEntityCatalog.GetId(spider.Passenger));
            return;
        }

        Assert.Fail("A skeleton jockey never spawned in 5000 attempts.");
    }

    [Fact]
    public void Daylight_disinterest_wraps_the_real_attack_rather_than_replacing_it()
    {
        var attack =
            Assert.IsType<LoseTargetInDaylightBehavior>(TestEntityCatalog.ByName("spider").Behaviors.Attack);

        Assert.IsType<JumpAttackBehavior>(attack.Inner);
    }

    /// <summary>
    ///     The fake world is unlit, so the daylight branch never fires and the wrapped attack always
    ///     runs — which is the behaviour that matters underground.
    /// </summary>
    [Fact]
    public void In_the_dark_a_spider_keeps_its_target_and_attacks()
    {
        FakeWorldContext world = new();
        var spider = Spider(world);
        Assert.True(world.Entities.SpawnEntity(spider));

        TestEntityPlayer player = new(world)
        {
            Name = "tester"
        };
        player.SetPositionAndAngles(9.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));

        spider.Target = player;
        var before = player.Health;

        var attack = (LoseTargetInDaylightBehavior)spider.Behaviors.Attack!;
        attack.AttackEntity(spider, player, 1.0F);

        Assert.Same(player, spider.Target);
        Assert.True(player.Health < before);
    }
}
