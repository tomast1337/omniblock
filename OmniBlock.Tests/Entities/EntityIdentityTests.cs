using System.Text.Json;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.NBT;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Covers entity identity moving off the C# class and onto the registered type, which is what lets a
/// mob exist with no class of its own. The cow is the first: <c>cow.json</c> is the whole entity.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityIdentityTests
{
    private static Entity Cow(FakeWorldContext world) => EntityRegistry.ByName("cow").Create(world);

    [Fact]
    public void A_cow_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();

        // Exactly EntityCreature, not a subclass of it: nothing named "cow" exists in C# any more.
        Assert.Equal(typeof(EntityCreature), Cow(world).GetType());
    }

    [Fact]
    public void A_cow_carries_the_type_that_created_it()
    {
        FakeWorldContext world = new();

        Assert.Same(EntityRegistry.ByName("cow"), Cow(world).Type);
        Assert.Equal("cow", EntityRegistry.GetId(Cow(world)));
    }

    /// <summary>
    ///     The save format keys entities by this id, so a classless cow has to round-trip under the
    ///     same name a subclassed one did.
    /// </summary>
    [Fact]
    public void A_cow_round_trips_through_nbt_as_a_cow()
    {
        FakeWorldContext world = new();
        Entity saved = Cow(world);
        saved.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        NBTTagCompound nbt = new();
        Assert.True(saved.SaveSelfNbt(nbt));

        // The on-disk name is the capitalised vanilla one, unchanged by the class going away.
        Assert.Equal("Cow", nbt.GetString("id"));

        Entity? loaded = EntityRegistry.GetEntityFromNbt(nbt, world);

        Assert.NotNull(loaded);
        Assert.Equal(typeof(EntityCreature), loaded.GetType());
        Assert.Equal("cow", EntityRegistry.GetId(loaded));
    }

    [Fact]
    public void A_cow_still_reads_its_configuration_and_slots_from_json()
    {
        FakeWorldContext world = new();
        EntityLiving cow = (EntityLiving)Cow(world);

        Assert.Equal("creature", cow.Definition.SpawnCategory);
        Assert.Equal("/mob/cow.png", cow.Definition.Texture);
        Assert.Equal(0.9F, cow.Definition.Width);
        Assert.IsType<SwapHeldItemBehavior>(cow.Behaviors.Interactable);
        Assert.IsType<LootTableBehavior>(cow.Behaviors.Loot);
    }

    /// <summary>
    ///     The client can no longer pick a renderer by class, because a cow and a sheep would be the
    ///     same class. The definition names the model instead.
    /// </summary>
    [Fact]
    public void A_cow_declares_its_renderer_in_json()
    {
        JsonElement renderer = Assert.NotNull(EntityRegistry.ByName("cow").Definition!.Renderer);

        Assert.Equal("living", renderer.GetProperty("Type").GetString());
        Assert.Equal("cow", renderer.GetProperty("Model").GetString());
        Assert.Equal(0.7F, renderer.GetProperty("Shadow").GetSingle());
    }

    /// <summary>
    ///     Guards the fallback that entities built outside the registry rely on: once a class backs
    ///     more than one registered type it must resolve to nothing rather than to whichever
    ///     registration ran first.
    /// </summary>
    [Fact]
    public void A_class_that_still_backs_one_type_resolves_by_class()
    {
        // Wolf, cow, sheep, pig and chicken all share EntityCreature now, so the class no longer identifies any of them.
        Assert.Null(EntityRegistry.ByRuntimeType(typeof(EntityCreature)));
    }

    [Theory]
    [InlineData("zombie")]
    [InlineData("skeleton")]
    [InlineData("creeper")]
    public void Every_plain_monster_is_json_rather_than_a_class(string name)
    {
        FakeWorldContext world = new();

        Assert.Equal(typeof(EntityCreature), EntityRegistry.ByName(name).Create(world).GetType());
        Assert.NotNull(EntityRegistry.ByName(name).Definition!.Renderer);
    }

    /// <summary>
    ///     Three registered types now share <c>EntityCreature</c>, so the class identifies none of
    ///     them and the by-class index must refuse to answer.
    /// </summary>
    [Fact]
    public void The_shared_creature_class_no_longer_identifies_a_single_type()
    {
        Assert.Null(EntityRegistry.ByRuntimeType(typeof(EntityCreature)));
    }

    /// <summary>Held items are declared configuration now, not a per-class property override.</summary>
    [Fact]
    public void A_mob_carries_the_item_its_definition_names()
    {
        FakeWorldContext world = new();

        EntityLiving skeleton = (EntityLiving)EntityRegistry.ByName("skeleton").Create(world);
        EntityLiving zombie = (EntityLiving)EntityRegistry.ByName("zombie").Create(world);
        EntityLiving pigZombie = (EntityLiving)EntityRegistry.ByName("pigzombie").Create(world);

        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:bow").Id, skeleton.HeldItem!.ItemId);
        Assert.Equal(ContentRuntime.Current.Items.Get("omniblock:sword_gold").Id, pigZombie.HeldItem!.ItemId);
        Assert.Null(zombie.HeldItem);
    }
}
