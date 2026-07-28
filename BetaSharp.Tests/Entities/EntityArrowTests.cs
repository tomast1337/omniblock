using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Tests.TestSupport;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers the arrow, the ninth non-living entity to lose its class — and the first projectile that
/// survives arrival: a block hit buries it and starts the shake, and a player-owned arrow stuck in
/// the ground can be pulled back out. Two statics answer what the rest of the game used to ask with
/// `is EntityArrow`: IsArrow for damage scaling, OwnerOf to credit the hit.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityArrowTests
{
    private static ArrowBehavior Flight => EntityRegistry.ByName("arrow").Behaviors.Find<ArrowBehavior>()!;

    private static TestEntityPlayer Player(FakeWorldContext world, double y = 65.0)
    {
        TestEntityPlayer player = new(world) { Name = "archer" };
        player.SetPositionAndAngles(8.5, y, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));
        return player;
    }

    /// <summary>Fires straight down into a floor and ticks until the arrow buries itself.</summary>
    private static Entity ArrowInGround(FakeWorldContext world)
    {
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);
        Entity arrow = EntityRegistry.ByName("arrow").Create(world);
        arrow.SetPositionAndAngles(8.5, 66.0, 8.5, 0f, 0f);
        Flight.SetHeading(arrow, 0.0, -1.0, 0.0, 1.0F, 0.0F);
        Assert.True(world.Entities.SpawnEntity(arrow));

        for (int tick = 0; tick < 40 && Flight.Shake(arrow) == 0; tick++) arrow.Tick();

        return arrow;
    }

    [Fact]
    public void An_arrow_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();
        Entity arrow = EntityRegistry.ByName("arrow").Create(world);

        Assert.Equal(typeof(EntityObject), arrow.GetType());
        Assert.True(ArrowBehavior.IsArrow(arrow));
        Assert.False(ArrowBehavior.IsArrow(EntityRegistry.ByName("snowball").Create(world)));
    }

    [Fact]
    public void A_shot_starts_at_the_archers_eyes_and_is_credited_to_them()
    {
        FakeWorldContext world = new();
        TestEntityPlayer player = Player(world);

        Entity arrow = ArrowBehavior.Shoot(world, player);

        Assert.Same(player, ArrowBehavior.OwnerOf(arrow));
        Assert.True(Flight.BelongsToPlayer(arrow), "A player's own arrow is one they can pick back up.");
        Assert.True(Math.Abs(arrow.Y - (player.Y + player.EyeHeight - 0.1)) < 0.01);
    }

    /// <summary>A mob's arrow is not the player's to reclaim.</summary>
    [Fact]
    public void A_mobs_arrow_cannot_be_picked_up()
    {
        FakeWorldContext world = new();
        EntityLiving skeleton = (EntityLiving)EntityRegistry.ByName("skeleton").Create(world);
        skeleton.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(skeleton));

        Entity arrow = ArrowBehavior.Shoot(world, skeleton);

        Assert.Same(skeleton, ArrowBehavior.OwnerOf(arrow));
        Assert.False(Flight.BelongsToPlayer(arrow));
    }

    [Fact]
    public void An_arrow_arcs_under_gravity_in_flight()
    {
        FakeWorldContext world = new();
        Entity arrow = EntityRegistry.ByName("arrow").Create(world);
        arrow.SetPositionAndAngles(8.5, 80.0, 8.5, 0f, 0f);
        Flight.SetHeading(arrow, 1.0, 0.0, 0.0, 1.0F, 0.0F);
        Assert.True(world.Entities.SpawnEntity(arrow));

        double velocityBefore = arrow.VelocityY;
        arrow.Tick();

        Assert.True(arrow.VelocityY < velocityBefore);
        Assert.False(arrow.Dead);
    }

    /// <summary>A block hit buries the arrow rather than killing it, and starts the impact wobble.</summary>
    [Fact]
    public void A_block_hit_buries_the_arrow_and_starts_the_shake()
    {
        FakeWorldContext world = new();
        Entity arrow = ArrowInGround(world);

        Assert.False(arrow.Dead);
        Assert.Equal(7, Flight.Shake(arrow));
    }

    [Fact]
    public void A_players_buried_arrow_can_be_pulled_back_out()
    {
        FakeWorldContext world = new();
        Entity arrow = ArrowInGround(world);
        Flight.SetBelongsToPlayer(arrow, true);
        TestEntityPlayer player = Player(world, 64.0);

        // The shake has to run down first: an arrow still quivering is not yet collectable.
        arrow.OnPlayerInteraction(player);
        Assert.False(arrow.Dead);

        for (int tick = 0; tick < 8; tick++) arrow.Tick();
        arrow.OnPlayerInteraction(player);

        Assert.True(arrow.Dead);
        Assert.True(CountInInventory(player, Item.ByName("arrow").Id) > 0);
    }

    private static int CountInInventory(TestEntityPlayer player, int itemId)
    {
        int count = 0;
        for (int slot = 0; slot < player.Inventory.Size; slot++)
        {
            if (player.Inventory.GetStack(slot) is { } stack && stack.ItemId == itemId) count += stack.Count;
        }

        return count;
    }

    [Fact]
    public void An_arrow_hitting_a_mob_damages_it_and_is_spent()
    {
        FakeWorldContext world = new();
        EntityLiving pig = (EntityLiving)EntityRegistry.ByName("pig").Create(world);
        pig.SetPositionAndAngles(8.5, 64.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(pig));
        int healthBefore = pig.Health;

        Entity arrow = EntityRegistry.ByName("arrow").Create(world);
        arrow.SetPositionAndAngles(8.5, 64.5, 6.5, 0f, 0f);
        Flight.SetHeading(arrow, 0.0, 0.0, 1.0, 0.5F, 0.0F);
        Assert.True(world.Entities.SpawnEntity(arrow));

        for (int tick = 0; tick < 20 && !arrow.Dead; tick++) arrow.Tick();

        Assert.True(arrow.Dead);
        Assert.True(pig.Health < healthBefore);
    }

    [Fact]
    public void Flight_state_survives_an_nbt_round_trip()
    {
        FakeWorldContext world = new();
        Entity arrow = ArrowInGround(world);
        Flight.SetBelongsToPlayer(arrow, true);

        NBTTagCompound nbt = new();
        arrow.Write(nbt);

        Entity restored = EntityRegistry.ByName("arrow").Create(world);
        restored.Read(nbt);

        Assert.True(Flight.BelongsToPlayer(restored));
        Assert.Equal(Flight.Shake(arrow), Flight.Shake(restored));
        Assert.Equal(BlockRegistry.Get("stone").id, nbt.GetByte("inTile") & 255);
        Assert.Equal(1, nbt.GetByte("inGround"));
    }

    [Fact]
    public void Protocol_facts_are_pinned()
    {
        EntityDefinition arrow = EntityRegistry.ByName("arrow").RequireDefinition();

        Assert.Equal(10, arrow.ProtocolId);
        Assert.Equal(60, arrow.SpawnObjectId);
        Assert.Equal(64, arrow.TrackingRange);
        Assert.Equal(2, arrow.TrackingFrequency);
        Assert.True(arrow.TracksVelocity);
        Assert.True(arrow.AlwaysSyncsRotation);
        Assert.False(arrow.PositionSyncAvoidsEntities);
    }
}
