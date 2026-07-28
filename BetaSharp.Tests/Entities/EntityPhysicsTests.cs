using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.NBT;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers the Physics capability slot and the three animals that lost their classes to it. Pig,
/// chicken and sheep are now <c>EntityAnimal</c> instances configured entirely by JSON.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityPhysicsTests
{
    private static EntityAnimal Spawn(FakeWorldContext world, string name)
    {
        EntityAnimal animal = (EntityAnimal)EntityRegistry.ByName(name).Create(world);
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

        Assert.Equal(typeof(EntityAnimal), EntityRegistry.ByName(name).Create(world).GetType());
        Assert.NotNull(EntityRegistry.ByName(name).Definition!.Renderer);
    }

    /// <summary>
    ///     Four registered types now share <c>EntityAnimal</c>, so the class identifies none of them
    ///     and must resolve to nothing rather than to whichever registration ran first.
    /// </summary>
    [Fact]
    public void A_class_shared_by_several_types_resolves_to_nothing()
    {
        Assert.Null(EntityRegistry.ByRuntimeType(typeof(EntityAnimal)));
    }

    [Fact]
    public void Slots_come_from_json_and_stay_null_where_undeclared()
    {
        Assert.IsType<FlapDescentBehavior>(EntityRegistry.ByName("chicken").Behaviors.Physics);
        Assert.IsType<RiderFallStatBehavior>(EntityRegistry.ByName("pig").Behaviors.Physics);

        Assert.Null(EntityRegistry.ByName("cow").Behaviors.Physics);
        Assert.Null(EntityRegistry.ByName("sheep").Behaviors.Physics);
    }

    /// <summary>
    ///     Landing is dispatched through the slot, and <c>true</c> is what suppresses the default
    ///     fall damage. A cow declares no Physics behavior, so it keeps the default.
    /// </summary>
    [Fact]
    public void A_chicken_absorbs_its_landing_and_a_cow_does_not()
    {
        FakeWorldContext world = new();
        EntityAnimal chicken = Spawn(world, "chicken");

        Assert.True(chicken.Behaviors.Physics!.OnLanding(chicken, 20.0F));
        Assert.Null(EntityRegistry.ByName("cow").Behaviors.Physics);
    }

    /// <summary>
    ///     A pig's landing hook credits its rider but returns <c>false</c>, so the pig still takes
    ///     its own fall damage — the order the original override had.
    /// </summary>
    [Fact]
    public void A_pig_credits_its_rider_without_suppressing_fall_damage()
    {
        FakeWorldContext world = new();
        EntityAnimal pig = Spawn(world, "pig");
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
        EntityAnimal sheep = Spawn(world, "sheep");
        WoolBehavior wool = Assert.IsType<WoolBehavior>(sheep.Behaviors.Interactable);
        wool.SetColorOn(sheep, 11);

        TestEntityPlayer player = new(world) { Name = "tester" };
        player.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        player.Inventory.SetStack(player.Inventory.SelectedSlot, new ItemStack(Item.ByName("shears")));

        sheep.Interact(player);

        Assert.True(wool.IsShearedOn(sheep));
        Assert.Equal(11, wool.ColorOf(sheep));
        Assert.Contains(world.Entities.Entities, e => e is EntityItem);

        // Already sheared: a second attempt drops nothing more.
        int before = world.Entities.Entities.Count(e => e is EntityItem);
        sheep.Interact(player);
        Assert.Equal(before, world.Entities.Entities.Count(e => e is EntityItem));
    }

    [Fact]
    public void A_sheep_round_trips_its_packed_wool_byte_through_nbt()
    {
        FakeWorldContext world = new();
        EntityAnimal sheep = Spawn(world, "sheep");
        WoolBehavior wool = (WoolBehavior)sheep.Behaviors.Interactable!;
        wool.SetColorOn(sheep, 12);

        NBTTagCompound nbt = new();
        Assert.True(sheep.SaveSelfNbt(nbt));
        Assert.Equal(12, nbt.GetByte("Color"));

        Entity loaded = Assert.IsType<EntityAnimal>(EntityRegistry.GetEntityFromNbt(nbt, new FakeWorldContext()));
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
        EntityAnimal sheep = Spawn(world, "sheep");
        WoolBehavior wool = (WoolBehavior)sheep.Behaviors.Interactable!;

        sheep.PostSpawn();

        Assert.InRange(wool.ColorOf(sheep), 0, 15);
    }
}
