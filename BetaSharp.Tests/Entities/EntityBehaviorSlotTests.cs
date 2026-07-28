using System.Collections.Generic;
using System.Linq;
using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.Worlds.Core.Systems;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers the four composable capability slots introduced in Phase 1 of the mob data-driven
/// migration (see docs/mob-data-driven-migration.md): Attack, Targeting, Loot and Lifecycle.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityBehaviorSlotTests
{
    [Fact]
    public void Melee_attack_damages_target_within_range()
    {
        FakeWorldContext world = new();
        TestZombie zombie = Spawn(world, new TestZombie(world), 8.5, 65.0, 8.5);
        EntityAnimal pig = Spawn(world, (EntityAnimal)EntityRegistry.ByName("pig").Create(world), 9.5, 65.0, 8.5);
        int healthBefore = pig.Health;

        zombie.ForceAttack(pig, 1.0f);

        Assert.True(pig.Health < healthBefore);
        Assert.Equal(20, zombie.ExposedAttackTime);
    }

    [Fact]
    public void Melee_attack_ignores_target_out_of_range()
    {
        FakeWorldContext world = new();
        TestZombie zombie = Spawn(world, new TestZombie(world), 8.5, 65.0, 8.5);
        EntityAnimal pig = Spawn(world, (EntityAnimal)EntityRegistry.ByName("pig").Create(world), 20.0, 65.0, 8.5);
        int healthBefore = pig.Health;

        zombie.ForceAttack(pig, 11.0f);

        Assert.Equal(healthBefore, pig.Health);
        Assert.Equal(0, zombie.ExposedAttackTime);
    }

    [Fact]
    public void Monster_composes_melee_and_always_hunt_slots()
    {
        FakeWorldContext world = new();
        EntityZombie zombie = new(world);

        Assert.IsType<MeleeAttackBehavior>(zombie.Attack);
        Assert.IsType<AlwaysHuntTargetBehavior>(zombie.Targeting);
    }

    [Fact]
    public void Spider_composes_jump_attack_and_darkness_only_targeting()
    {
        FakeWorldContext world = new();
        EntitySpider spider = new(world);

        Assert.IsType<JumpAttackBehavior>(spider.Attack);
        Assert.IsType<DarknessOnlyTargetBehavior>(spider.Targeting);
    }

    [Fact]
    public void Animals_never_target_and_never_attack()
    {
        FakeWorldContext world = new();

        foreach (EntityCreature animal in new EntityCreature[] { (EntityAnimal)EntityRegistry.ByName("pig").Create(world), (EntityAnimal)EntityRegistry.ByName("cow").Create(world), (EntityAnimal)EntityRegistry.ByName("sheep").Create(world), (EntityAnimal)EntityRegistry.ByName("chicken").Create(world) })
        {
            Assert.Null(animal.Attack);
            Assert.Null(animal.Targeting);
        }
    }

    [Fact]
    public void Darkness_only_targeting_acquires_player_while_unlit()
    {
        FakeWorldContext world = new();
        EntitySpider spider = Spawn(world, new EntitySpider(world), 8.5, 65.0, 8.5);
        TestEntityPlayer player = Spawn(world, new TestEntityPlayer(world) { Name = "tester" }, 9.5, 65.0, 8.5);

        // The fake world is unlit, so the brightness gate opens. Its daylight rejection branch needs
        // a lit world and is not reachable through FakeWorldContext's lighting.
        Assert.Same(player, spider.Targeting!.FindPlayerToAttack(spider));
    }

    [Fact]
    public void Always_hunt_targeting_acquires_a_player_in_range()
    {
        FakeWorldContext world = new();
        EntityZombie zombie = Spawn(world, new EntityZombie(world), 8.5, 65.0, 8.5);
        TestEntityPlayer player = Spawn(world, new TestEntityPlayer(world) { Name = "tester" }, 11.5, 65.0, 8.5);

        // FakeWorldContext's raycast always reports MISS, so the line-of-sight rejection branch that
        // separates this behavior from DarknessOnlyTargetBehavior is not reachable here.
        Assert.Same(player, zombie.Targeting!.FindPlayerToAttack(zombie));
    }

    [Fact]
    public void Simple_loot_drops_only_its_own_item()
    {
        FakeWorldContext world = new();
        EntityAnimal cow = Spawn(world, (EntityAnimal)EntityRegistry.ByName("cow").Create(world), 8.5, 65.0, 8.5);

        List<int> dropped = CollectDrops(world, cow, killer: null, rolls: 100);

        Assert.NotEmpty(dropped);
        Assert.All(dropped, id => Assert.Equal(Item.ByName("leather").Id, id));
    }

    [Fact]
    public void Skeleton_loot_drops_both_arrows_and_bones()
    {
        FakeWorldContext world = new();
        EntitySkeleton skeleton = Spawn(world, new EntitySkeleton(world), 8.5, 65.0, 8.5);

        List<int> dropped = CollectDrops(world, skeleton, killer: null, rolls: 100);

        Assert.Contains(Item.ByName("arrow").Id, dropped);
        Assert.Contains(Item.ByName("bone").Id, dropped);
        Assert.All(dropped, id => Assert.True(id == Item.ByName("arrow").Id || id == Item.ByName("bone").Id));
    }

    [Fact]
    public void Creeper_loot_drops_record_only_when_killed_by_skeleton()
    {
        FakeWorldContext world = new();
        EntityCreeper creeper = Spawn(world, new EntityCreeper(world), 8.5, 65.0, 8.5);
        EntitySkeleton skeleton = Spawn(world, new EntitySkeleton(world), 12.5, 65.0, 12.5);
        int recordId = Item.ByName("record").Id;

        List<int> withoutSkeleton = CollectDrops(world, creeper, killer: null, rolls: 60);
        Assert.DoesNotContain(recordId, withoutSkeleton);
        Assert.All(withoutSkeleton, id => Assert.Equal(Item.ByName("gunpowder").Id, id));

        List<int> withSkeleton = CollectDrops(world, creeper, skeleton, rolls: 60);
        Assert.Contains(recordId, withSkeleton);
    }

    [Fact]
    public void Sheep_loot_drops_one_wool_stamped_with_its_fleece_colour()
    {
        FakeWorldContext world = new();
        EntityAnimal sheep = Spawn(world, (EntityAnimal)EntityRegistry.ByName("sheep").Create(world), 8.5, 65.0, 8.5);
        ((WoolBehavior)EntityRegistry.ByName("sheep").Behaviors.Interactable!).SetColorOn(sheep, 4);

        List<EntityItem> drops = CollectDropStacks(world, sheep, killer: null, rolls: 1);

        EntityItem wool = Assert.Single(drops);
        Assert.Equal(BlockRegistry.Get("wool").id, wool.Stack.ItemId);
        Assert.Equal(4, wool.Stack.getDamage());
    }

    [Fact]
    public void Pig_loot_drops_cooked_porkchop_only_while_on_fire()
    {
        FakeWorldContext world = new();
        TestPig pig = Spawn(world, new TestPig(world), 8.5, 65.0, 8.5);

        List<int> raw = CollectDrops(world, pig, killer: null, rolls: 60);
        Assert.NotEmpty(raw);
        Assert.All(raw, id => Assert.Equal(Item.ByName("porkchop_raw").Id, id));

        pig.Ignite();
        List<int> cooked = CollectDrops(world, pig, killer: null, rolls: 60);
        Assert.NotEmpty(cooked);
        Assert.All(cooked, id => Assert.Equal(Item.ByName("porkchop_cooked").Id, id));
    }

    [Fact]
    public void Slime_loot_drops_slimeballs_only_at_the_smallest_size()
    {
        FakeWorldContext world = new();
        EntitySlime slime = Spawn(world, new EntitySlime(world), 8.5, 65.0, 8.5);

        slime.SlimeSize = 2;
        Assert.Empty(CollectDrops(world, slime, killer: null, rolls: 60));

        slime.SlimeSize = 1;
        List<int> drops = CollectDrops(world, slime, killer: null, rolls: 60);
        Assert.NotEmpty(drops);
        Assert.All(drops, id => Assert.Equal(Item.ByName("slimeball").Id, id));
    }

    [Fact]
    public void Squid_loot_always_drops_at_least_one_ink_sac()
    {
        FakeWorldContext world = new();
        EntitySquid squid = Spawn(world, new EntitySquid(world), 8.5, 65.0, 8.5);

        for (int roll = 0; roll < 20; roll++)
        {
            Assert.NotEmpty(CollectDrops(world, squid, killer: null, rolls: 1));
        }
    }

    [Fact]
    public void Wolf_has_no_loot_behavior()
    {
        FakeWorldContext world = new();
        Assert.Null(new EntityWolf(world).Loot);
    }

    [Fact]
    public void Slime_split_spawns_half_sized_children_on_death()
    {
        FakeWorldContext world = new();
        TestSlime slime = Spawn(world, new TestSlime(world), 8.5, 65.0, 8.5);
        slime.SlimeSize = 4;
        slime.Kill();

        slime.MarkDead();

        List<EntitySlime> children = world.Entities.Entities.OfType<EntitySlime>().Where(s => !ReferenceEquals(s, slime)).ToList();
        Assert.Equal(4, children.Count);
        Assert.All(children, child => Assert.Equal(2, child.SlimeSize));
    }

    [Fact]
    public void Slime_split_does_not_fire_for_the_smallest_size()
    {
        FakeWorldContext world = new();
        TestSlime slime = Spawn(world, new TestSlime(world), 8.5, 65.0, 8.5);
        slime.SlimeSize = 1;
        slime.Kill();

        slime.MarkDead();

        Assert.DoesNotContain(world.Entities.Entities.OfType<EntitySlime>(), s => !ReferenceEquals(s, slime));
    }

    [Fact]
    public void Pig_lightning_converts_the_pig_into_a_zombie_pigman()
    {
        FakeWorldContext world = new();
        EntityAnimal pig = Spawn(world, (EntityAnimal)EntityRegistry.ByName("pig").Create(world), 8.5, 65.0, 8.5);

        pig.OnStruckByLightning(new EntityLightningBolt(world, pig.X, pig.Y, pig.Z));

        Assert.True(pig.Dead);
        Assert.Single(world.Entities.Entities, e => e.GetType().Name == "EntityPigZombie");
        // The default fire/damage response is suppressed, so the pig never burns on the way out.
        Assert.False(pig.IsOnFire);
    }

    [Fact]
    public void Creeper_keeps_its_own_lightning_override_and_has_no_lifecycle_slot()
    {
        FakeWorldContext world = new();
        EntityCreeper creeper = Spawn(world, new EntityCreeper(world), 8.5, 65.0, 8.5);

        Assert.Null(creeper.Lifecycle);

        creeper.OnStruckByLightning(new EntityLightningBolt(world, creeper.X, creeper.Y, creeper.Z));

        Assert.True(creeper.Powered.Value);
    }

    private static T Spawn<T>(FakeWorldContext world, T entity, double x, double y, double z) where T : Entity
    {
        entity.SetPositionAndAngles(x, y, z, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(entity));
        return entity;
    }

    /// <summary>
    /// Invokes the mob's Loot slot <paramref name="rolls"/> times and returns every dropped item id.
    /// Repeating the roll avoids depending on a lucky RNG seed for behaviors whose count is 0-2.
    /// </summary>
    private static List<int> CollectDrops(FakeWorldContext world, EntityLiving mob, Entity? killer, int rolls) =>
        CollectDropStacks(world, mob, killer, rolls).Select(item => item.Stack.ItemId).ToList();

    private static List<EntityItem> CollectDropStacks(FakeWorldContext world, EntityLiving mob, Entity? killer, int rolls)
    {
        HashSet<EntityItem> before = world.Entities.Entities.OfType<EntityItem>().ToHashSet();

        for (int roll = 0; roll < rolls; roll++)
        {
            mob.Loot!.DropLoot(mob, killer);
        }

        return world.Entities.Entities.OfType<EntityItem>().Where(item => !before.Contains(item)).ToList();
    }

    private sealed class TestZombie(IWorldContext world) : EntityZombie(world)
    {
        public int ExposedAttackTime => AttackTime;
        public void ForceAttack(Entity target, float distance) => attackEntity(target, distance);
    }

    private sealed class TestPig(IWorldContext world) : EntityAnimal(world, EntityRegistry.ByName("pig"))
    {
        public void Ignite() => FireTicks = 100;
    }

    private sealed class TestSlime(IWorldContext world) : EntitySlime(world)
    {
        public void Kill() => Health = 0;
    }
}
