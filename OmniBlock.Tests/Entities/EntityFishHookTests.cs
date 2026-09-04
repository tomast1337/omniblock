using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.NBT;

namespace OmniBlock.Tests.Entities;

/// <summary>
///     Covers the fishing bobber, the eleventh non-living entity to lose its class — and the only
///     projectile tied to its thrower for its whole life: the angler owns it, the rod reels it, and it
///     removes itself the moment the angler stops holding a rod or walks too far off.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityFishHookTests
{
    private static FishingBobberBehavior Bobber => TestEntityCatalog.ByName("fishhook").Behaviors.Find<FishingBobberBehavior>()!;

    private static TestEntityPlayer Angler(FakeWorldContext world)
    {
        TestEntityPlayer player = new(world)
        {
            Name = "angler"
        };
        player.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);
        player.Inventory.SetStack(player.Inventory.SelectedSlot, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:fishing_rod"), 1));
        Assert.True(world.Entities.SpawnEntity(player));
        return player;
    }

    private static Entity Cast(FakeWorldContext world, TestEntityPlayer angler)
    {
        var bobber = FishingBobberBehavior.Cast(world, angler);
        Assert.True(world.Entities.SpawnEntity(bobber));
        return bobber;
    }

    [Fact]
    public void A_bobber_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();
        var bobber = TestEntityCatalog.ByName("fishhook").Create(world);

        Assert.Equal(typeof(EntityObject), bobber.GetType());
        Assert.True(bobber.IgnoreFrustumCheck, "The line has to be drawn even when the float is off-screen.");
    }

    [Fact]
    public void Casting_hangs_the_bobber_off_the_angler()
    {
        FakeWorldContext world = new();
        var angler = Angler(world);

        var bobber = Cast(world, angler);

        Assert.Same(angler, Bobber.Angler(bobber));
        Assert.Same(bobber, angler.FishHook);
    }

    /// <summary>The leash: an angler who wanders more than 32 blocks off loses the line.</summary>
    [Fact]
    public void A_bobber_removes_itself_when_the_angler_walks_away()
    {
        FakeWorldContext world = new();
        var angler = Angler(world);
        var bobber = Cast(world, angler);

        angler.SetPositionAndAngles(200.5, 65.0, 8.5, 0f, 0f);
        bobber.Tick();

        Assert.True(bobber.Dead);
        Assert.Null(angler.FishHook);
    }

    [Fact]
    public void A_bobber_removes_itself_when_the_rod_is_put_away()
    {
        FakeWorldContext world = new();
        var angler = Angler(world);
        var bobber = Cast(world, angler);

        angler.Inventory.SetStack(angler.Inventory.SelectedSlot, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:stick"), 1));
        bobber.Tick();

        Assert.True(bobber.Dead);
        Assert.Null(angler.FishHook);
    }

    /// <summary>Reeling in nothing costs the rod nothing and still clears the line.</summary>
    [Fact]
    public void Reeling_an_empty_line_wears_the_rod_none()
    {
        FakeWorldContext world = new();
        var angler = Angler(world);
        var bobber = Cast(world, angler);

        Assert.Equal(0, Bobber.Reel(bobber));

        Assert.True(bobber.Dead);
        Assert.Null(angler.FishHook);
    }

    /// <summary>
    ///     A bobber that strikes a mob hooks it, rides it, and drags it back on the reel — the wear of
    ///     three is what tells the rod that is what happened.
    /// </summary>
    [Fact]
    public void Reeling_a_hooked_mob_drags_it_towards_the_angler()
    {
        FakeWorldContext world = new();
        var angler = Angler(world);
        var pig = (EntityLiving)TestEntityCatalog.ByName("pig").Create(world);
        pig.SetPositionAndAngles(8.5, 65.0, 14.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(pig));

        var bobber = Cast(world, angler);
        bobber.SetPositionAndAngles(8.5, 65.5, 11.5, 0f, 0f);
        bobber.VelocityX = bobber.VelocityY = 0.0;
        bobber.VelocityZ = 0.5;

        for (var tick = 0; tick < 30 && Bobber.Hooked(bobber) is null; tick++) bobber.Tick();

        Assert.Same(pig, Bobber.Hooked(bobber));

        // Riding it: the bobber sits on whatever it hooked rather than flying on.
        pig.SetPositionAndAngles(8.5, 65.0, 20.5, 0f, 0f);
        bobber.Tick();
        Assert.Equal(pig.X, bobber.X, 5);

        Assert.Equal(3, Bobber.Reel(bobber));
        Assert.True(pig.VelocityZ < 0.0, "The pull is back towards the angler, who stands at lower Z.");
        Assert.True(bobber.Dead);
        Assert.Null(angler.FishHook);
    }

    [Fact]
    public void Flight_state_survives_an_nbt_round_trip()
    {
        FakeWorldContext world = new();
        var bobber = TestEntityCatalog.ByName("fishhook").Create(world);
        bobber.SetPositionAndAngles(8.5, 65.0, 8.5, 0f, 0f);

        NBTTagCompound nbt = new();
        bobber.Write(nbt);
        nbt.SetShort("xTile", 4);
        nbt.SetByte("inTile", 2);
        nbt.SetByte("shake", 3);
        nbt.SetByte("inGround", 1);

        var restored = TestEntityCatalog.ByName("fishhook").Create(world);
        restored.Read(nbt);
        NBTTagCompound written = new();
        restored.Write(written);

        Assert.Equal(4, written.GetShort("xTile"));
        Assert.Equal(2, written.GetByte("inTile"));
        Assert.Equal(3, written.GetByte("shake"));
        Assert.Equal(1, written.GetByte("inGround"));
    }

    [Fact]
    public void Protocol_facts_are_pinned()
    {
        var bobber = TestEntityCatalog.ByName("fishhook").RequireDefinition();

        Assert.Equal(64, bobber.ProtocolId);
        Assert.Equal(90, bobber.SpawnObjectId);
        Assert.Equal(64, bobber.TrackingRange);
        Assert.Equal(5, bobber.TrackingFrequency);
        Assert.True(bobber.TracksVelocity);
        Assert.True(bobber.IgnoreFrustumCheck);
    }
}
