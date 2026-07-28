using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers the Interactable capability slot: right-click and walk-into behavior declared in
/// <c>assets/entity/*.json</c> instead of overridden on a subclass.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityInteractableTests
{
    private static TestEntityPlayer Player(FakeWorldContext world, Item? holding = null)
    {
        TestEntityPlayer player = new(world) { Name = "tester" };
        player.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        if (holding is not null)
        {
            player.Inventory.SetStack(player.Inventory.SelectedSlot, new ItemStack(holding));
        }

        return player;
    }

    [Fact]
    public void Milking_a_cow_swaps_the_bucket_for_milk()
    {
        FakeWorldContext world = new();
        EntityCow cow = new(world);
        TestEntityPlayer player = Player(world, Item.ByName("bucket"));

        Assert.True(cow.Interact(player));
        Assert.Equal(Item.ByName("milk").Id, player.Inventory.ItemInHand!.ItemId);
    }

    [Fact]
    public void Milking_needs_the_right_item_in_hand()
    {
        FakeWorldContext world = new();
        EntityCow cow = new(world);

        Assert.False(cow.Interact(Player(world)));
        Assert.False(cow.Interact(Player(world, Item.ByName("stick"))));
    }

    [Fact]
    public void An_unsaddled_pig_cannot_be_ridden()
    {
        FakeWorldContext world = new();
        EntityPig pig = new(world);
        TestEntityPlayer player = Player(world);

        Assert.False(pig.Interact(player));
        Assert.Null(player.Vehicle);
    }

    [Fact]
    public void A_saddled_pig_carries_the_player()
    {
        FakeWorldContext world = new();
        EntityPig pig = new(world) { Saddled = { Value = true } };
        pig.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        TestEntityPlayer player = Player(world);

        Assert.True(pig.Interact(player));
        Assert.Same(pig, player.Vehicle);
    }

    [Fact]
    public void A_saddled_pig_refuses_a_second_rider()
    {
        FakeWorldContext world = new();
        EntityPig pig = new(world) { Saddled = { Value = true } };
        pig.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        Assert.True(pig.Interact(Player(world)));
        Assert.False(pig.Interact(Player(world)));
    }

    [Fact]
    public void Only_a_large_slime_hurts_the_player_it_touches()
    {
        FakeWorldContext world = new();
        EntitySlime slime = new(world);
        slime.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        TestEntityPlayer player = Player(world);
        int before = player.Health;

        slime.SlimeSize = 1;
        slime.OnPlayerInteraction(player);
        Assert.Equal(before, player.Health);

        slime.SlimeSize = 4;
        slime.OnPlayerInteraction(player);
        Assert.True(player.Health < before);
    }

    [Fact]
    public void Slots_come_from_json_and_stay_null_where_undeclared()
    {
        Assert.IsType<SwapHeldItemBehavior>(EntityRegistry.ByName("cow").Behaviors.Interactable);
        Assert.IsType<RideIfSaddledBehavior>(EntityRegistry.ByName("pig").Behaviors.Interactable);
        Assert.IsType<ContactDamageBehavior>(EntityRegistry.ByName("slime").Behaviors.Interactable);

        Assert.Null(EntityRegistry.ByName("zombie").Behaviors.Interactable);
    }
}
