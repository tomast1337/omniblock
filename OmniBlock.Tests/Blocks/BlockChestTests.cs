using OmniBlock.Blocks;
using OmniBlock.Blocks.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.NBT;
using OmniBlock.Screens;
using OmniBlock.Textures;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Chunks.Storage;

namespace OmniBlock.Tests.Blocks;

public sealed class BlockChestTests
{
    private static readonly AtlasTileMap s_terrain = AtlasTileMap.Load("textures/atlas/terrain.json");

    [Fact]
    public void Changing_chest_metadata_does_not_replace_its_inventory()
    {
        FakeWorldContext world = new();
        const int x = 20;
        const int y = 64;
        const int z = 20;
        var block = TestBlocks.Get("chest");
        var chunk = world.ChunkHost.GetChunk(x >> 4, z >> 4);
        world.ReaderWriter.SetInitial(x, y, z, block.Id);
        chunk.Blocks[ChuckFormat.GetIndex(x & 15, y, z & 15)] = (byte)block.Id;
        BlockEntityChest chest = new() { World = world, X = x, Y = y, Z = z };
        chunk.AddBlockEntity(chest);
        chest.SetStack(3, new ItemStack(world.Content.Items.Get("omniblock:coal"), 12));

        Assert.True(chunk.SetBlock(x & 15, y, z & 15, block.Id, meta: 1));

        Assert.Same(chest, chunk.GetBlockEntity(x & 15, y, z & 15));
        Assert.Equal(12, chest.GetStack(3)?.Count);
    }

    [Fact]
    public void Chest_contents_round_trip_through_chunk_nbt()
    {
        FakeWorldContext world = new();
        const int x = 20;
        const int y = 64;
        const int z = 20;
        var chestId = TestBlocks.Get("chest").Id;
        var chunk = world.ChunkHost.GetChunk(x >> 4, z >> 4);
        world.ReaderWriter.SetInitial(x, y, z, chestId);
        chunk.Blocks[ChuckFormat.GetIndex(x & 15, y, z & 15)] = (byte)chestId;
        BlockEntityChest chest = new() { World = world, X = x, Y = y, Z = z };
        chunk.AddBlockEntity(chest);
        var coal = world.Content.Items.Get("omniblock:coal");
        chest.SetStack(3, new ItemStack(coal, 12));

        NBTTagCompound level = new();
        RegionChunkStorage.storeChunkInCompound(chunk, world, level);
        var restored = RegionChunkStorage.LoadChunkFromNbt(world, level);
        var restoredChest = Assert.IsType<BlockEntityChest>(restored.GetBlockEntity(x & 15, y, z & 15));
        Assert.Equal(coal.Id, restoredChest.GetStack(3)?.ItemId);
        Assert.Equal(12, restoredChest.GetStack(3)?.Count);
    }

    [Fact]
    public void Chest_contents_reach_container_screen_snapshot()
    {
        FakeWorldContext world = new();
        BlockEntityChest chest = new();
        var coal = world.Content.Items.Get("omniblock:coal");
        chest.SetStack(3, new ItemStack(coal, 12));

        GenericContainerScreenHandler screen = new(new InventoryBasic("Player", 36), chest);

        Assert.Equal(coal.Id, screen.GetStacks()[3]?.ItemId);
        Assert.Equal(12, screen.GetStacks()[3]?.Count);
    }

    [Fact]
    public void GetTextureId_NorthSouthDoubleChestFacingEast_UsesCorrectFrontHalves()
    {
        FakeWorldContext world = new();
        PlaceNorthSouthDoubleChest(world);

        Assert.Equal(s_terrain.IndexOf("chest_double_front_right"), TestBlocks.Get("chest").GetTextureId(world.Reader, 0, 64, 0, Side.East));
        Assert.Equal(s_terrain.IndexOf("chest_double_front_left"), TestBlocks.Get("chest").GetTextureId(world.Reader, 0, 64, 1, Side.East));
    }

    [Fact]
    public void GetTextureId_NorthSouthDoubleChestFacingWest_UsesCorrectFrontHalves()
    {
        FakeWorldContext world = new();
        PlaceNorthSouthDoubleChest(world);
        world.ReaderWriter.SetInitial(1, 64, 0, TestBlocks.Get("stone").Id);
        world.ReaderWriter.SetInitial(1, 64, 1, TestBlocks.Get("stone").Id);

        Assert.Equal(s_terrain.IndexOf("chest_double_front_left"), TestBlocks.Get("chest").GetTextureId(world.Reader, 0, 64, 0, Side.West));
        Assert.Equal(s_terrain.IndexOf("chest_double_front_right"), TestBlocks.Get("chest").GetTextureId(world.Reader, 0, 64, 1, Side.West));
    }

    private static void PlaceNorthSouthDoubleChest(FakeWorldContext world)
    {
        world.ReaderWriter.SetInitial(0, 64, 0, TestBlocks.Get("chest").Id);
        world.ReaderWriter.SetInitial(0, 64, 1, TestBlocks.Get("chest").Id);
    }
}
