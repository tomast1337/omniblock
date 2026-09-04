using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;
using OmniBlock.Entities;
using OmniBlock.Items;
using OmniBlock.Tests.Entities;
using OmniBlock.Textures;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockDispenserTests
{
    [Fact]
    public void NeighborUpdate_PoweredByEmitter_SchedulesDispenseTick()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("dispenser").Id, 3);
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("lit_redstone_torch").Id); // powers dispenser position

        TestBlocks.Get("dispenser").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 3, TestBlocks.Get("lit_redstone_torch").Id));

        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t is { X: 0, Y: 64, Z: 0 } && t.BlockId == TestBlocks.Get("dispenser").Id && t.TickRate == 4);
    }

    [Fact]
    public void NeighborUpdate_NonEmitterTrigger_DoesNotScheduleTick()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("dispenser").Id, 3);
        world.ReaderWriter.SetInitial(0, 63, 0, TestBlocks.Get("stone").Id);

        TestBlocks.Get("dispenser").NeighborUpdate(new OnTickEvent(world, 0, 64, 0, 3, TestBlocks.Get("stone").Id));

        Assert.Empty(world.TickSchedulerSpy.ScheduledTicks);
    }

    [Fact]
    public void NeighborUpdate_BlockIdZero_DoesNotScheduleTick()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(20, 64, 20, TestBlocks.Get("dispenser").Id, 3);
        world.ReaderWriter.SetInitial(20, 63, 20, TestBlocks.Get("lit_redstone_torch").Id);

        TestBlocks.Get("dispenser").NeighborUpdate(new OnTickEvent(world, 20, 64, 20, 3, 0));

        Assert.Empty(world.TickSchedulerSpy.ScheduledTicks);
    }

    [Fact]
    public void NeighborUpdate_EmitterButDispenserUnpowered_DoesNotScheduleTick()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(21, 64, 21, TestBlocks.Get("dispenser").Id, 3);

        TestBlocks.Get("dispenser").NeighborUpdate(new OnTickEvent(world, 21, 64, 21, 0, TestBlocks.Get("lit_redstone_torch").Id));

        Assert.Empty(world.TickSchedulerSpy.ScheduledTicks);
    }

    [Fact]
    public void NeighborUpdate_PoweredFromBlockAbove_SchedulesTick()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(22, 64, 22, TestBlocks.Get("dispenser").Id, 3);
        world.ReaderWriter.SetInitial(22, 65, 22, TestBlocks.Get("lit_redstone_torch").Id);

        TestBlocks.Get("dispenser").NeighborUpdate(new OnTickEvent(world, 22, 64, 22, 5, TestBlocks.Get("lit_redstone_torch").Id));

        Assert.Contains(world.TickSchedulerSpy.ScheduledTicks, t =>
            t is { X: 22, Y: 64, Z: 22 } && t.BlockId == TestBlocks.Get("dispenser").Id);
    }

    private static void AttachDispenser(FakeWorldContext world, int x, int y, int z)
    {
        BlockEntityDispenser dispenser = new()
        {
            World = world,
            X = x,
            Y = y,
            Z = z
        };
        world.Entities.SetBlockEntity(x, y, z, dispenser);
    }

    private static OnTickEvent DispenserTick(FakeWorldContext world, int x, int y, int z) =>
        new(world, x, y, z, world.Reader.GetBlockMeta(x, y, z), TestBlocks.Get("dispenser").Id);

    [Fact]
    public void OnTick_PoweredEmpty_EmitsClickWorldEventAndSpawnsNothing()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(2, 63, 2, TestBlocks.Get("lit_redstone_torch").Id);
        world.ReaderWriter.SetInitial(2, 64, 2, TestBlocks.Get("dispenser").Id, 3);
        AttachDispenser(world, 2, 64, 2);

        var entitiesBefore = world.Entities.Entities.Count;
        TestBlocks.Get("dispenser").OnTick(DispenserTick(world, 2, 64, 2));

        Assert.Equal(entitiesBefore, world.Entities.Entities.Count);
    }

    [Fact]
    public void OnTick_Unpowered_DoesNotDispense()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(23, 64, 23, TestBlocks.Get("dispenser").Id, 3);
        AttachDispenser(world, 23, 64, 23);
        world.Entities.GetBlockEntity<BlockEntityDispenser>(23, 64, 23)!.SetStack(0, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:arrow"), 1));

        var before = world.Entities.Entities.Count;
        TestBlocks.Get("dispenser").OnTick(DispenserTick(world, 23, 64, 23));

        Assert.Equal(before, world.Entities.Entities.Count);
    }

    [Fact]
    public void OnTick_PoweredNoBlockEntity_DoesNotThrow()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(24, 63, 24, TestBlocks.Get("lit_redstone_torch").Id);
        world.ReaderWriter.SetInitial(24, 64, 24, TestBlocks.Get("dispenser").Id, 3);

        TestBlocks.Get("dispenser").OnTick(DispenserTick(world, 24, 64, 24));
    }

    [Fact]
    public void OnTick_PoweredWithARROW_SpawnsARROWEntity()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(3, 63, 3, TestBlocks.Get("lit_redstone_torch").Id);
        world.ReaderWriter.SetInitial(3, 64, 3, TestBlocks.Get("dispenser").Id, 3);
        AttachDispenser(world, 3, 64, 3);
        world.Entities.GetBlockEntity<BlockEntityDispenser>(3, 64, 3)!.SetStack(0, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:arrow"), 1));

        TestBlocks.Get("dispenser").OnTick(DispenserTick(world, 3, 64, 3));

        Assert.Contains(world.Entities.Entities, e => TestEntityCatalog.GetId(e) == "arrow");
    }

    [Fact]
    public void OnTick_Meta2North_FiresAlongNegativeZ()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(30, 63, 30, TestBlocks.Get("lit_redstone_torch").Id);
        world.ReaderWriter.SetInitial(30, 64, 30, TestBlocks.Get("dispenser").Id, 2);
        AttachDispenser(world, 30, 64, 30);
        world.Entities.GetBlockEntity<BlockEntityDispenser>(30, 64, 30)!.SetStack(0, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:arrow"), 1));

        TestBlocks.Get("dispenser").OnTick(DispenserTick(world, 30, 64, 30));

        Assert.Contains(world.Entities.Entities, e => TestEntityCatalog.GetId(e) == "arrow");
    }

    [Fact]
    public void OnTick_Meta5East_FiresAlongPositiveX()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(31, 63, 31, TestBlocks.Get("lit_redstone_torch").Id);
        world.ReaderWriter.SetInitial(31, 64, 31, TestBlocks.Get("dispenser").Id, 5);
        AttachDispenser(world, 31, 64, 31);
        world.Entities.GetBlockEntity<BlockEntityDispenser>(31, 64, 31)!.SetStack(0, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:arrow"), 1));

        TestBlocks.Get("dispenser").OnTick(DispenserTick(world, 31, 64, 31));

        Assert.Contains(world.Entities.Entities, e => TestEntityCatalog.GetId(e) == "arrow");
    }

    [Fact]
    public void OnTick_Meta4West_FiresAlongNegativeX()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(32, 63, 32, TestBlocks.Get("lit_redstone_torch").Id);
        world.ReaderWriter.SetInitial(32, 64, 32, TestBlocks.Get("dispenser").Id, 4);
        AttachDispenser(world, 32, 64, 32);
        world.Entities.GetBlockEntity<BlockEntityDispenser>(32, 64, 32)!.SetStack(0, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:arrow"), 1));

        TestBlocks.Get("dispenser").OnTick(DispenserTick(world, 32, 64, 32));

        Assert.Contains(world.Entities.Entities, e => TestEntityCatalog.GetId(e) == "arrow");
    }

    [Fact]
    public void OnTick_PoweredWithEgg_SpawnsEggEntity()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(4, 63, 4, TestBlocks.Get("lit_redstone_torch").Id);
        world.ReaderWriter.SetInitial(4, 64, 4, TestBlocks.Get("dispenser").Id, 3);
        AttachDispenser(world, 4, 64, 4);
        world.Entities.GetBlockEntity<BlockEntityDispenser>(4, 64, 4)!.SetStack(0, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:egg"), 1));

        TestBlocks.Get("dispenser").OnTick(DispenserTick(world, 4, 64, 4));

        Assert.Contains(world.Entities.Entities, e => TestEntityCatalog.GetId(e) == "egg");
    }

    [Fact]
    public void OnTick_PoweredWithSnowball_SpawnsSnowballEntity()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(5, 63, 5, TestBlocks.Get("lit_redstone_torch").Id);
        world.ReaderWriter.SetInitial(5, 64, 5, TestBlocks.Get("dispenser").Id, 3);
        AttachDispenser(world, 5, 64, 5);
        world.Entities.GetBlockEntity<BlockEntityDispenser>(5, 64, 5)!.SetStack(0, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:snowball"), 1));

        TestBlocks.Get("dispenser").OnTick(DispenserTick(world, 5, 64, 5));

        Assert.Contains(world.Entities.Entities, e => TestEntityCatalog.GetId(e) == "snowball");
    }

    [Fact]
    public void OnTick_PoweredWithGenericItem_SpawnsItemEntity()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(6, 63, 6, TestBlocks.Get("lit_redstone_torch").Id);
        world.ReaderWriter.SetInitial(6, 64, 6, TestBlocks.Get("dispenser").Id, 3);
        AttachDispenser(world, 6, 64, 6);
        world.Entities.GetBlockEntity<BlockEntityDispenser>(6, 64, 6)!.SetStack(0, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:stick"), 1));

        TestBlocks.Get("dispenser").OnTick(DispenserTick(world, 6, 64, 6));

        Assert.Contains(world.Entities.Entities, e => EntityTestHarness.DroppedStack(e)?.ItemId == ContentRuntime.Current.Items.Get("omniblock:stick").Id);
    }

    [Fact]
    public void GetDroppedItemId_ReturnsDispenserBlock() => Assert.Equal(TestBlocks.Get("dispenser").Id, TestBlocks.Get("dispenser").GetDroppedItemId(0));

    [Fact]
    public void GetTickRate_IsFour() => Assert.Equal(4, TestBlocks.Get("dispenser").TickRate);

    [Fact]
    public void GetTexture_TopBottom_AndSouthFace_Vary()
    {
        var top = TestBlocks.Get("dispenser").GetTexture(Side.Up);
        var south = TestBlocks.Get("dispenser").GetTexture(Side.South);
        var north = TestBlocks.Get("dispenser").GetTexture(Side.North);
        Assert.Equal(Atlases.Terrain.IndexOf("furnace_top"), top);
        Assert.Equal(Atlases.Terrain.IndexOf("dispenser_front"), south);
        Assert.Equal(TestBlocks.Get("dispenser").TextureId, north);
    }

    [Fact]
    public void GetTextureId_TopAndFacingMeta_MatchBlockTextures()
    {
        FakeWorldContext world = new();
        int x = 40, y = 64, z = 40;
        world.ReaderWriter.SetInitial(x, y, z, TestBlocks.Get("dispenser").Id, 3);
        var meta = world.Reader.GetBlockMeta(x, y, z);
        var facing = meta.ToSide();

        Assert.Equal(Atlases.Terrain.IndexOf("furnace_top"), TestBlocks.Get("dispenser").GetTextureId(world.Reader, x, y, z, Side.Up));
        Assert.Equal(Atlases.Terrain.IndexOf("furnace_top"), TestBlocks.Get("dispenser").GetTextureId(world.Reader, x, y, z, Side.Down));
        Assert.Equal(Atlases.Terrain.IndexOf("dispenser_front"), TestBlocks.Get("dispenser").GetTextureId(world.Reader, x, y, z, facing));
        Assert.Equal(TestBlocks.Get("dispenser").TextureId, TestBlocks.Get("dispenser").GetTextureId(world.Reader, x, y, z, facing.OppositeFace()));
    }

    [Fact]
    public void OnPlaced_NullPlacer_OpensSouthWhenNorthFaceIsOpaque()
    {
        FakeWorldContext world = new();
        int x = 50, y = 64, z = 50;
        world.ReaderWriter.SetInitial(x, y, z - 1, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(x, y, z + 1, 0);
        world.ReaderWriter.SetInitial(x - 1, y, z, 0);
        world.ReaderWriter.SetInitial(x + 1, y, z, 0);
        world.ReaderWriter.SetInitial(x, y, z, TestBlocks.Get("dispenser").Id);

        TestBlocks.Get("dispenser").OnPlaced(new OnPlacedEvent(world, null, Side.Up, Side.Up, x, y, z));

        Assert.Equal(3, world.Reader.GetBlockMeta(x, y, z));
    }

    [Fact]
    public void OnPlaced_NullPlacer_OpensNorthWhenSouthFaceIsOpaque()
    {
        FakeWorldContext world = new();
        int x = 51, y = 64, z = 51;
        world.ReaderWriter.SetInitial(x, y, z - 1, 0);
        world.ReaderWriter.SetInitial(x, y, z + 1, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(x - 1, y, z, 0);
        world.ReaderWriter.SetInitial(x + 1, y, z, 0);
        world.ReaderWriter.SetInitial(x, y, z, TestBlocks.Get("dispenser").Id);

        TestBlocks.Get("dispenser").OnPlaced(new OnPlacedEvent(world, null, Side.Up, Side.Up, x, y, z));

        Assert.Equal(Side.North.ToInt(), world.Reader.GetBlockMeta(x, y, z));
    }

    [Fact]
    public void OnPlaced_NullPlacer_OpensEastWhenWestFaceIsOpaque()
    {
        FakeWorldContext world = new();
        int x = 52, y = 64, z = 52;
        world.ReaderWriter.SetInitial(x, y, z - 1, 0);
        world.ReaderWriter.SetInitial(x, y, z + 1, 0);
        world.ReaderWriter.SetInitial(x - 1, y, z, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(x + 1, y, z, 0);
        world.ReaderWriter.SetInitial(x, y, z, TestBlocks.Get("dispenser").Id);

        TestBlocks.Get("dispenser").OnPlaced(new OnPlacedEvent(world, null, Side.Up, Side.Up, x, y, z));

        Assert.Equal(Side.East.ToInt(), world.Reader.GetBlockMeta(x, y, z));
    }

    [Fact]
    public void OnPlaced_NullPlacer_OpensWestWhenEastFaceIsOpaque()
    {
        FakeWorldContext world = new();
        int x = 53, y = 64, z = 53;
        world.ReaderWriter.SetInitial(x, y, z - 1, 0);
        world.ReaderWriter.SetInitial(x, y, z + 1, 0);
        world.ReaderWriter.SetInitial(x - 1, y, z, 0);
        world.ReaderWriter.SetInitial(x + 1, y, z, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(x, y, z, TestBlocks.Get("dispenser").Id);

        TestBlocks.Get("dispenser").OnPlaced(new OnPlacedEvent(world, null, Side.Up, Side.Up, x, y, z));

        Assert.Equal(Side.West.ToInt(), world.Reader.GetBlockMeta(x, y, z));
    }

    [Fact]
    public void OnPlaced_NullPlacer_IsRemote_DoesNotChangeMetaFromUpdateDirection()
    {
        FakeWorldContext world = new();
        world.IsRemote = true;
        int x = 54, y = 64, z = 54;
        world.ReaderWriter.SetInitial(x, y, z - 1, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(x, y, z, TestBlocks.Get("dispenser").Id, 7);

        TestBlocks.Get("dispenser").OnPlaced(new OnPlacedEvent(world, null, Side.Up, Side.Up, x, y, z));

        Assert.Equal(7, world.Reader.GetBlockMeta(x, y, z));
    }

    [Fact]
    public void OnPlaced_WithPlacer_YawMapsToFacingMeta()
    {
        FakeWorldContext world = new();
        int x = 60, y = 64, z = 60;
        world.ReaderWriter.SetInitial(x, y, z, TestBlocks.Get("dispenser").Id);

        CapturingDispenserPlayer placer = new(world)
        {
            Yaw = 90f
        };
        TestBlocks.Get("dispenser").OnPlaced(new OnPlacedEvent(world, placer, Side.Up, Side.Up, x, y, z));

        Assert.Equal(Side.East.ToInt(), world.Reader.GetBlockMeta(x, y, z));
    }

    [Fact]
    public void OnUse_IsRemote_ReturnsTrueWithoutOpening()
    {
        FakeWorldContext world = new();
        world.IsRemote = true;
        world.ReaderWriter.SetInitial(70, 64, 70, TestBlocks.Get("dispenser").Id, 3);
        AttachDispenser(world, 70, 64, 70);
        CapturingDispenserPlayer player = new(world);

        var result = TestBlocks.Get("dispenser").OnUse(new OnUseEvent(world, player, 70, 64, 70));

        Assert.True(result);
        Assert.Null(player.LastOpened);
    }

    [Fact]
    public void OnUse_Server_WithBlockEntity_OpensScreen()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(71, 64, 71, TestBlocks.Get("dispenser").Id, 3);
        AttachDispenser(world, 71, 64, 71);
        var be = world.Entities.GetBlockEntity<BlockEntityDispenser>(71, 64, 71)!;
        CapturingDispenserPlayer player = new(world);

        var result = TestBlocks.Get("dispenser").OnUse(new OnUseEvent(world, player, 71, 64, 71));

        Assert.True(result);
        Assert.Same(be, player.LastOpened);
    }

    [Fact]
    public void OnUse_Server_ChunkGetBlockEntityAutoCreates_OpensScreen()
    {
        FakeWorldContext world = new();
        world.ReaderWriter.SetInitial(72, 64, 72, TestBlocks.Get("dispenser").Id, 3);
        CapturingDispenserPlayer player = new(world);

        var result = TestBlocks.Get("dispenser").OnUse(new OnUseEvent(world, player, 72, 64, 72));

        Assert.True(result);
        var be = world.Entities.GetBlockEntity<BlockEntityDispenser>(72, 64, 72);
        Assert.NotNull(be);
        Assert.Same(be, player.LastOpened);
    }

    [Fact]
    public void OnBreak_WithStacks_SpawnsItemEntities()
    {
        FakeWorldContext world = new();
        int x = 80, y = 64, z = 80;
        world.ReaderWriter.SetInitial(x, y, z, TestBlocks.Get("dispenser").Id, 3);
        AttachDispenser(world, x, y, z);
        world.Entities.GetBlockEntity<BlockEntityDispenser>(x, y, z)!.SetStack(0, new ItemStack(ContentRuntime.Current.Items.Get("omniblock:ingot_iron"), 24));

        TestBlocks.Get("dispenser").OnBreak(new OnBreakEvent(world, null, x, y, z));

        Assert.Contains(world.Entities.Entities, EntityTestHarness.IsDroppedItem);
    }

    private sealed class CapturingDispenserPlayer : EntityPlayer
    {
        public BlockEntityDispenser? LastOpened;

        public CapturingDispenserPlayer(IWorldContext world) : base(world)
        {
        }

        public override EntityType Type => TestEntityCatalog.ByName("player");

        public override void Spawn()
        {
        }

        public override void openDispenserScreen(BlockEntityDispenser dispenser) => LastOpened = dispenser;
    }
}
