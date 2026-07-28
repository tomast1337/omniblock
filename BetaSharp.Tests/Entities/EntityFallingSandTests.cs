using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.NBT;
using BetaSharp.Tests.TestSupport;

namespace BetaSharp.Tests.Entities;

/// <summary>
/// Covers falling sand, the second non-living entity to lose its class. Which block is falling is
/// per-instance state on one behavior across two slots, and the sand/gravel wire ids are declared
/// data rather than a pair of hardcoded branches.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityFallingSandTests
{
    private static SettleAsBlockBehavior Settle => EntityRegistry.ByName("fallingsand").Behaviors.Find<SettleAsBlockBehavior>()!;

    private static Entity FallingBlock(FakeWorldContext world, string block, double x = 8.5, double y = 70.0, double z = 8.5)
    {
        Entity sand = EntityRegistry.ByName("fallingsand").Create(world);
        Settle.SetBlock(sand, BlockRegistry.Get(block).id);
        sand.SetPositionAndAngles(x, y, z, 0.0F, 0.0F);
        Assert.True(world.Entities.SpawnEntity(sand));
        return sand;
    }

    [Fact]
    public void Falling_sand_has_no_class_of_its_own()
    {
        FakeWorldContext world = new();
        Entity sand = FallingBlock(world, "sand");

        Assert.Equal(typeof(EntityObject), sand.GetType());
        Assert.Same(EntityRegistry.ByName("fallingsand").Behaviors.Ticker, EntityRegistry.ByName("fallingsand").Behaviors.Persistence);
    }

    [Fact]
    public void It_falls_and_settles_back_into_the_world_as_its_block()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);
        int sandId = BlockRegistry.Get("sand").id;
        Entity sand = FallingBlock(world, "sand", y: 70.0);

        for (int tick = 0; tick < 150 && !sand.Dead; tick++) sand.Tick();

        Assert.True(sand.Dead);
        Assert.Equal(sandId, world.Reader.GetBlockId(8, 64, 8));
    }

    /// <summary>The block it left behind is erased the moment the entity passes through its cell.</summary>
    [Fact]
    public void It_erases_the_block_it_fell_from()
    {
        FakeWorldContext world = new();
        int sandId = BlockRegistry.Get("sand").id;
        world.Writer.SetBlock(8, 70, 8, sandId);
        Entity sand = FallingBlock(world, "sand", y: 70.5);

        sand.Tick();

        Assert.Equal(0, world.Reader.GetBlockId(8, 70, 8));
    }

    /// <summary>Falling too long without landing gives up and drops the block as an item.</summary>
    [Fact]
    public void A_block_that_never_lands_becomes_a_drop()
    {
        FakeWorldContext world = new();
        Entity sand = FallingBlock(world, "sand", y: 200.0);

        for (int tick = 0; tick < 101 && !sand.Dead; tick++)
        {
            sand.Tick();
            sand.SetPositionAndAngles(8.5, 200.0, 8.5, 0.0F, 0.0F);
            sand.OnGround = false;
        }

        Assert.True(sand.Dead);
        Assert.Contains(world.Entities.Entities, entity => entity is EntityItem);
    }

    [Fact]
    public void A_block_of_nothing_dies_immediately()
    {
        FakeWorldContext world = new();
        Entity sand = EntityRegistry.ByName("fallingsand").Create(world);
        sand.SetPositionAndAngles(8.5, 70.0, 8.5, 0.0F, 0.0F);
        Assert.True(world.Entities.SpawnEntity(sand));

        sand.Tick();

        Assert.True(sand.Dead);
    }

    [Fact]
    public void The_carried_block_survives_an_nbt_round_trip()
    {
        FakeWorldContext world = new();
        Entity gravel = FallingBlock(world, "gravel");

        NBTTagCompound nbt = new();
        gravel.Write(nbt);

        Entity restored = EntityRegistry.ByName("fallingsand").Create(world);
        restored.Read(nbt);

        Assert.Equal(BlockRegistry.Get("gravel").id, Settle.BlockId(restored));
    }

    /// <summary>
    /// Sand and gravel are different ids on the wire — a protocol fact now declared in JSON. The
    /// behavior answers in both directions: announcing an instance, and resolving a received spawn.
    /// </summary>
    [Fact]
    public void Wire_ids_are_declared_per_block_and_pinned()
    {
        FakeWorldContext world = new();
        Entity sand = FallingBlock(world, "sand");
        Entity gravel = FallingBlock(world, "gravel", x: 10.5);

        Assert.Equal(70, Settle.SpawnObjectId(sand));
        Assert.Equal(71, Settle.SpawnObjectId(gravel));
        Assert.Equal(BlockRegistry.Get("sand").id, Settle.BlockForSpawnObjectId(70));
        Assert.Equal(BlockRegistry.Get("gravel").id, Settle.BlockForSpawnObjectId(71));
        Assert.Null(Settle.BlockForSpawnObjectId(50));

        Assert.Equal(21, EntityRegistry.ByName("fallingsand").RequireDefinition().ProtocolId);
    }
}
