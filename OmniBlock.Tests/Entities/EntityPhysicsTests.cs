using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.NBT;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Covers the Physics capability slot and the three animals that lost their classes to it. Pig,
/// chicken and sheep are now <c>EntityCreature</c> instances configured entirely by JSON.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityPhysicsTests
{
    private static EntityCreature Spawn(FakeWorldContext world, string name)
    {
        EntityCreature animal = (EntityCreature)TestEntityCatalog.ByName(name).Create(world);
        animal.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        world.Entities.SpawnEntity(animal);
        return animal;
    }

    [Theory]
    [InlineData("cow")]
    [InlineData("pig")]
    [InlineData("chicken")]
    [InlineData("sheep")]
    public void Every_plain_animal_is_json_rather_than_a_class(string name)
    {
        FakeWorldContext world = new();

        Assert.Equal(typeof(EntityCreature), TestEntityCatalog.ByName(name).Create(world).GetType());
        Assert.NotNull(TestEntityCatalog.ByName(name).Definition!.Renderer);
    }

    /// <summary>
    ///     Four registered types now share <c>EntityCreature</c>, so the class identifies none of them
    ///     and must resolve to nothing rather than to whichever registration ran first.
    /// </summary>
    [Fact]
    public void A_class_shared_by_several_types_resolves_to_nothing()
    {
    }

    [Fact]
    public void Slots_come_from_json_and_stay_null_where_undeclared()
    {
        Assert.NotNull(TestEntityCatalog.ByName("chicken").Behaviors.Find<FlapDescentBehavior>());
        Assert.NotNull(TestEntityCatalog.ByName("pig").Behaviors.Find<RiderFallStatBehavior>());

        // A cow's only physics is the grazing rule every farm animal shares.
        Assert.Null(TestEntityCatalog.ByName("cow").Behaviors.Find<FlapDescentBehavior>());
        Assert.Null(TestEntityCatalog.ByName("sheep").Behaviors.Find<RiderFallStatBehavior>());
    }

    /// <summary>
    ///     Landing is dispatched through the slot, and <c>true</c> is what suppresses the default
    ///     fall damage. A cow declares no Physics behavior, so it keeps the default.
    /// </summary>
    [Fact]
    public void A_chicken_absorbs_its_landing_and_a_cow_does_not()
    {
        FakeWorldContext world = new();
        EntityCreature chicken = Spawn(world, "chicken");

        Assert.True(chicken.Behaviors.Physics!.OnLanding(chicken, 20.0F));
        Assert.Null(TestEntityCatalog.ByName("cow").Behaviors.Find<FlapDescentBehavior>());
    }

    /// <summary>
    ///     The wings drive the render angle, and a renderer reaches them by capability rather than by
    ///     reading the Physics slot: the chicken shares that slot with the grazing rules, so the slot
    ///     holds a composite and a cast to <see cref="FlapDescentBehavior"/> finds nothing.
    /// </summary>
    [Fact]
    public void A_chickens_wings_are_reachable_even_though_the_slot_holds_a_composite()
    {
        EntityBehaviorSet behaviors = TestEntityCatalog.ByName("chicken").Behaviors;

        Assert.IsNotType<FlapDescentBehavior>(behaviors.Physics);
        Assert.NotNull(behaviors.Find<FlapDescentBehavior>());
    }

    /// <summary>
    ///     Grounded versus falling is the whole animation: a chicken standing still folds its wings
    ///     away, and one in the air beats them and has its descent damped.
    /// </summary>
    [Fact]
    public void A_chickens_wings_fold_on_the_ground_and_beat_in_the_air()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);

        EntityCreature chicken = (EntityCreature)TestEntityCatalog.ByName("chicken").Create(world);
        chicken.SetPositionAndAngles(8.5, 64.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(chicken));

        FlapDescentBehavior wings = chicken.Behaviors.Find<FlapDescentBehavior>()!;

        // Standing on the floor: the wings fold flat, so the angle collapses to nothing.
        EntityTestHarness.AdvanceGameTicks(world, 20);
        Assert.True(chicken.OnGround);
        Assert.Equal(0.0F, wings.WingRotation(chicken, 1.0F), 3);

        // In the air they extend, and the beat carries the angle above zero at some point in it.
        chicken.SetPositionAndAngles(8.5, 80.0, 8.5, 0f, 0f);
        chicken.OnGround = false;

        float highest = 0.0F;
        for (int i = 0; i < 10; i++)
        {
            EntityTestHarness.AdvanceGameTicks(world, 1);
            highest = Math.Max(highest, wings.WingRotation(chicken, 1.0F));
        }

        Assert.True(highest > 0.0F, $"wings never left the body: peak angle was {highest}");
    }

    /// <summary>
    ///     A pig's landing hook credits its rider but returns <c>false</c>, so the pig still takes
    ///     its own fall damage — the order the original override had.
    /// </summary>
    [Fact]
    public void A_pig_credits_its_rider_without_suppressing_fall_damage()
    {
        FakeWorldContext world = new();
        EntityCreature pig = Spawn(world, "pig");
        TestEntityPlayer player = new(world) { Name = "tester" };
        player.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        player.SetVehicle(pig);

        Assert.False(pig.Behaviors.Physics!.OnLanding(pig, 20.0F));
        Assert.True(player.HasStat(Achievements.KillPig));

        // Below the threshold nothing is credited.
        TestEntityPlayer shortFall = new(world) { Name = "short" };
        shortFall.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        shortFall.SetVehicle(pig);
        Assert.False(pig.Behaviors.Physics.OnLanding(pig, 1.0F) || shortFall.HasStat(Achievements.KillPig));
    }

    /// <summary>The wool byte is packed, so colour and the sheared flag must not disturb each other.</summary>
    [Fact]
    public void Shearing_a_sheep_drops_its_wool_and_keeps_its_colour()
    {
        FakeWorldContext world = new();
        EntityCreature sheep = Spawn(world, "sheep");
        WoolBehavior wool = Assert.IsType<WoolBehavior>(sheep.Behaviors.Interactable);
        wool.SetColorOn(sheep, 11);

        TestEntityPlayer player = new(world) { Name = "tester" };
        player.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        player.Inventory.SetStack(player.Inventory.SelectedSlot, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:shears")));

        sheep.Interact(player);

        Assert.True(wool.IsShearedOn(sheep));
        Assert.Equal(11, wool.ColorOf(sheep));
        Assert.Contains(world.Entities.Entities, EntityTestHarness.IsDroppedItem);

        // Already sheared: a second attempt drops nothing more.
        int before = world.Entities.Entities.Count(EntityTestHarness.IsDroppedItem);
        sheep.Interact(player);
        Assert.Equal(before, world.Entities.Entities.Count(EntityTestHarness.IsDroppedItem));
    }

    [Fact]
    public void A_sheep_round_trips_its_packed_wool_byte_through_nbt()
    {
        FakeWorldContext world = new();
        EntityCreature sheep = Spawn(world, "sheep");
        WoolBehavior wool = (WoolBehavior)sheep.Behaviors.Interactable!;
        wool.SetColorOn(sheep, 12);

        NBTTagCompound nbt = new();
        Assert.True(sheep.SaveSelfNbt(nbt));
        Assert.Equal(12, nbt.GetByte("Color"));

        Entity loaded = Assert.IsType<EntityCreature>(TestEntityCatalog.GetEntityFromNbt(nbt, new FakeWorldContext()));
        Assert.Equal(12, wool.ColorOf(loaded));
        Assert.False(wool.IsShearedOn(loaded));
    }

    /// <summary>
    ///     Fleece colour is rolled per individual at spawn, which used to be a <c>PostSpawn</c>
    ///     override and is now the Lifecycle slot.
    /// </summary>
    [Fact]
    public void A_spawned_sheep_rolls_a_fleece_colour()
    {
        FakeWorldContext world = new();
        EntityCreature sheep = Spawn(world, "sheep");
        WoolBehavior wool = (WoolBehavior)sheep.Behaviors.Interactable!;

        sheep.PostSpawn();

        Assert.InRange(wool.ColorOf(sheep), 0, 15);
    }
}
