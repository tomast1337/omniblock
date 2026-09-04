using System.Linq;
using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.NBT;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Covers the slime, the last mob whose size other code had to know it by. Size is now a declared
/// synced property, so contact damage, loot and splitting all read it without a cast — and the
/// behaviors that do are no longer slime-specific at all.
/// </summary>
[Collection("EntityTests")]
public sealed class EntitySlimeTests
{
    private static SizedBodyBehavior Body => TestEntityCatalog.ByName("slime").Behaviors.Find<SizedBodyBehavior>()!;

    private static EntityLiving Slime(FakeWorldContext world, int size, double x = 8.5, double z = 8.5)
    {
        EntityLiving slime = (EntityLiving)TestEntityCatalog.ByName("slime").Create(world);
        Body.SetSize(slime, size);
        slime.SetPositionAndAngles(x, 65.0, z, 0f, 0f);
        return slime;
    }

    [Fact]
    public void A_slime_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();

        Assert.Equal(typeof(EntityLiving), Slime(world, 2).GetType());
        Assert.NotNull(TestEntityCatalog.ByName("slime").Behaviors.Find<HoppingBehavior>());
        Assert.NotNull(TestEntityCatalog.ByName("slime").Behaviors.Find<SplitOnDeathBehavior>());
        Assert.IsType<SlimeChunkSpawnBehavior>(TestEntityCatalog.ByName("slime").Behaviors.Physics);
    }

    /// <summary>Size is the body: it decides the box and the health, and nothing else sets either.</summary>
    [Fact]
    public void Size_decides_the_box_and_the_health()
    {
        FakeWorldContext world = new();
        EntityLiving slime = Slime(world, 4);

        Assert.Equal(16, slime.Health);
        Assert.Equal(2.4F, slime.Width, 5);
        Assert.Equal(2.4F, slime.Height, 5);

        Body.SetSize(slime, 1);

        Assert.Equal(1, slime.Health);
        Assert.Equal(0.6F, slime.Width, 5);
    }

    /// <summary>
    ///     Three separate consumers read the size off the entity, not off a class.
    /// </summary>
    [Fact]
    public void Size_is_readable_as_a_plain_synced_property()
    {
        FakeWorldContext world = new();
        EntityLiving slime = Slime(world, 2);

        Assert.Equal(2, slime.Synced<byte>("size")!.Value);
        Assert.Null(TestEntityCatalog.ByName("zombie").Create(world).Synced<byte>("size"));
    }

    /// <summary>A creation roll always lands on one of the declared sizes.</summary>
    [Fact]
    public void Creation_rolls_one_of_the_declared_sizes()
    {
        FakeWorldContext world = new();
        HashSet<int> seen = [];

        for (int attempt = 0; attempt < 200; attempt++)
        {
            EntityLiving slime = (EntityLiving)TestEntityCatalog.ByName("slime").Create(world);
            int size = Body.Size(slime);

            Assert.Contains(size, new[] { 1, 2, 4 });
            Assert.Equal(size * size, slime.Health);
            seen.Add(size);
        }

        Assert.Equal(3, seen.Count);
    }

    /// <summary>
    ///     The on-disk value counts from zero while the live one counts from one, and restoring it
    ///     has to land after the health the base class reads back — otherwise a loaded slime would
    ///     keep whatever health it was saved with rather than the one its size implies.
    /// </summary>
    [Fact]
    public void Size_round_trips_through_nbt_and_wins_over_the_saved_health()
    {
        FakeWorldContext world = new();
        EntityLiving slime = Slime(world, 4);
        slime.Health = 3;

        NBTTagCompound nbt = new();
        slime.SaveSelfNbt(nbt);

        Assert.Equal(3, nbt.GetInteger("Size"));

        EntityLiving loaded = (EntityLiving)TestEntityCatalog.ByName("slime").Create(world);
        loaded.Read(nbt);

        Assert.Equal(4, Body.Size(loaded));
        Assert.Equal(16, loaded.Health);
    }

    /// <summary>
    ///     Splitting builds children from the dying mob's own registered type, so it never names what
    ///     it is splitting — and each child is sized by the same behavior the parent used.
    /// </summary>
    [Fact]
    public void A_dying_slime_splits_into_four_half_sized_copies()
    {
        FakeWorldContext world = new();
        EntityLiving slime = Slime(world, 4);
        Assert.True(world.Entities.SpawnEntity(slime));
        slime.Health = 0;

        slime.MarkDead();

        List<EntityLiving> children = world.Entities.Entities
            .OfType<EntityLiving>()
            .Where(entity => !ReferenceEquals(entity, slime))
            .ToList();

        Assert.Equal(4, children.Count);
        Assert.All(children, child => Assert.Equal("slime", TestEntityCatalog.GetId(child)));
        Assert.All(children, child => Assert.Equal(2, Body.Size(child)));
        Assert.All(children, child => Assert.Equal(4, child.Health));
    }

    /// <summary>Hopping is the mob's whole AI, and it leaves the ground when the timer runs out.</summary>
    [Fact]
    public void A_grounded_slime_eventually_hops()
    {
        FakeWorldContext world = new();
        EntityLiving slime = Slime(world, 2);
        Assert.True(world.Entities.SpawnEntity(slime));
        slime.OnGround = true;

        for (int tick = 0; tick < 100; tick++)
        {
            Assert.True(slime.Behaviors.Ticker!.OnTickLiving(slime));
            if (!slime.Jumping) continue;

            // A hop is aimed as well as vertical: forward speed scales with size.
            Assert.Equal(2.0F, slime.ForwardSpeed);
            return;
        }

        Assert.Fail("A grounded slime never hopped in 100 ticks.");
    }

    [Fact]
    public void A_slime_in_the_air_never_hops()
    {
        FakeWorldContext world = new();
        EntityLiving slime = Slime(world, 2);
        Assert.True(world.Entities.SpawnEntity(slime));
        slime.OnGround = false;

        for (int tick = 0; tick < 100; tick++)
        {
            slime.Behaviors.Ticker!.OnTickLiving(slime);
            Assert.False(slime.Jumping);
        }
    }

    /// <summary>Landing squashes the mob, and the squash relaxes over the ticks that follow.</summary>
    [Fact]
    public void Landing_squashes_the_slime_and_the_squash_relaxes()
    {
        FakeWorldContext world = new();
        EntityLiving slime = Slime(world, 2);
        Assert.True(world.Entities.SpawnEntity(slime));
        HoppingBehavior hop = slime.Behaviors.Find<HoppingBehavior>()!;

        // Airborne, then touching down: the ticker compares the two.
        slime.OnGround = false;
        hop.OnTick(slime);
        slime.OnGround = true;
        hop.OnTickEnd(slime);

        float squashed = hop.Squish(slime, 1.0F);
        Assert.True(squashed < 0.0F, "Landing should squash the slime.");

        hop.OnTick(slime);
        hop.OnTickEnd(slime);

        Assert.True(hop.Squish(slime, 1.0F) > squashed, "The squash should relax after landing.");
    }

    /// <summary>Only a big slime hurts on contact, and only within a reach that scales with it.</summary>
    [Fact]
    public void Contact_damage_reads_the_size_without_knowing_what_a_slime_is()
    {
        FakeWorldContext world = new();
        EntityLiving slime = Slime(world, 4);
        Assert.True(world.Entities.SpawnEntity(slime));

        TestEntityPlayer player = new(world) { Name = "tester" };
        player.SetPositionAndAngles(9.5, 65.0, 8.5, 0f, 0f);
        Assert.True(world.Entities.SpawnEntity(player));

        int before = player.Health;
        Body.SetSize(slime, 1);
        slime.OnPlayerInteraction(player);
        Assert.Equal(before, player.Health);

        Body.SetSize(slime, 4);
        slime.OnPlayerInteraction(player);
        Assert.True(player.Health < before);
    }
}
