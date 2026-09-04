using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.NBT;

namespace OmniBlock.Tests.Entities;

/// <summary>
///     Covers the last two monsters to lose their classes: the giant, whose size is a declared scale
///     factor, and the zombie pigman, whose whole anger machine is one behavior across four slots.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityAngerTests
{
    private static EntityCreature Spawn(FakeWorldContext world, string name, double x = 8.5, double z = 8.5)
    {
        var mob = (EntityCreature)TestEntityCatalog.ByName(name).Create(world);
        mob.SetPositionAndAngles(x, 65.0, z, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(mob));
        return mob;
    }

    private static (AngerBehavior Anger, EntityCreature Mob) PigZombie(FakeWorldContext world, double x = 8.5, double z = 8.5)
    {
        var mob = Spawn(world, "pigzombie", x, z);
        return (mob.Behaviors.Find<AngerBehavior>()!, mob);
    }

    /// <summary>
    ///     The giant's box is 3.6000001 by 10.799999 — float artifacts of multiplying by six. A
    ///     declared scale has to reproduce them exactly, not approximate them.
    /// </summary>
    [Fact]
    public void A_giant_keeps_the_exact_float_box_its_constructor_produced()
    {
        FakeWorldContext world = new();
        var giant = Spawn(world, "giant");

        Assert.Equal(typeof(EntityCreature), giant.GetType());
        Assert.Equal(0.6F * 6.0F, giant.Width);
        Assert.Equal(1.8F * 6.0F, giant.Height);
    }

    [Fact]
    public void A_giant_seeks_light_where_other_monsters_avoid_it()
    {
        // The giant declares its own path preference beside the shared monster rule, and being
        // first in the composite is what makes it win.
        Assert.NotNull(TestEntityCatalog.ByName("giant").Behaviors.Find<LightSeekingPathBehavior>());
        Assert.Null(TestEntityCatalog.ByName("zombie").Behaviors.Find<LightSeekingPathBehavior>());

        FakeWorldContext lit = new();
        var giant = (EntityCreature)TestEntityCatalog.ByName("giant").Create(lit);
        var zombie = (EntityCreature)TestEntityCatalog.ByName("zombie").Create(lit);

        // Exact opposites: the giant reads luminance - 0.5 where every other monster reads
        // 0.5 - luminance.
        Assert.Equal(
            -zombie.Behaviors.Physics!.GetBlockPathWeight(zombie, 8, 65, 8),
            giant.Behaviors.Physics!.GetBlockPathWeight(giant, 8, 65, 8));
    }

    [Fact]
    public void A_pig_zombie_fills_four_slots_from_one_entry()
    {
        FakeWorldContext world = new();
        var (anger, mob) = PigZombie(world);

        Assert.Same(anger, mob.Behaviors.Find<AngerBehavior>());
        Assert.NotNull(mob.Behaviors.Targeting);
        Assert.Same(anger, mob.Behaviors.Find<AngerBehavior>());
        Assert.NotNull(mob.Behaviors.Lifecycle);
        Assert.NotNull(mob.Behaviors.Persistence);
    }

    [Fact]
    public void An_unprovoked_pig_zombie_hunts_nobody()
    {
        FakeWorldContext world = new();
        var (anger, mob) = PigZombie(world);
        TestEntityPlayer player = new(world)
        {
            Name = "tester"
        };
        player.SetPositionAndAngles(9.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));

        Assert.False(anger.IsAngry(mob));
        Assert.Null(anger.FindPlayerToAttack(mob));
    }

    [Fact]
    public void Hitting_one_pig_zombie_angers_the_whole_neighbourhood()
    {
        FakeWorldContext world = new();
        var (anger, struck) = PigZombie(world);
        var (_, bystander) = PigZombie(world, 12.5, 12.5);
        var zombie = Spawn(world, "zombie", 9.0, 9.0);

        TestEntityPlayer player = new(world)
        {
            Name = "tester"
        };
        player.SetPositionAndAngles(9.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));

        struck.Damage(player, 1);

        Assert.True(anger.IsAngry(struck));
        Assert.True(anger.IsAngry(bystander));
        Assert.Same(player, struck.Target);
        Assert.Same(player, bystander.Target);

        // Only its own kind joins in — a plain zombie nearby is unaffected.
        Assert.Null(zombie.Target);
    }

    [Fact]
    public void An_angered_pig_zombie_hunts_and_speeds_up()
    {
        FakeWorldContext world = new();
        var (anger, mob) = PigZombie(world);
        TestEntityPlayer player = new(world)
        {
            Name = "tester"
        };
        player.SetPositionAndAngles(9.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));

        mob.Damage(player, 1);

        Assert.Same(player, anger.FindPlayerToAttack(mob));

        anger.OnTick(mob);
        Assert.Equal(0.95F, mob.MovementSpeed);
    }

    [Fact]
    public void Anger_survives_a_save_load_round_trip()
    {
        FakeWorldContext world = new();
        var (anger, mob) = PigZombie(world);
        TestEntityPlayer player = new(world)
        {
            Name = "tester"
        };
        player.SetPositionAndAngles(9.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));

        mob.Damage(player, 1);

        NBTTagCompound nbt = new();
        Assert.True(mob.SaveSelfNbt(nbt));
        Assert.True(nbt.GetShort("Anger") > 0);

        Entity loaded = Assert.IsType<EntityCreature>(TestEntityCatalog.GetEntityFromNbt(nbt, new FakeWorldContext()));
        Assert.True(anger.IsAngry(loaded));
    }

    /// <summary>Spawn rules ignore the darkness check monsters normally obey, but not peaceful mode.</summary>
    [Fact]
    public void A_pig_zombie_spawns_regardless_of_light()
    {
        FakeWorldContext world = new();

        // Not added to the world: a mob already occupying its own box blocks its own spawn check.
        var mob = (EntityCreature)TestEntityCatalog.ByName("pigzombie").Create(world);
        mob.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        var spawn = mob.Behaviors.Find<SpawnIgnoringLightBehavior>()!;

        world.Difficulty = 0;
        Assert.False(spawn.CanSpawn(mob));

        world.Difficulty = 2;
        Assert.True(spawn.CanSpawn(mob));
    }
}
