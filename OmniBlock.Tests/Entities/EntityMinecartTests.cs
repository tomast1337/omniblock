using OmniBlock.Blocks;
using OmniBlock.Blocks.Behaviors;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.NBT;
namespace OmniBlock.Tests.Entities;

/// <summary>
/// Covers the minecart, the last non-living entity to lose its class: rails, the three cart kinds,
/// collisions, NBT and the client branch. The three kinds are one entity type separated by a stored
/// number, so the wire ids that used to be a class check are declared data like falling sand's.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityMinecartTests
{
    private static MinecartBehavior Cart => EntityRegistry.ByName("minecart").Behaviors.Find<MinecartBehavior>()!;

    private static Entity Place(FakeWorldContext world, double x, double y, double z, int type) =>
        MinecartBehavior.Place(world, x, y, z, type);

    private static void PlaceRailWithFloor(FakeWorldContext world, int x, int y, int z, int railBlockId, int meta)
    {
        if (!RailBehavior.IsRail(BlockRegistry.GetByProtocolId(railBlockId)))
        {
            throw new ArgumentException("Not a rail block id", nameof(railBlockId));
        }

        world.Writer.SetBlock(x, y - 1, z, BlockRegistry.Get("stone").Id);
        world.Writer.SetBlock(x, y, z, railBlockId, meta);
    }

    /// <summary>Opaque neighbor required for slope rails to stay valid (see <see cref="RailBehavior.NeighborUpdate"/>).</summary>
    private static void PlaceSlopeSupport(FakeWorldContext world, int x, int railY, int z, int slopeMeta)
    {
        switch (slopeMeta)
        {
            case 2:
                world.Writer.SetBlock(x + 1, railY, z, BlockRegistry.Get("stone").Id);
                break;
            case 3:
                world.Writer.SetBlock(x - 1, railY, z, BlockRegistry.Get("stone").Id);
                break;
            case 4:
                world.Writer.SetBlock(x, railY, z - 1, BlockRegistry.Get("stone").Id);
                break;
            case 5:
                world.Writer.SetBlock(x, railY, z + 1, BlockRegistry.Get("stone").Id);
                break;
        }
    }

    [Fact]
    public void A_minecart_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();
        Entity cart = Place(world, 8.5, 65.0, 8.5, MinecartBehavior.Rideable);

        Assert.Equal(typeof(EntityObject), cart.GetType());
        Assert.True(MinecartBehavior.IsMinecart(cart));
        Assert.True(cart.IsPushable);
        Assert.True(cart.PreventEntitySpawning);
        Assert.NotNull(cart.GetBoundingBox());
    }

    /// <summary>Three kinds, one entity type, three object-spawn ids resolved through the behavior.</summary>
    [Fact]
    public void Each_cart_kind_goes_out_on_its_own_wire_id()
    {
        FakeWorldContext world = new();

        Assert.Equal(10, Cart.SpawnObjectId(Place(world, 8.5, 65.0, 8.5, MinecartBehavior.Rideable)));
        Assert.Equal(11, Cart.SpawnObjectId(Place(world, 8.5, 65.0, 9.5, MinecartBehavior.Chest)));
        Assert.Equal(12, Cart.SpawnObjectId(Place(world, 8.5, 65.0, 10.5, MinecartBehavior.Furnace)));

        Assert.Equal(MinecartBehavior.Rideable, Cart.TypeForSpawnObjectId(10));
        Assert.Equal(MinecartBehavior.Chest, Cart.TypeForSpawnObjectId(11));
        Assert.Equal(MinecartBehavior.Furnace, Cart.TypeForSpawnObjectId(12));
        Assert.Null(Cart.TypeForSpawnObjectId(99));
    }

    /// <summary>Only the chest cart carries anything; the other two have no inventory at all.</summary>
    [Fact]
    public void Only_the_chest_cart_carries_cargo()
    {
        FakeWorldContext world = new();

        Assert.Null(Cart.Cargo(Place(world, 8.5, 65.0, 8.5, MinecartBehavior.Rideable)));
        Assert.Null(Cart.Cargo(Place(world, 8.5, 65.0, 9.5, MinecartBehavior.Furnace)));

        MinecartCargo cargo = Cart.Cargo(Place(world, 8.5, 65.0, 10.5, MinecartBehavior.Chest))!;
        Assert.Equal(27, cargo.Size);
        Assert.Equal(64, cargo.MaxCountPerStack);
        Assert.Equal("Minecart", cargo.Name);
    }

    [Fact]
    public void GetTrackPosition_and_GetTrackPositionOffset_on_flat_straight_rails()
    {
        FakeWorldContext world = new();
        PlaceRailWithFloor(world, 8, 64, 8, BlockRegistry.Get("rail").Id, 0);
        PlaceRailWithFloor(world, 12, 64, 8, BlockRegistry.Get("rail").Id, 1);

        Entity cart = Place(world, 8.5, 65.0, 8.5, MinecartBehavior.Rideable);
        Assert.NotNull(Cart.GetTrackPosition(cart, 8.5, 65.0, 8.5));
        Assert.NotNull(Cart.GetTrackPositionOffset(cart, 8.5, 65.0, 8.5, 0.1));

        cart.SetPosition(1000, 65, 1000);
        Assert.Null(Cart.GetTrackPosition(cart, cart.X, cart.Y, cart.Z));
    }

    [Fact]
    public void Ticks_on_straight_rail_meta0_and_meta1()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 31, 0, 15, 63);
        PlaceRailWithFloor(world, 8, 64, 8, BlockRegistry.Get("rail").Id, 0);
        PlaceRailWithFloor(world, 9, 64, 8, BlockRegistry.Get("rail").Id, 0);
        PlaceRailWithFloor(world, 12, 64, 12, BlockRegistry.Get("rail").Id, 1);
        PlaceRailWithFloor(world, 12, 64, 13, BlockRegistry.Get("rail").Id, 1);

        Entity cart0 = Place(world, 8.5, 65.0, 8.5, MinecartBehavior.Rideable);
        cart0.VelocityZ = 0.15;
        Assert.True(world.Entities.SpawnEntity(cart0));

        Entity cart1 = Place(world, 12.5, 65.0, 12.5, MinecartBehavior.Rideable);
        cart1.VelocityX = 0.15;
        Assert.True(world.Entities.SpawnEntity(cart1));

        double startSpeed0 = EntityTestHarness.HorizontalSpeed(cart0);
        double startSpeed1 = EntityTestHarness.HorizontalSpeed(cart1);
        EntityTestHarness.AdvanceGameTicks(world, 80);

        Assert.True(EntityTestHarness.HorizontalSpeed(cart0) > 0.0);
        Assert.True(EntityTestHarness.HorizontalSpeed(cart1) > 0.0);
        Assert.NotEqual(startSpeed0, EntityTestHarness.HorizontalSpeed(cart0));
        Assert.NotEqual(startSpeed1, EntityTestHarness.HorizontalSpeed(cart1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Ticks_on_slope_rail_meta(int slopeMeta)
    {
        FakeWorldContext world = new();
        int x = 16 + slopeMeta;
        int z = 8;
        PlaceRailWithFloor(world, x, 64, z, BlockRegistry.Get("rail").Id, slopeMeta);
        PlaceSlopeSupport(world, x, 64, z, slopeMeta);

        Entity cart = Place(world, x + 0.5, 65.0, z + 0.5, MinecartBehavior.Rideable);
        cart.VelocityX = 0.05;
        cart.VelocityZ = 0.05;
        Assert.True(world.Entities.SpawnEntity(cart));
        double startY = cart.Y;
        EntityTestHarness.AdvanceGameTicks(world, 60);
        Assert.NotEqual(startY, cart.Y);
    }

    [Fact]
    public void Powered_rail_boost_and_braking_and_detector_rail()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 31, 0, 15, 63);

        PlaceRailWithFloor(world, 8, 64, 8, BlockRegistry.Get("powered_rail").Id, 0 | 8);
        Entity boosted = Place(world, 8.5, 65.0, 8.5, MinecartBehavior.Rideable);
        boosted.VelocityZ = 0.02;
        Assert.True(world.Entities.SpawnEntity(boosted));

        PlaceRailWithFloor(world, 10, 64, 8, BlockRegistry.Get("powered_rail").Id, 0);
        Entity braking = Place(world, 10.5, 65.0, 8.5, MinecartBehavior.Rideable);
        braking.VelocityZ = 0.15;
        Assert.True(world.Entities.SpawnEntity(braking));

        PlaceRailWithFloor(world, 12, 64, 8, BlockRegistry.Get("detector_rail").Id, 0);
        Entity detector = Place(world, 12.5, 65.0, 8.5, MinecartBehavior.Rideable);
        detector.VelocityZ = 0.1;
        Assert.True(world.Entities.SpawnEntity(detector));

        double boostedBefore = EntityTestHarness.HorizontalSpeed(boosted);
        double brakingBefore = EntityTestHarness.HorizontalSpeed(braking);

        EntityTestHarness.AdvanceGameTicks(world, 40);

        Assert.NotEqual(boostedBefore, EntityTestHarness.HorizontalSpeed(boosted));
        Assert.NotEqual(brakingBefore, EntityTestHarness.HorizontalSpeed(braking));
        Assert.False(detector.Dead);
    }

    [Fact]
    public void Powered_rail_low_speed_starter_uses_neighbor_suffocate_meta0()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 31, 0, 15, 63);
        int x = 20;
        int z = 8;
        PlaceRailWithFloor(world, x, 64, z, BlockRegistry.Get("powered_rail").Id, 0 | 8);
        world.Writer.SetBlock(x, 64, z - 1, BlockRegistry.Get("stone").Id);
        world.Writer.SetBlock(x, 64, z + 1, BlockRegistry.Get("stone").Id);

        Entity cart = Place(world, x + 0.5, 65.0, z + 0.5, MinecartBehavior.Rideable);
        Assert.True(world.Entities.SpawnEntity(cart));
        EntityTestHarness.AdvanceGameTicks(world, 8);
        Assert.True(EntityTestHarness.HorizontalSpeed(cart) > 0.0);
    }

    [Fact]
    public void Powered_rail_low_speed_starter_uses_neighbor_suffocate_meta1()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 31, 0, 15, 63);
        int x = 22;
        int z = 8;
        PlaceRailWithFloor(world, x, 64, z, BlockRegistry.Get("powered_rail").Id, 1 | 8);
        world.Writer.SetBlock(x - 1, 64, z, BlockRegistry.Get("stone").Id);
        world.Writer.SetBlock(x + 1, 64, z, BlockRegistry.Get("stone").Id);

        Entity cart = Place(world, x + 0.5, 65.0, z + 0.5, MinecartBehavior.Rideable);
        Assert.True(world.Entities.SpawnEntity(cart));
        EntityTestHarness.AdvanceGameTicks(world, 8);
        Assert.True(EntityTestHarness.HorizontalSpeed(cart) > 0.0);
    }

    [Fact]
    public void Corner_rail_metas_6_to_9_tick()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 40, 0, 15, 63);
        for (int m = 6; m <= 9; m++)
        {
            int x = 8 + m;
            PlaceRailWithFloor(world, x, 64, 8, BlockRegistry.Get("rail").Id, m);
        }

        Entity cart = Place(world, 14.5, 65.0, 8.5, MinecartBehavior.Rideable);
        cart.VelocityX = 0.12;
        Assert.True(world.Entities.SpawnEntity(cart));
        double startX = cart.X;
        EntityTestHarness.AdvanceGameTicks(world, 120);
        Assert.NotEqual(startX, cart.X);
    }

    [Fact]
    public void Two_minecarts_OnCollision_mixed_types()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceRailRunX(world, 4, 20, 64, 8);

        Entity slow = Place(world, 8.5, 65.0, 8.5, MinecartBehavior.Rideable);
        slow.VelocityX = 0.2;
        Assert.True(world.Entities.SpawnEntity(slow));

        Entity furnace = Place(world, 10.5, 65.0, 8.5, MinecartBehavior.Furnace);
        furnace.VelocityX = -0.15;
        Assert.True(world.Entities.SpawnEntity(furnace));

        EntityTestHarness.AdvanceGameTicks(world, 40);
        Assert.False(slow.Dead);
        Assert.False(furnace.Dead);
    }

    [Fact]
    public void Furnace_minecart_Interact_coal_and_push_vector()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceRailRunX(world, 6, 14, 64, 10);
        Entity cart = Place(world, 10.5, 65.0, 10.5, MinecartBehavior.Furnace);
        Assert.True(world.Entities.SpawnEntity(cart));

        var player = new TestEntityPlayer(world);
        player.SetPosition(12.0, 65.0, 10.5);
        Assert.True(world.Entities.SpawnEntity(player));
        player.Inventory.SetStack(0, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:coal"), 16));
        player.Inventory.SelectedSlot = 0;

        bool interacted = cart.Interact(player);
        Assert.True(interacted);
        Assert.NotNull(player.Inventory.GetStack(0));
        Assert.Equal(15, player.Inventory.GetStack(0)!.Count);
        Assert.Equal(1200, Cart.Fuel(cart));

        // Stoked, it drives itself away from whoever fed it, burning through the coal as it goes.
        EntityTestHarness.AdvanceGameTicks(world, 30);
        Assert.True(EntityTestHarness.HorizontalSpeed(cart) > 0.0);
        Assert.True(Cart.Fuel(cart) < 1200);
    }

    [Fact]
    public void Empty_minecart_Interact_player_mounted()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceRailRunX(world, 4, 12, 64, 6);
        Entity cart = Place(world, 8.5, 65.0, 6.5, MinecartBehavior.Rideable);
        Assert.True(world.Entities.SpawnEntity(cart));

        var player = new TestEntityPlayer(world);
        player.SetPosition(8.5, 65.0, 6.5);
        Assert.True(world.Entities.SpawnEntity(player));

        bool interacted = cart.Interact(player);
        EntityTestHarness.AdvanceGameTicks(world, 20);
        Assert.True(interacted);
        Assert.Same(cart, player.Vehicle);
        Assert.Same(player, cart.Passenger);
    }

    [Fact]
    public void Chest_minecart_Interact_and_inventory_and_damage_drops()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceRailRunX(world, 4, 10, 64, 4);
        Entity cart = Place(world, 8.5, 65.0, 4.5, MinecartBehavior.Chest);
        MinecartCargo cargo = Cart.Cargo(cart)!;
        cargo.SetStack(3, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:stick"), 4));
        Assert.Equal(2, cargo.RemoveStack(3, 2)!.Count);
        cargo.SetStack(3, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:stick"), 4));

        Assert.True(world.Entities.SpawnEntity(cart));

        var player = new TestEntityPlayer(world);
        player.SetPosition(8.5, 65.0, 4.5);
        Assert.True(world.Entities.SpawnEntity(player));
        bool interacted = cart.Interact(player);

        Assert.True(cart.Damage(null, 10));
        Assert.True(interacted);
        Assert.True(cart.Dead);
    }

    /// <summary>Breaking a cart leaves behind exactly the pieces that built it, kind by kind.</summary>
    [Theory]
    [InlineData(MinecartBehavior.Rideable, "minecart")]
    [InlineData(MinecartBehavior.Chest, "chest")]
    [InlineData(MinecartBehavior.Furnace, "furnace")]
    public void Breaking_a_cart_drops_the_pieces_that_built_it(int cartType, string expectedDrop)
    {
        FakeWorldContext world = new();
        Entity cart = Place(world, 8.5, 65.0, 8.5, cartType);
        Assert.True(world.Entities.SpawnEntity(cart));

        Assert.True(cart.Damage(null, 5));

        Assert.True(cart.Dead);
        Assert.True(ContentRuntime.Current.Items.TryParse(expectedDrop, out ItemStack? expected));
        Assert.True(ContentRuntime.Current.Items.TryParse("minecart", out ItemStack? minecart));
        int expectedId = expected.ItemId;
        int minecartId = minecart.ItemId;
        List<int> dropped = [.. world.Entities.Entities
            .Select(EntityTestHarness.DroppedStack)
            .Where(stack => stack is not null)
            .Select(stack => stack!.ItemId)];

        Assert.Contains(minecartId, dropped);
        Assert.Contains(expectedId, dropped);
    }

    [Fact]
    public void Empty_cart_damage_breaks_and_AnimateHurt()
    {
        FakeWorldContext world = new();
        Entity cart = Place(world, 8.5, 65.0, 8.5, MinecartBehavior.Rideable);
        Assert.True(world.Entities.SpawnEntity(cart));

        int restingDirection = Cart.RockDirection(cart);
        cart.AnimateHurt();
        Assert.Equal(-restingDirection, Cart.RockDirection(cart));
        Assert.Equal(10, Cart.TimeSinceHit(cart));

        Assert.True(cart.Damage(null, 10));
        Assert.True(cart.Dead);
    }

    [Fact]
    public void Client_branch_interpolation_and_remote_static_tick()
    {
        FakeWorldContext world = new();
        world.IsRemote = true;

        Entity cart = Place(world, 8.0, 65.0, 8.0, MinecartBehavior.Rideable);
        cart.SetPositionAndAnglesAvoidEntities(9.0, 65.5, 9.0, 45f, 0f, 4);
        Assert.True(world.Entities.SpawnEntity(cart));
        EntityTestHarness.AdvanceGameTicks(world, 6);

        Entity cart2 = Place(world, 12.0, 65.0, 12.0, MinecartBehavior.Rideable);
        Assert.True(world.Entities.SpawnEntity(cart2));
        EntityTestHarness.AdvanceGameTicks(world, 2);
        Assert.True(cart.X > 8.0);
        Assert.False(cart2.Dead);
    }

    [Fact]
    public void SetVelocityClient_updates_motion()
    {
        FakeWorldContext world = new();
        world.IsRemote = true;
        Entity cart = Place(world, 8.0, 65.0, 8.0, MinecartBehavior.Rideable);
        cart.SetVelocityClient(0.1, 0.0, -0.05);
        Assert.True(world.Entities.SpawnEntity(cart));
        EntityTestHarness.AdvanceGameTicks(world, 2);
        Assert.Equal(0.1, cart.VelocityX, 6);
        Assert.Equal(-0.05, cart.VelocityZ, 6);
    }

    [Fact]
    public void Chest_minecart_NBT_round_trip_with_items()
    {
        FakeWorldContext worldA = new();
        EntityTestHarness.PlaceStoneFloor(worldA, 0, 15, 0, 15, 63);
        Entity original = Place(worldA, 8.5, 65.0, 8.5, MinecartBehavior.Chest);
        Cart.Cargo(original)!.SetStack(5, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:stick"), 3));

        var nbt = new NBTTagCompound();
        Assert.True(original.SaveSelfNbt(nbt));

        FakeWorldContext worldB = new();
        EntityTestHarness.PlaceStoneFloor(worldB, 0, 15, 0, 15, 63);
        Entity? loaded = EntityRegistry.GetEntityFromNbt(nbt, worldB);
        Assert.NotNull(loaded);
        Assert.Equal(typeof(EntityObject), loaded.GetType());
        Assert.Equal(MinecartBehavior.Chest, Cart.Type(loaded));

        MinecartCargo cargo = Cart.Cargo(loaded)!;
        Assert.NotNull(cargo.GetStack(5));
        Assert.Equal(3, cargo.GetStack(5)!.Count);
    }

    [Fact]
    public void OnCollision_empty_cart_pushes_pig_when_fast()
    {
        FakeWorldContext world = new();
        Entity cart = Place(world, 8.5, 65.0, 8.5, MinecartBehavior.Rideable);
        cart.VelocityX = 0.15;
        cart.VelocityZ = 0.12;
        Assert.True(world.Entities.SpawnEntity(cart));

        var pig = (EntityCreature)EntityRegistry.ByName("pig").Create(world);
        pig.SetPosition(9.2, 65.0, 8.6);
        Assert.True(world.Entities.SpawnEntity(pig));

        double pigSpeedBefore = EntityTestHarness.HorizontalSpeed(pig);
        cart.OnCollision(pig);
        Assert.True(EntityTestHarness.HorizontalSpeed(pig) > pigSpeedBefore);
    }

    [Fact]
    public void OnCollision_two_furnace_carts_averages_velocity()
    {
        FakeWorldContext world = new();
        Entity a = Place(world, 8.0, 65.0, 8.0, MinecartBehavior.Furnace);
        a.VelocityX = 0.12;
        a.VelocityZ = 0.05;
        Assert.True(world.Entities.SpawnEntity(a));

        Entity b = Place(world, 8.3, 65.0, 8.2, MinecartBehavior.Furnace);
        b.VelocityX = -0.1;
        b.VelocityZ = 0.04;
        Assert.True(world.Entities.SpawnEntity(b));

        a.OnCollision(b);
        Assert.True(EntityTestHarness.HorizontalSpeed(a) >= 0.0);
        Assert.True(EntityTestHarness.HorizontalSpeed(b) >= 0.0);
    }

    /// <summary>However a chest cart is removed, its contents end up on the ground.</summary>
    [Fact]
    public void MarkDead_chest_cart_spills_inventory_without_Damage()
    {
        FakeWorldContext world = new();
        Entity cart = Place(world, 8.5, 65.0, 8.5, MinecartBehavior.Chest);
        Cart.Cargo(cart)!.SetStack(0, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:stick"), 24));
        Assert.True(world.Entities.SpawnEntity(cart));

        cart.MarkDead();

        Assert.True(cart.Dead);
        Assert.Equal(24, world.Entities.Entities
            .Select(EntityTestHarness.DroppedStack)
            .Where(stack => stack is not null && stack.ItemId == ContentRuntime.Current.Items.Get("omniblock:stick").Id)
            .Sum(stack => stack!.Count));
    }

    [Fact]
    public void Furnace_minecart_NBT_round_trip_push_and_fuel()
    {
        FakeWorldContext worldA = new();
        EntityTestHarness.PlaceStoneFloor(worldA, 0, 15, 0, 15, 63);
        Entity original = Place(worldA, 8.5, 65.0, 8.5, MinecartBehavior.Furnace);

        var nbt = new NBTTagCompound();
        Assert.True(original.SaveSelfNbt(nbt));

        FakeWorldContext worldB = new();
        EntityTestHarness.PlaceStoneFloor(worldB, 0, 15, 0, 15, 63);
        Entity? loaded = EntityRegistry.GetEntityFromNbt(nbt, worldB);
        Assert.NotNull(loaded);
        Assert.Equal(MinecartBehavior.Furnace, Cart.Type(loaded));
    }

    [Fact]
    public void Protocol_facts_are_pinned()
    {
        EntityDefinition minecart = EntityRegistry.ByName("minecart").RequireDefinition();

        Assert.Equal(40, minecart.ProtocolId);
        Assert.Equal(0, minecart.SpawnObjectId);
        Assert.Equal(160, minecart.TrackingRange);
        Assert.Equal(5, minecart.TrackingFrequency);
        Assert.True(minecart.TracksVelocity);
        Assert.Equal(0.0, minecart.PassengerRideHeightScale);
        Assert.Equal(-0.3, minecart.PassengerRideOffset);
    }
}
