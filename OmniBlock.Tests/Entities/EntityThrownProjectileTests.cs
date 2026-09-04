using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.NBT;

namespace OmniBlock.Tests.Entities;

/// <summary>
///     Covers the thrown projectiles, the fifth and sixth non-living entities to lose their classes —
///     and the first pair to share one behavior: the snowball and the egg were the same flight verbatim,
///     so ThrownProjectileBehavior flies both and the egg's hatch roll is declared as data.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityThrownProjectileTests
{
    private static ThrownProjectileBehavior Thrown(string typeName) =>
        TestEntityCatalog.ByName(typeName).Behaviors.Find<ThrownProjectileBehavior>()!;

    private static TestEntityPlayer Player(FakeWorldContext world)
    {
        TestEntityPlayer player = new(world)
        {
            Name = "thrower"
        };
        player.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));
        return player;
    }

    [Fact]
    public void Thrown_projectiles_have_no_class_of_their_own()
    {
        FakeWorldContext world = new();

        Assert.Equal(typeof(EntityObject), TestEntityCatalog.ByName("snowball").Create(world).GetType());
        Assert.Equal(typeof(EntityObject), TestEntityCatalog.ByName("egg").Create(world).GetType());
        Assert.Same(TestEntityCatalog.ByName("snowball").Behaviors.Ticker, TestEntityCatalog.ByName("snowball").Behaviors.Interactable);
    }

    [Fact]
    public void A_throw_launches_from_the_throwers_eyes_and_remembers_them()
    {
        FakeWorldContext world = new();
        var player = Player(world);

        var snowball = ThrownProjectileBehavior.Throw(world, "snowball", player);

        Assert.Same(player, Thrown("snowball").Thrower(snowball));
        Assert.True(Math.Abs(snowball.Y - (player.Y + player.EyeHeight - 0.1)) < 0.01);
        Assert.True(snowball.VelocityX * snowball.VelocityX + snowball.VelocityY * snowball.VelocityY + snowball.VelocityZ * snowball.VelocityZ > 0.01);
    }

    [Fact]
    public void A_projectile_arcs_under_gravity_in_flight()
    {
        FakeWorldContext world = new();
        var snowball = TestEntityCatalog.ByName("snowball").Create(world);
        snowball.SetPositionAndAngles(8.5, 80.0, 8.5, 0f, 0f);
        snowball.VelocityX = 0.2;
        Assert.True(world.Entities.SpawnEntity(snowball));

        var velocityBefore = snowball.VelocityY;
        snowball.Tick();

        Assert.True(snowball.VelocityY < velocityBefore);
        Assert.False(snowball.Dead);
    }

    [Fact]
    public void A_projectile_pops_when_it_hits_the_ground()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);
        var snowball = TestEntityCatalog.ByName("snowball").Create(world);
        snowball.SetPositionAndAngles(8.5, 66.0, 8.5, 0f, 0f);
        snowball.VelocityY = -0.4;
        Assert.True(world.Entities.SpawnEntity(snowball));

        for (var tick = 0; tick < 50 && !snowball.Dead; tick++) snowball.Tick();

        Assert.True(snowball.Dead);
    }

    [Fact]
    public void A_projectile_pops_when_it_hits_an_entity()
    {
        FakeWorldContext world = new();
        var pig = TestEntityCatalog.ByName("pig").Create(world);
        pig.SetPositionAndAngles(8.5, 64.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(pig));

        var snowball = TestEntityCatalog.ByName("snowball").Create(world);
        snowball.SetPositionAndAngles(8.5, 64.5, 6.5, 0f, 0f);
        snowball.VelocityZ = 0.5;
        Assert.True(world.Entities.SpawnEntity(snowball));

        for (var tick = 0; tick < 20 && !snowball.Dead; tick++) snowball.Tick();

        Assert.True(snowball.Dead);
    }

    /// <summary>The hatch roll is egg data: enough impacts always produce at least one chicken.</summary>
    [Fact]
    public void Only_the_egg_hatches_chickens_on_impact()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);

        for (var i = 0; i < 200; i++)
        {
            var egg = TestEntityCatalog.ByName("egg").Create(world);
            egg.SetPositionAndAngles(8.5, 66.0, 8.5, 0f, 0f);
            egg.VelocityY = -0.4;
            Assert.True(world.Entities.SpawnEntity(egg));
            for (var tick = 0; tick < 50 && !egg.Dead; tick++) egg.Tick();
            Assert.True(egg.Dead);
        }

        Assert.Contains(world.Entities.Entities, e => TestEntityCatalog.GetId(e) == "chicken");
    }

    [Fact]
    public void Flight_state_survives_an_nbt_round_trip()
    {
        FakeWorldContext world = new();
        var snowball = TestEntityCatalog.ByName("snowball").Create(world);
        snowball.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        NBTTagCompound nbt = new();
        snowball.Write(nbt);
        nbt.SetShort("xTile", 3);
        nbt.SetShort("yTile", 64);
        nbt.SetShort("zTile", 7);
        nbt.SetByte("inTile", 1);
        nbt.SetByte("shake", 5);
        nbt.SetByte("inGround", 1);

        var restored = TestEntityCatalog.ByName("snowball").Create(world);
        restored.Read(nbt);
        NBTTagCompound written = new();
        restored.Write(written);

        Assert.Equal(3, written.GetShort("xTile"));
        Assert.Equal(64, written.GetShort("yTile"));
        Assert.Equal(7, written.GetShort("zTile"));
        Assert.Equal(1, written.GetByte("inTile"));
        Assert.Equal(5, written.GetByte("shake"));
        Assert.Equal(1, written.GetByte("inGround"));
    }

    [Fact]
    public void Protocol_facts_are_pinned()
    {
        var snowball = TestEntityCatalog.ByName("snowball").RequireDefinition();
        var egg = TestEntityCatalog.ByName("egg").RequireDefinition();

        Assert.Equal(11, snowball.ProtocolId);
        Assert.Equal(61, snowball.SpawnObjectId);
        Assert.Equal(62, egg.ProtocolId);
        Assert.Equal(62, egg.SpawnObjectId);
        Assert.True(snowball.TracksVelocity);
        Assert.True(egg.TracksVelocity);
        Assert.Equal(4.0, snowball.RenderDistanceWeight);
    }
}
