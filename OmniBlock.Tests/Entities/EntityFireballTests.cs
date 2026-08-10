using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.NBT;
using OmniBlock.Tests.TestSupport;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Covers the fireball, the seventh non-living entity to lose its class. Its flight is powered —
/// a constant acceleration vector instead of gravity — and its damage response is the deflection
/// that makes the ghast duel work: a punch re-aims it along the attacker's look vector.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityFireballTests
{
    private static FireballBehavior Flight => EntityRegistry.ByName("fireball").Behaviors.Find<FireballBehavior>()!;

    private static EntityLiving Ghast(FakeWorldContext world)
    {
        EntityLiving ghast = (EntityLiving)EntityRegistry.ByName("ghast").Create(world);
        ghast.SetPositionAndAngles(8.5, 70.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(ghast));
        return ghast;
    }

    [Fact]
    public void A_fireball_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();
        Entity fireball = EntityRegistry.ByName("fireball").Create(world);

        Assert.Equal(typeof(EntityObject), fireball.GetType());
        Assert.Equal(1.0F, fireball.TargetingMargin);
        Assert.True(fireball.HasCollision);
    }

    [Fact]
    public void A_shot_starts_on_the_shooter_with_a_normalised_power_vector()
    {
        FakeWorldContext world = new();
        EntityLiving ghast = Ghast(world);

        Entity fireball = FireballBehavior.Shoot(world, ghast, 10.0, 0.0, 0.0);

        Assert.Same(ghast, Flight.Owner(fireball));
        double power = Math.Sqrt(
            Flight.PowerX(fireball) * Flight.PowerX(fireball) +
            Flight.PowerY(fireball) * Flight.PowerY(fireball) +
            Flight.PowerZ(fireball) * Flight.PowerZ(fireball));
        Assert.Equal(0.1, power, 5);
    }

    /// <summary>No gravity: the power vector accelerates the fireball every tick until drag balances it.</summary>
    [Fact]
    public void A_fireball_accelerates_along_its_power_and_burns()
    {
        FakeWorldContext world = new();
        Entity fireball = EntityRegistry.ByName("fireball").Create(world);
        fireball.SetPositionAndAngles(8.5, 80.0, 8.5, 0f, 0f);
        Flight.SetDirection(fireball, 1.0, 0.0, 0.0);
        Assert.True(world.Entities.SpawnEntity(fireball));

        fireball.Tick();
        double speedAfterOne = fireball.VelocityX;
        fireball.Tick();

        Assert.True(speedAfterOne > 0.0);
        Assert.True(fireball.VelocityX > speedAfterOne);
        Assert.True(fireball.IsOnFire);
    }

    [Fact]
    public void A_fireball_explodes_when_it_hits_the_ground()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);
        Entity fireball = EntityRegistry.ByName("fireball").Create(world);
        fireball.SetPositionAndAngles(8.5, 70.0, 8.5, 0f, 0f);
        Flight.SetDirection(fireball, 0.0, -1.0, 0.0);
        Assert.True(world.Entities.SpawnEntity(fireball));

        for (int tick = 0; tick < 100 && !fireball.Dead; tick++) fireball.Tick();

        Assert.True(fireball.Dead);
    }

    /// <summary>The deflection: a punch never lands, it re-aims velocity and power along the attacker's look.</summary>
    [Fact]
    public void A_punch_deflects_the_fireball_along_the_attackers_look()
    {
        FakeWorldContext world = new();
        TestEntityPlayer player = new(world) { Name = "deflector" };
        player.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));

        Entity fireball = EntityRegistry.ByName("fireball").Create(world);
        fireball.SetPositionAndAngles(8.5, 65.0, 10.5, 0f, 0f);
        Flight.SetDirection(fireball, 0.0, 0.0, -1.0);
        Assert.True(world.Entities.SpawnEntity(fireball));

        Assert.True(fireball.Damage(player, 0));

        Assert.False(fireball.Dead);
        Assert.Equal(fireball.VelocityX * 0.1, Flight.PowerX(fireball), 9);
        Assert.Equal(fireball.VelocityY * 0.1, Flight.PowerY(fireball), 9);
        Assert.Equal(fireball.VelocityZ * 0.1, Flight.PowerZ(fireball), 9);
    }

    [Fact]
    public void Flight_state_survives_an_nbt_round_trip()
    {
        FakeWorldContext world = new();
        Entity fireball = EntityRegistry.ByName("fireball").Create(world);
        fireball.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        NBTTagCompound nbt = new();
        fireball.Write(nbt);
        nbt.SetShort("xTile", 3);
        nbt.SetByte("inTile", (sbyte)4);
        nbt.SetByte("inGround", 1);

        Entity restored = EntityRegistry.ByName("fireball").Create(world);
        restored.Read(nbt);
        NBTTagCompound written = new();
        restored.Write(written);

        Assert.Equal(3, written.GetShort("xTile"));
        Assert.Equal(4, written.GetByte("inTile"));
        Assert.Equal(1, written.GetByte("inGround"));
    }

    [Fact]
    public void Protocol_facts_are_pinned()
    {
        EntityDefinition fireball = EntityRegistry.ByName("fireball").RequireDefinition();

        Assert.Equal(63, fireball.ProtocolId);
        Assert.Equal(63, fireball.SpawnObjectId);
        Assert.False(fireball.TracksVelocity);
        Assert.Equal(64, fireball.TrackingRange);
    }
}
