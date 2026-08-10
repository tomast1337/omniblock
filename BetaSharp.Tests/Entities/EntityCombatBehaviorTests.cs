using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Tests.Entities;

[Collection("EntityTests")]
public sealed class EntityCombatBehaviorTests
{
    [Fact]
    public void Skeleton_attack_spawns_arrow_and_sets_cooldown()
    {
        FakeWorldContext world = new();
        var skeleton = new TestSkeleton(world);
        skeleton.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(skeleton));

        var pig = (EntityCreature)EntityRegistry.ByName("pig").Create(world);
        pig.SetPositionAndAngles(10.0, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(pig));

        skeleton.ForceAttack(pig, 2.0f);

        Assert.Equal(30, skeleton.ExposedAttackTime);
        Assert.True(skeleton.ExposedHasAttacked);
        Assert.Equal(Item.ByName("bow").Id, skeleton.HeldItem.ItemId);
    }

    [Fact]
    public void Spider_attack_performs_leap()
    {
        FakeWorldContext world = new();
        var spider = new TestSpider(world);
        spider.OnGround = true;
        spider.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(spider));

        var pig = (EntityCreature)EntityRegistry.ByName("pig").Create(world);
        pig.SetPositionAndAngles(11.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(pig));

        // know good seed for leap attack.
        spider.Random.SetSeed(0);
        spider.ForceAttack(pig, 3.0f);
        Assert.True(spider.VelocityY > 0.0);
    }

    [Fact]
    public void Wolf_damage_from_player_sets_angry_and_target()
    {
        FakeWorldContext world = new();
        EntityCreature wolf = (EntityCreature)EntityRegistry.ByName("wolf").Create(world);
        wolf.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(wolf));

        var player = new TestEntityPlayer(world) { Name = "tester" };
        player.SetPositionAndAngles(9.0, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));

        Assert.True(wolf.Damage(player, 1));

        TameableBehavior tame = wolf.Behaviors.Find<TameableBehavior>()!;
        Assert.True(tame.IsAngry(wolf));
        Assert.Same(player, wolf.Target);

        // The texture follows the mood, and is republished at the end of the tick that set it.
        wolf.Tick();
        Assert.Contains("angry", wolf.GetTexture(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Wolf_server_status_shaking_branch_executes_without_throwing()
    {
        FakeWorldContext world = new();
        EntityCreature wolf = (EntityCreature)EntityRegistry.ByName("wolf").Create(world);

        wolf.ProcessServerEntityStatus(8);
        wolf.Tick();

        Assert.True(wolf.Behaviors.Find<ShakeOffWaterBehavior>()!.Shading(wolf, 0.5f) >= 0f);
    }

    [Fact]
    public void Projectile_velocity_client_sets_angles_for_arrow_egg_and_snowball()
    {
        FakeWorldContext world = new();

        Entity arrow = EntityRegistry.ByName("arrow").Create(world);
        arrow.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        arrow.SetVelocityClient(0.2, 0.1, -0.3);
        Assert.NotEqual(0f, arrow.Yaw);
        Assert.NotEqual(0f, arrow.Pitch);

        Entity egg = EntityRegistry.ByName("egg").Create(world);
        egg.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        egg.SetVelocityClient(0.2, 0.1, -0.3);
        Assert.NotEqual(0f, egg.Yaw);
        Assert.NotEqual(0f, egg.Pitch);

        Entity snowball = EntityRegistry.ByName("snowball").Create(world);
        snowball.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        snowball.SetVelocityClient(0.2, 0.1, -0.3);
        Assert.NotEqual(0f, snowball.Yaw);
        Assert.NotEqual(0f, snowball.Pitch);
    }

    [Fact]
    public void Wolf_nbt_roundtrip_preserves_owner_and_sitting_state()
    {
        FakeWorldContext worldA = new();
        EntityCreature wolf = (EntityCreature)EntityRegistry.ByName("wolf").Create(worldA);
        TameableBehavior tame = wolf.Behaviors.Find<TameableBehavior>()!;
        wolf.Synced<string?>("owner")!.Value = "owner";
        tame.SetSitting(wolf, true);
        wolf.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        var nbt = new NBTTagCompound();
        Assert.True(wolf.SaveSelfNbt(nbt));

        FakeWorldContext worldB = new();
        EntityLiving loaded = Assert.IsAssignableFrom<EntityLiving>(EntityRegistry.GetEntityFromNbt(nbt, worldB));
        Assert.Equal("wolf", EntityRegistry.GetId(loaded));
        Assert.Equal("owner", tame.Owner(loaded));
        Assert.True(tame.IsSitting(loaded));
        Assert.True(tame.IsTamed(loaded));
    }

    private sealed class TestSkeleton(IWorldContext world) : EntityCreature(world, EntityRegistry.ByName("skeleton"))
    {
        public int ExposedAttackTime => AttackTime;
        public bool ExposedHasAttacked => HasAttacked;
        public void ForceAttack(Entity target, float distance) => attackEntity(target, distance);
    }

    private sealed class TestSpider(IWorldContext world) : EntityCreature(world, EntityRegistry.ByName("spider"))
    {
        public void ForceAttack(Entity target, float distance) => attackEntity(target, distance);
    }
}
