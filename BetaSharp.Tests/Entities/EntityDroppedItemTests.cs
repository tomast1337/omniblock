using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Tests.TestSupport;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers the dropped item, the fourth non-living entity to lose its class — and the most widely
/// constructed one: every drop in the game now goes through DroppedItemBehavior.Create. The stack,
/// the pickup delay, and the five hit points are one behavior across five slots.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityDroppedItemTests
{
    private static DroppedItemBehavior Dropped => EntityRegistry.ByName("item").Behaviors.Find<DroppedItemBehavior>()!;

    private static Entity Drop(FakeWorldContext world, ItemStack stack, int pickupDelay = 0, double y = 65.0)
    {
        Entity item = DroppedItemBehavior.Create(world, 8.5, y, 8.5, stack, pickupDelay);
        Assert.True(world.Entities.SpawnEntity(item));
        return item;
    }

    private static TestEntityPlayer Player(FakeWorldContext world)
    {
        TestEntityPlayer player = new(world) { Name = "collector" };
        player.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));
        return player;
    }

    [Fact]
    public void A_dropped_item_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();
        Entity item = Drop(world, new ItemStack(Item.ByName("stick"), 1));

        Assert.Equal(typeof(EntityObject), item.GetType());
        Assert.Same(EntityRegistry.ByName("item").Behaviors.Ticker, EntityRegistry.ByName("item").Behaviors.Interactable);
        Assert.Equal(Item.ByName("stick").Id, Dropped.Stack(item)!.ItemId);
    }

    /// <summary>Every drop pops upward and scatters a little, with its own bob phase for the renderer.</summary>
    [Fact]
    public void A_drop_is_born_with_a_pop_and_its_own_phase()
    {
        FakeWorldContext world = new();
        Entity item = Drop(world, new ItemStack(Item.ByName("stick"), 1));

        Assert.Equal(0.2, item.VelocityY, 5);
    }

    [Fact]
    public void A_player_walking_over_a_drop_picks_it_up()
    {
        FakeWorldContext world = new();
        Entity item = Drop(world, new ItemStack(Item.ByName("stick"), 3));
        TestEntityPlayer player = Player(world);

        item.OnPlayerInteraction(player);

        Assert.True(item.Dead);
        Assert.Equal(3, CountInInventory(player, Item.ByName("stick").Id));
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

    /// <summary>A fresh player drop waits out its delay so the dropper does not instantly re-collect it.</summary>
    [Fact]
    public void The_pickup_delay_holds_the_drop_out_of_reach()
    {
        FakeWorldContext world = new();
        Entity item = Drop(world, new ItemStack(Item.ByName("stick"), 1), pickupDelay: 2);
        TestEntityPlayer player = Player(world);

        item.OnPlayerInteraction(player);
        Assert.False(item.Dead);

        item.Tick();
        item.Tick();
        item.OnPlayerInteraction(player);
        Assert.True(item.Dead);
    }

    /// <summary>Fire and explosions chip at the five hit points; the hit never counts as landed.</summary>
    [Fact]
    public void Five_points_of_damage_destroy_a_drop()
    {
        FakeWorldContext world = new();
        Entity item = Drop(world, new ItemStack(Item.ByName("stick"), 1));

        Assert.False(item.Damage(null, 4));
        Assert.False(item.Dead);

        Assert.False(item.Damage(null, 1));
        Assert.True(item.Dead);
    }

    [Fact]
    public void An_old_enough_drop_cleans_itself_up()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);
        Entity item = Drop(world, new ItemStack(Item.ByName("stick"), 1), y: 64.5);

        for (int tick = 0; tick < 6001 && !item.Dead; tick++) item.Tick();

        Assert.True(item.Dead);
    }

    [Fact]
    public void The_stack_survives_an_nbt_round_trip()
    {
        FakeWorldContext world = new();
        Entity item = Drop(world, new ItemStack(BlockRegistry.Get("wool").id, 5, 11));

        NBTTagCompound nbt = new();
        item.Write(nbt);

        Entity restored = EntityRegistry.ByName("item").Create(world);
        restored.Read(nbt);

        ItemStack stack = Dropped.Stack(restored)!;
        Assert.Equal(BlockRegistry.Get("wool").id, stack.ItemId);
        Assert.Equal(5, stack.Count);
        Assert.Equal(11, stack.getDamage());
    }

    /// <summary>The mine-wood achievement is declared data: picking up a log awards it.</summary>
    [Fact]
    public void Picking_up_a_log_awards_the_achievement()
    {
        FakeWorldContext world = new();
        Entity item = Drop(world, new ItemStack(BlockRegistry.Get("log").id, 1, 0));
        TestEntityPlayer player = Player(world);

        item.OnPlayerInteraction(player);

        Assert.True(item.Dead);
    }

    [Fact]
    public void Protocol_facts_are_pinned()
    {
        EntityType type = EntityRegistry.ByName("item");

        Assert.Equal(1, type.RequireDefinition().ProtocolId);
        Assert.True(type.RequireDefinition().TracksVelocity);
    }
}
