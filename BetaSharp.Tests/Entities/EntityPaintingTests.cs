using OmniBlock.Entities;
using OmniBlock.Entities.Behaviors;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.Tests.TestSupport;

namespace OmniBlock.Tests.Entities;

/// <summary>
/// Covers the painting, the tenth non-living entity to lose its class — and the first whose box is
/// not its definition's: the canvas sizes it. Everything that disturbs a painting knocks it down as
/// an item, which is why the behavior fills the two new movement hooks as well as damage.
/// </summary>
[Collection("EntityTests")]
public sealed class EntityPaintingTests
{
    private static HangingArtBehavior Hanging => EntityRegistry.ByName("painting").Behaviors.Find<HangingArtBehavior>()!;

    /// <summary>
    /// The backing a painting anchored at (8, 65, 8) facing +Z hangs on. The art's own anchor block
    /// is the wall — the canvas sits nine-sixteenths of a block proud of it — so the strip goes at
    /// z=8, not behind it.
    /// </summary>
    private static FakeWorldContext WalledWorld()
    {
        FakeWorldContext world = new();
        EntityTestHarness.PlaceStoneFloor(world, 0, 15, 0, 15, 63);
        EntityTestHarness.PlaceStoneWallStrip(world, 8, 8, 64, 68);
        EntityTestHarness.PlaceStoneWallStrip(world, 9, 8, 64, 68);
        return world;
    }

    private static Entity Hang(FakeWorldContext world, string title = "Kebab")
    {
        Entity painting = HangingArtBehavior.HangAt(world, 8, 65, 8, 2, title);
        Assert.True(world.Entities.SpawnEntity(painting));
        return painting;
    }

    private static bool DroppedAPainting(FakeWorldContext world) =>
        world.Entities.Entities.Any(e =>
            EntityTestHarness.DroppedStack(e) is { } stack && stack.ItemId == Item.ByName("painting").Id);

    [Fact]
    public void A_painting_has_no_class_of_its_own()
    {
        FakeWorldContext world = WalledWorld();
        Entity painting = Hang(world);

        Assert.Equal(typeof(EntityObject), painting.GetType());
        Assert.Equal("Kebab", Hanging.Art(painting)!.Title);
        Assert.True(painting.HasCollision);
    }

    /// <summary>The canvas sizes the box, not the definition's 0.5 cube.</summary>
    [Fact]
    public void The_art_sizes_the_bounding_box()
    {
        FakeWorldContext world = WalledWorld();
        Entity small = Hang(world, "Kebab");

        FakeWorldContext otherWorld = WalledWorld();
        Entity wide = HangingArtBehavior.HangAt(otherWorld, 8, 65, 8, 2, "Pool");
        Assert.True(otherWorld.Entities.SpawnEntity(wide));

        Assert.True(wide.BoundingBox.MaxX - wide.BoundingBox.MinX > small.BoundingBox.MaxX - small.BoundingBox.MinX,
            "A 32-wide art hangs across more space than a 16-wide one.");
    }

    [Fact]
    public void A_hit_knocks_the_painting_down_as_an_item()
    {
        FakeWorldContext world = WalledWorld();
        Entity painting = Hang(world);

        Assert.True(painting.Damage(null, 1));

        Assert.True(painting.Dead);
        Assert.True(DroppedAPainting(world));
    }

    /// <summary>A painting does not travel: being moved at all is what takes it off the wall.</summary>
    [Fact]
    public void Being_moved_knocks_the_painting_down()
    {
        FakeWorldContext world = WalledWorld();
        Entity painting = Hang(world);

        painting.Move(0.1, 0.0, 0.0);

        Assert.True(painting.Dead);
        Assert.True(DroppedAPainting(world));
    }

    [Fact]
    public void Being_shoved_knocks_the_painting_down()
    {
        FakeWorldContext world = WalledWorld();
        Entity painting = Hang(world);

        painting.AddVelocity(0.0, 0.2, 0.0);

        Assert.True(painting.Dead);
        Assert.True(DroppedAPainting(world));
    }

    /// <summary>A zero-length move is not a disturbance, so the painting stays hung.</summary>
    [Fact]
    public void A_still_painting_on_a_solid_wall_stays_hung()
    {
        FakeWorldContext world = WalledWorld();
        Entity painting = Hang(world);

        painting.Move(0.0, 0.0, 0.0);
        for (int tick = 0; tick < 200; tick++) painting.Tick();

        Assert.False(painting.Dead);
    }

    /// <summary>The periodic check is what notices the wall went away.</summary>
    [Fact]
    public void A_painting_whose_wall_is_gone_falls_on_the_next_check()
    {
        FakeWorldContext world = WalledWorld();
        Entity painting = Hang(world);

        for (int y = 64; y <= 68; y++) world.Writer.SetBlock(8, y, 8, 0);

        for (int tick = 0; tick < 200 && !painting.Dead; tick++) painting.Tick();

        Assert.True(painting.Dead);
        Assert.True(DroppedAPainting(world));
    }

    [Fact]
    public void Two_paintings_cannot_claim_the_same_wall()
    {
        FakeWorldContext world = WalledWorld();
        Hang(world);

        Entity second = HangingArtBehavior.HangAt(world, 8, 65, 8, 2, "Kebab");

        Assert.False(second.Behaviors.Find<HangingArtBehavior>()!.CanHang(second));
    }

    [Fact]
    public void The_art_and_anchor_survive_an_nbt_round_trip()
    {
        FakeWorldContext world = WalledWorld();
        Entity painting = Hang(world, "Aztec");

        NBTTagCompound nbt = new();
        painting.Write(nbt);

        Entity restored = EntityRegistry.ByName("painting").Create(world);
        restored.Read(nbt);

        HangingArtBehavior hanging = restored.Behaviors.Find<HangingArtBehavior>()!;
        Assert.Equal("Aztec", hanging.Art(restored)!.Title);
        Assert.Equal(2, hanging.Direction(restored));
        Assert.Equal(8, hanging.TileX(restored));
        Assert.Equal(65, hanging.TileY(restored));
        Assert.Equal(8, hanging.TileZ(restored));
    }

    [Fact]
    public void Protocol_facts_are_pinned()
    {
        EntityDefinition painting = EntityRegistry.ByName("painting").RequireDefinition();

        Assert.Equal(9, painting.ProtocolId);
        Assert.Equal(0, painting.SpawnObjectId);
        Assert.Equal(160, painting.TrackingRange);
        Assert.Equal(int.MaxValue, painting.TrackingFrequency);
    }
}
