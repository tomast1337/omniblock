using OmniBlock.Blocks;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.Tests.TestSupport;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Covers the boat, the twelfth non-living entity to lose its class — and the first vehicle: it
/// carries a passenger, is steered by them leaning rather than by controls of its own, and comes
/// apart into planks and sticks when it takes enough damage or hits a wall at speed.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityBoatTests
{
    private static BoatBehavior Hull => EntityRegistry.ByName("boat").Behaviors.Find<BoatBehavior>()!;

    private static Entity Launch(FakeWorldContext world, double x = 8.5, double y = 65.0, double z = 8.5)
    {
        Entity boat = BoatBehavior.Launch(world, x, y, z);
        Assert.True(world.Entities.SpawnEntity(boat));
        return boat;
    }

    private static TestEntityPlayer Sailor(FakeWorldContext world)
    {
        TestEntityPlayer player = new(world) { Name = "sailor" };
        player.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));
        return player;
    }

    private static int CountDropped(FakeWorldContext world, string itemName)
    {
        Assert.True(ContentRuntime.Current.Items.TryParse(itemName, out ItemStack? resolved));
        int itemId = resolved.ItemId;
        return world.Entities.Entities
            .Select(EntityTestHarness.DroppedStack)
            .Where(stack => stack is not null && stack.ItemId == itemId)
            .Sum(stack => stack!.Count);
    }

    [Fact]
    public void A_boat_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();
        Entity boat = Launch(world);

        Assert.Equal(typeof(EntityObject), boat.GetType());
        Assert.True(boat.HasCollision);
        Assert.True(boat.IsPushable);
        Assert.True(boat.PreventEntitySpawning);
    }

    /// <summary>A hull is something you collide with, not a marker you walk through.</summary>
    [Fact]
    public void A_boat_is_a_solid_shape_others_collide_with()
    {
        FakeWorldContext world = new();
        Entity boat = Launch(world);
        TestEntityPlayer sailor = Sailor(world);

        Assert.NotNull(boat.GetBoundingBox());
        Assert.NotNull(boat.GetCollisionAgainstShape(sailor));
    }

    [Fact]
    public void Interacting_puts_the_player_aboard()
    {
        FakeWorldContext world = new();
        Entity boat = Launch(world);
        TestEntityPlayer sailor = Sailor(world);

        Assert.True(boat.Interact(sailor));

        Assert.Same(boat, sailor.Vehicle);
        Assert.Same(sailor, boat.Passenger);
    }

    /// <summary>A boat someone else is already sitting in refuses the second player.</summary>
    [Fact]
    public void An_occupied_boat_refuses_another_player()
    {
        FakeWorldContext world = new();
        Entity boat = Launch(world);
        TestEntityPlayer first = Sailor(world);
        TestEntityPlayer second = new(world) { Name = "stowaway" };
        second.SetPositionAndAngles(8.5, 65.0, 9.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(second));

        boat.Interact(first);
        boat.Interact(second);

        Assert.Same(first, boat.Passenger);
        Assert.Null(second.Vehicle);
    }

    /// <summary>The rider sits down inside the hull, not on top of it — a zero height scale.</summary>
    [Fact]
    public void The_rider_sits_down_inside_the_hull()
    {
        FakeWorldContext world = new();
        Entity boat = Launch(world);
        TestEntityPlayer sailor = Sailor(world);
        boat.Interact(sailor);

        boat.UpdatePassengerPosition();

        Assert.True(sailor.Y < boat.Y + sailor.StandingEyeHeight,
            "A passenger seated at -0.3 rides below where a straight mount would put them.");
    }

    [Fact]
    public void A_hit_rocks_the_hull_the_other_way_each_time()
    {
        FakeWorldContext world = new();
        Entity boat = Launch(world);

        int restingDirection = Hull.RockDirection(boat);
        Assert.True(boat.Damage(null, 1));

        Assert.Equal(-restingDirection, Hull.RockDirection(boat));
        Assert.Equal(10, Hull.TimeSinceHit(boat));
        Assert.False(boat.Dead);

        boat.Damage(null, 1);
        Assert.Equal(restingDirection, Hull.RockDirection(boat));
    }

    /// <summary>Past forty points the hull comes apart into exactly what built it.</summary>
    [Fact]
    public void Enough_damage_breaks_the_boat_into_planks_and_sticks()
    {
        FakeWorldContext world = new();
        Entity boat = Launch(world);

        Assert.True(boat.Damage(null, 5));

        Assert.True(boat.Dead);
        Assert.Equal(3, CountDropped(world, "planks"));
        Assert.Equal(2, CountDropped(world, "stick"));
    }

    /// <summary>Breaking up throws the rider clear rather than trapping them in a dead boat.</summary>
    [Fact]
    public void Breaking_up_ejects_the_rider()
    {
        FakeWorldContext world = new();
        Entity boat = Launch(world);
        TestEntityPlayer sailor = Sailor(world);
        boat.Interact(sailor);

        boat.Damage(null, 5);

        Assert.True(boat.Dead);
        Assert.Null(sailor.Vehicle);
    }

    /// <summary>Buoyancy is sampled in slices: out of water the hull just falls.</summary>
    [Fact]
    public void A_boat_out_of_water_sinks_towards_the_ground()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);
        Entity boat = Launch(world, y: 70.0);

        double startY = boat.Y;
        boat.Tick();

        Assert.True(boat.VelocityY < 0.0);
        Assert.True(boat.Y < startY);
    }

    /// <summary>In water the same sampling pushes it back up to the surface instead.</summary>
    [Fact]
    public void A_submerged_boat_is_pushed_back_up()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 60);
        EntityTestHarness.FillWaterColumn(world, 8, 8, 61, 68);
        for (int x = 7; x <= 10; x++)
        {
            for (int z = 7; z <= 10; z++) EntityTestHarness.FillWaterColumn(world, x, z, 61, 68);
        }

        Entity boat = Launch(world, y: 64.0);
        boat.VelocityY = -0.3;
        boat.Tick();

        Assert.True(boat.VelocityY > -0.3, "Buoyancy halves a downward drift and adds lift.");
    }

    [Fact]
    public void A_boat_ploughs_through_snow_rather_than_riding_over_it()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);
        int snowId = BlockRegistry.Get("snow").Id;
        for (int x = 7; x <= 10; x++)
        {
            for (int z = 7; z <= 10; z++) world.Writer.SetBlock(x, 64, z, snowId);
        }

        Entity boat = Launch(world, y: 64.0);
        boat.Tick();

        Assert.Contains(
            from x in Enumerable.Range(7, 4)
            from z in Enumerable.Range(7, 4)
            select world.Reader.GetBlockId(x, 64, z),
            id => id == 0);
    }

    [Fact]
    public void Protocol_facts_are_pinned()
    {
        EntityDefinition boat = EntityRegistry.ByName("boat").RequireDefinition();

        Assert.Equal(41, boat.ProtocolId);
        Assert.Equal(1, boat.SpawnObjectId);
        Assert.Equal(160, boat.TrackingRange);
        Assert.Equal(5, boat.TrackingFrequency);
        Assert.True(boat.TracksVelocity);
        Assert.Equal(0.0, boat.PassengerRideHeightScale);
        Assert.Equal(-0.3, boat.PassengerRideOffset);
    }
}
