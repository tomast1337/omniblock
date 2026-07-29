using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers the light-dependent branches that were unreachable while <see cref="FakeWorldContext"/>
/// reported the world as pitch dark: a spider losing interest by day, darkness-only targeting
/// rejecting a lit player, and a monster refusing to spawn in the light.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityDaylightTests
{
    private static EntityCreature Spawn(FakeWorldContext world, string name, double x = 8.5, double z = 8.5)
    {
        EntityCreature mob = (EntityCreature)EntityRegistry.ByName(name).Create(world);
        mob.SetPositionAndAngles(x, 65.0, z, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(mob));
        return mob;
    }

    private static TestEntityPlayer Player(FakeWorldContext world)
    {
        TestEntityPlayer player = new(world) { Name = "tester" };
        player.SetPositionAndAngles(9.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));
        return player;
    }

    [Fact]
    public void Lighting_is_dark_by_default_and_bright_once_raised()
    {
        FakeWorldContext world = new();
        EntityCreature zombie = Spawn(world, "zombie");

        // Light level 0 maps to the dimension's ambient floor rather than to true black.
        Assert.True(zombie.GetBrightnessAtEyes(1.0F) < 0.5F);

        world.SetLightLevel(15);

        Assert.True(zombie.GetBrightnessAtEyes(1.0F) > 0.5F);
    }

    /// <summary>Chunks created after the level is set are lit too, not just the ones already made.</summary>
    [Fact]
    public void Chunks_created_after_lighting_is_set_are_lit_as_well()
    {
        FakeWorldContext world = new();
        world.SetLightLevel(15);

        EntityCreature distant = Spawn(world, "zombie", 520.5, 520.5);

        Assert.True(distant.GetBrightnessAtEyes(1.0F) > 0.5F);
    }

    /// <summary>
    ///     The branch <see cref="LoseTargetInDaylightBehavior"/> exists for. One-in-a-hundred per
    ///     call, so this drives it until it fires rather than depending on a seed.
    /// </summary>
    [Fact]
    public void A_spider_in_daylight_eventually_loses_interest()
    {
        FakeWorldContext world = new();
        world.SetLightLevel(15);

        EntityCreature spider = Spawn(world, "spider");
        TestEntityPlayer player = Player(world);
        LoseTargetInDaylightBehavior attack = (LoseTargetInDaylightBehavior)spider.Behaviors.Attack!;

        for (int attempt = 0; attempt < 5000; attempt++)
        {
            spider.Target = player;
            attack.AttackEntity(spider, player, 1.0F);

            if (spider.Target is null) return;
        }

        Assert.Fail("A spider in full daylight never lost its target in 5000 attacks.");
    }

    [Fact]
    public void A_spider_in_the_dark_never_loses_interest()
    {
        FakeWorldContext world = new();
        EntityCreature spider = Spawn(world, "spider");
        TestEntityPlayer player = Player(world);
        LoseTargetInDaylightBehavior attack = (LoseTargetInDaylightBehavior)spider.Behaviors.Attack!;

        for (int attempt = 0; attempt < 500; attempt++)
        {
            spider.Target = player;
            attack.AttackEntity(spider, player, 1.0F);
            Assert.Same(player, spider.Target);
        }
    }

    /// <summary>
    ///     The rejection branch that separates darkness-only targeting from always-hunt. Both find
    ///     the player in the dark; only this one gives up in the light.
    /// </summary>
    [Fact]
    public void Darkness_only_targeting_rejects_a_player_standing_in_light()
    {
        FakeWorldContext world = new();
        world.SetLightLevel(15);

        EntityCreature spider = Spawn(world, "spider");
        TestEntityPlayer player = Player(world);

        Assert.Null(spider.Targeting!.FindPlayerToAttack(spider));

        // Always-hunt ignores light entirely, which is what the two behaviors differ on.
        EntityCreature zombie = Spawn(world, "zombie", 10.5, 10.5);
        Assert.Same(player, zombie.Targeting!.FindPlayerToAttack(zombie));
    }

    /// <summary>
    ///     A monster refuses to spawn in sky light, but a zombie pigman ignores the rule entirely —
    ///     the Physics-slot replacement that could not be told apart from the default until now.
    /// </summary>
    [Fact]
    public void Light_blocks_a_monster_spawn_but_not_a_pig_zombie()
    {
        FakeWorldContext world = new();
        world.SetLightLevel(15);

        EntityCreature zombie = (EntityCreature)EntityRegistry.ByName("zombie").Create(world);
        zombie.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        EntityCreature pigZombie = (EntityCreature)EntityRegistry.ByName("pigzombie").Create(world);
        pigZombie.SetPositionAndAngles(40.5, 65.0, 40.5, 0f, 0f);

        Assert.False(zombie.CanSpawn());
        Assert.True(pigZombie.CanSpawn());
    }
}
