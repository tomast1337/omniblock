using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;

namespace OmniBlock.Tests.Entities;

/// <summary>
///     Covers the wolf, the last mob to lose its class. Everything it was — taming, sitting, anger,
///     following an owner, shaking off water, cocking its head at a bone — is declared, and the three
///     bits it all reads sit in one synced byte the way the protocol requires.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityWolfTests
{
    private static TameableBehavior Tame => TestEntityCatalog.ByName("wolf").Behaviors.Find<TameableBehavior>()!;

    private static EntityCreature Wolf(FakeWorldContext world, double x = 8.5, double z = 8.5)
    {
        var wolf = (EntityCreature)TestEntityCatalog.ByName("wolf").Create(world);
        wolf.SetPositionAndAngles(x, 65.0, z, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(wolf));
        return wolf;
    }

    private static TestEntityPlayer Player(FakeWorldContext world, Item? holding = null, string name = "tester", double x = 9.0)
    {
        TestEntityPlayer player = new(world)
        {
            Name = name
        };
        player.SetPositionAndAngles(x, 65.0, 8.5, 0f, 0f);
        if (holding is not null) player.Inventory.SetStack(player.Inventory.SelectedSlot, new ItemStack(holding));

        Assert.True(world.Entities.SpawnEntity(player));
        return player;
    }

    /// <summary>Offers bones until one is accepted; taming is a one-in-three roll.</summary>
    private static EntityCreature TamedWolf(FakeWorldContext world, TestEntityPlayer owner)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var wolf = Wolf(world);
            owner.Inventory.SetStack(owner.Inventory.SelectedSlot, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:bone"), 64));

            Assert.True(wolf.Interact(owner));
            if (Tame.IsTamed(wolf)) return wolf;

            wolf.MarkDead();
            world.Entities.Entities.Remove(wolf);
        }

        Assert.Fail("A wolf never accepted a bone in 500 offers.");
        return null!;
    }

    [Fact]
    public void A_wolf_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();

        Assert.Equal(typeof(EntityCreature), Wolf(world).GetType());

        var behaviors = TestEntityCatalog.ByName("wolf").Behaviors;
        Assert.NotNull(behaviors.Find<TameableBehavior>());
        Assert.NotNull(behaviors.Find<FollowOwnerBehavior>());
        Assert.NotNull(behaviors.Find<HeadTiltBehavior>());
        Assert.NotNull(behaviors.Find<ShakeOffWaterBehavior>());
        Assert.IsType<BiteAttackBehavior>(Assert.IsType<JumpAttackBehavior>(behaviors.Attack).Fallback);
    }

    /// <summary>
    ///     One entry fills six slots, which is the point: sitting, anger and taming are three bits of
    ///     one byte, and every hook that reads them is the same object.
    /// </summary>
    [Fact]
    public void One_behavior_fills_every_slot_that_reads_the_flags()
    {
        var behaviors = TestEntityCatalog.ByName("wolf").Behaviors;

        Assert.IsType<CompositeBehavior>(behaviors.Interactable);
        Assert.IsType<CompositeBehavior>(behaviors.Persistence);
        Assert.IsType<CompositeBehavior>(behaviors.Targeting);
        Assert.IsType<CompositeBehavior>(behaviors.Ticker);
        Assert.IsType<CompositeBehavior>(behaviors.Lifecycle);
        Assert.IsType<CompositeBehavior>(behaviors.Physics);

        // The same instance every time, not one per slot.
        Assert.Same(behaviors.Interactable, behaviors.Physics);
        Assert.Single(((CompositeBehavior)behaviors.Ticker!).Children.OfType<TameableBehavior>());
    }

    [Fact]
    public void A_bone_eventually_tames_a_wolf_and_gives_it_an_owner()
    {
        FakeWorldContext world = new();
        var owner = Player(world);
        var wolf = TamedWolf(world, owner);

        Assert.True(Tame.IsTamed(wolf));
        Assert.Equal("tester", Tame.Owner(wolf));
        Assert.True(Tame.IsSitting(wolf));
        Assert.Equal(20, wolf.Health);

        wolf.Tick();
        Assert.Contains("tame", wolf.GetTexture(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Only_a_bone_interests_an_untamed_wolf()
    {
        FakeWorldContext world = new();
        var wolf = Wolf(world);

        Assert.False(wolf.Interact(Player(world)));
        Assert.False(wolf.Interact(Player(world, ContentRuntime.Current.Items.Get("omniblock:stick"), "other", 9.5)));
        Assert.False(Tame.IsTamed(wolf));
    }

    /// <summary>A tamed wolf takes orders from its owner and ignores everybody else.</summary>
    [Fact]
    public void Only_the_owner_can_tell_a_wolf_to_sit()
    {
        FakeWorldContext world = new();
        var owner = Player(world);
        var wolf = TamedWolf(world, owner);
        owner.Inventory.SetStack(owner.Inventory.SelectedSlot, null);

        Assert.True(Tame.IsSitting(wolf));

        Assert.False(wolf.Interact(Player(world, name: "stranger", x: 9.5)));
        Assert.True(Tame.IsSitting(wolf));

        Assert.True(wolf.Interact(owner));
        Assert.False(Tame.IsSitting(wolf));
    }

    [Fact]
    public void Feeding_meat_heals_a_hurt_wolf()
    {
        FakeWorldContext world = new();
        var owner = Player(world);
        var wolf = TamedWolf(world, owner);

        wolf.Health = 5;
        wolf.Tick();

        owner.Inventory.SetStack(owner.Inventory.SelectedSlot, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:porkchop_raw"), 4));
        Assert.True(wolf.Interact(owner));

        Assert.True(wolf.Health > 5);
    }

    [Fact]
    public void A_tamed_wolf_is_never_cleaned_up()
    {
        FakeWorldContext world = new();
        var owner = Player(world);
        var wolf = TamedWolf(world, owner);

        Assert.False(Tame.CanDespawn(wolf));
        Assert.Null(Tame.CanDespawn(Wolf(world, 40.5, 40.5)));
    }

    /// <summary>Sitting stops the mob where it is, which is what the AI reads before it paths.</summary>
    [Fact]
    public void A_sitting_wolf_stays_put_and_watches_less_closely()
    {
        FakeWorldContext world = new();
        var wolf = Wolf(world);

        Assert.Null(wolf.Behaviors.Physics!.IsMovementCeased(wolf));
        Assert.Null(wolf.Behaviors.Physics.MaxFallDistance(wolf));

        Tame.SetSitting(wolf, true);

        Assert.True(wolf.Behaviors.Physics.IsMovementCeased(wolf));
        Assert.Equal(20, wolf.Behaviors.Physics.MaxFallDistance(wolf));
    }

    /// <summary>
    ///     Anything but a player's own hand is halved, which is the difference between a wolf that
    ///     survives a fight with a zombie and one that does not.
    /// </summary>
    [Fact]
    public void A_wolf_shrugs_off_half_of_anything_a_player_did_not_do()
    {
        FakeWorldContext world = new();
        var wolf = Wolf(world);
        var zombie = (EntityCreature)TestEntityCatalog.ByName("zombie").Create(world);
        var player = Player(world);

        Assert.Equal(3, wolf.Behaviors.Lifecycle!.ModifyDamage(wolf, zombie, 5));
        Assert.Equal(5, wolf.Behaviors.Lifecycle.ModifyDamage(wolf, player, 5));
        Assert.Equal(5, wolf.Behaviors.Lifecycle.ModifyDamage(wolf, null, 5));
    }

    [Fact]
    public void Hitting_a_wolf_angers_it_and_every_wolf_watching()
    {
        FakeWorldContext world = new();
        var wolf = Wolf(world);
        var packMate = Wolf(world, 10.5);
        var player = Player(world);

        Assert.True(wolf.Damage(player, 1));

        Assert.True(Tame.IsAngry(wolf));
        Assert.Same(player, wolf.Target);
        Assert.True(Tame.IsAngry(packMate));
        Assert.Same(player, packMate.Target);
    }

    /// <summary>An angry wolf hunts; a calm or tamed one picks nothing of its own.</summary>
    [Fact]
    public void Only_an_angry_wolf_goes_looking_for_a_player()
    {
        FakeWorldContext world = new();
        var wolf = Wolf(world);
        var player = Player(world);

        Assert.Null(wolf.Targeting!.FindPlayerToAttack(wolf));

        Assert.True(wolf.Damage(player, 1));
        Assert.Same(player, wolf.Targeting.FindPlayerToAttack(wolf));
    }

    [Fact]
    public void A_wolf_bites_harder_once_it_belongs_to_someone()
    {
        FakeWorldContext world = new();
        var owner = Player(world);
        var wild = Wolf(world, 40.5, 40.5);
        var pet = TamedWolf(world, owner);

        var bite = (BiteAttackBehavior)((JumpAttackBehavior)wild.Behaviors.Attack!).Fallback!;

        var wildTarget = Player(world, name: "prey", x: 40.9);
        wildTarget.SetPositionAndAngles(40.9, 65.0, 40.5, 0f, 0f);
        var beforeWild = wildTarget.Health;
        bite.AttackEntity(wild, wildTarget, 0.5F);
        var wildBite = beforeWild - wildTarget.Health;

        var petTarget = Player(world, name: "quarry", x: 8.9);
        var beforePet = petTarget.Health;
        bite.AttackEntity(pet, petTarget, 0.5F);
        var petBite = beforePet - petTarget.Health;

        Assert.True(wildBite > 0);
        Assert.True(petBite > wildBite);
    }

    /// <summary>
    ///     Two separate facts: the mob needs a shake, and it is shaking now. Getting wet sets the
    ///     first; standing on dry ground starts the second.
    /// </summary>
    [Fact]
    public void A_soaked_wolf_shakes_itself_dry_on_land()
    {
        FakeWorldContext world = new();
        var wolf = Wolf(world);
        var shake = wolf.Behaviors.Find<ShakeOffWaterBehavior>()!;

        Assert.False(shake.IsShaking(wolf));

        world.ReaderWriter.SetBlock(8, 65, 8, TestBlocks.Get("water").Id, 0);
        wolf.Tick();

        Assert.True(shake.IsShaking(wolf), "A wolf standing in water needs a shake.");

        // Out of the water and on the ground, the shake itself begins and stops the mob.
        world.ReaderWriter.SetBlock(8, 65, 8, 0, 0);
        wolf.OnGround = true;
        wolf.Behaviors.Physics!.AfterTickMovement(wolf);

        Assert.True(wolf.Behaviors.Physics.IsMovementCeased(wolf));
    }

    [Fact]
    public void A_wolf_says_one_of_the_four_things_a_wolf_says()
    {
        FakeWorldContext world = new();
        var wolf = Wolf(world);
        HashSet<string> heard = [];

        for (var attempt = 0; attempt < 200; attempt++) heard.Add(wolf.Behaviors.Ticker!.LivingSound(wolf)!);

        Assert.Subset(new HashSet<string>
        {
            "mob.wolf.bark",
            "mob.wolf.panting",
            "mob.wolf.whine",
            "mob.wolf.growl"
        }, heard);
        Assert.Contains("mob.wolf.bark", heard);

        Tame.SetSitting(wolf, false);
        Assert.True(wolf.Damage(Player(world), 1));

        Assert.Equal("mob.wolf.growl", wolf.Behaviors.Ticker!.LivingSound(wolf));
    }
}
