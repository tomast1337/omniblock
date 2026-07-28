using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.NBT;
using BetaSharp.Rules;
using BetaSharp.Tests.TestSupport;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers primed TNT, the first non-living entity to lose its class. Everything it was — a lit
/// fuse, tumbling ballistics, the detonation and its rule gate — is declared in primedtnt.json and
/// carried by one behavior across three slots.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityTntTests
{
    private static PrimedExplosiveBehavior Fuse => EntityRegistry.ByName("primedtnt").Behaviors.Find<PrimedExplosiveBehavior>()!;

    private static Entity Tnt(FakeWorldContext world, double x = 8.5, double y = 66.0, double z = 8.5)
    {
        Entity tnt = EntityRegistry.ByName("primedtnt").Create(world);
        tnt.SetPositionAndAngles(x, y, z, 0.0F, 0.0F);
        Assert.True(world.Entities.SpawnEntity(tnt));
        return tnt;
    }

    [Fact]
    public void Primed_tnt_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();
        Entity tnt = Tnt(world);

        Assert.Equal(typeof(EntityObject), tnt.GetType());

        EntityBehaviorSet behaviors = EntityRegistry.ByName("primedtnt").Behaviors;
        Assert.Same(behaviors.Ticker, behaviors.Lifecycle);
        Assert.Same(behaviors.Ticker, behaviors.Persistence);
    }

    /// <summary>Created armed: full fuse, a pop upward, and a barely-there horizontal kick.</summary>
    [Fact]
    public void Primed_tnt_is_born_lit()
    {
        FakeWorldContext world = new();
        Entity tnt = Tnt(world);

        Assert.Equal(80, Fuse.FuseTicks(tnt));
        Assert.Equal(0.2F, tnt.VelocityY, 5);
    }

    [Fact]
    public void The_body_comes_from_the_definition()
    {
        FakeWorldContext world = new();
        Entity tnt = Tnt(world);

        Assert.Equal(0.98F, tnt.Width, 5);
        Assert.Equal(0.98F, tnt.Height, 5);
        Assert.Equal(tnt.Height / 2.0F, tnt.StandingEyeHeight, 5);
        Assert.True(tnt.PreventEntitySpawning);
        Assert.True(tnt.HasCollision);
        Assert.Equal(0.0F, tnt.GetShadowRadius());
    }

    [Fact]
    public void The_fuse_burns_down_one_tick_at_a_time()
    {
        FakeWorldContext world = new();
        Entity tnt = Tnt(world);

        tnt.Tick();
        Assert.Equal(79, Fuse.FuseTicks(tnt));

        tnt.Tick();
        Assert.Equal(78, Fuse.FuseTicks(tnt));
        Assert.False(tnt.Dead);
    }

    [Fact]
    public void Tnt_falls_and_settles_on_the_ground()
    {
        FakeWorldContext world = new();
        Entity tnt = Tnt(world);
        tnt.VelocityY = 0.0D;

        tnt.Tick();

        // Gravity pulled, drag applied: -0.04 * 0.98.
        Assert.True(tnt.VelocityY < 0.0D);
    }

    [Fact]
    public void A_spent_fuse_detonates_and_breaks_the_world()
    {
        FakeWorldContext world = new();
        int stone = BlockRegistry.Get("stone").id;
        world.Writer.SetBlock(9, 66, 8, stone);
        Entity tnt = Tnt(world);
        Fuse.SetFuse(tnt, 0);

        tnt.Tick();

        Assert.True(tnt.Dead);
        Assert.Equal(0, world.Reader.GetBlockId(9, 66, 8));
    }

    /// <summary>The rule gate: the fuse still spends and the entity still dies, but nothing breaks.</summary>
    [Fact]
    public void The_tnt_explodes_rule_defuses_the_blast()
    {
        FakeWorldContext world = new();
        world.Rules.Set(DefaultRules.TntExplodes, new BoolValue(false));
        int stone = BlockRegistry.Get("stone").id;
        world.Writer.SetBlock(9, 66, 8, stone);
        Entity tnt = Tnt(world);
        Fuse.SetFuse(tnt, 0);

        tnt.Tick();

        Assert.True(tnt.Dead);
        Assert.Equal(stone, world.Reader.GetBlockId(9, 66, 8));
    }

    /// <summary>Chain reactions run on a short random fuse, an eighth to three-eighths of the full one.</summary>
    [Fact]
    public void A_nearby_explosion_lights_a_short_fuse()
    {
        FakeWorldContext world = new();

        for (int attempt = 0; attempt < 20; attempt++)
        {
            Entity tnt = Tnt(world);
            Fuse.ShortenFuse(tnt);
            Assert.InRange(Fuse.FuseTicks(tnt), 10, 29);
        }
    }

    [Fact]
    public void The_fuse_survives_an_nbt_round_trip()
    {
        FakeWorldContext world = new();
        Entity tnt = Tnt(world);
        Fuse.SetFuse(tnt, 42);

        NBTTagCompound nbt = new();
        tnt.Write(nbt);

        Entity restored = Tnt(world);
        restored.Read(nbt);

        Assert.Equal(42, Fuse.FuseTicks(restored));
    }

    /// <summary>Both wire ids are protocol facts: the registry id and the object-spawn id.</summary>
    [Fact]
    public void Protocol_ids_are_pinned()
    {
        EntityType type = EntityRegistry.ByName("primedtnt");

        Assert.Equal(20, type.RequireDefinition().ProtocolId);
        Assert.Equal(50, type.RequireDefinition().SpawnObjectId);
        Assert.Same(type, EntityRegistry.BySpawnObjectId(50));
    }
}
