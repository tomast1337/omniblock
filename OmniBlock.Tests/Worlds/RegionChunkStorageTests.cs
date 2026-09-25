using OmniBlock.NBT;
using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Chunks.Storage;
using OmniBlock.Worlds.Storage.RegionFormat;

namespace OmniBlock.Tests.Worlds;

public sealed class RegionChunkStorageTests
{
    [Fact]
    public void Chunk_coordinates_are_read_from_level_compound()
    {
        NBTTagCompound root = new();
        NBTTagCompound level = new();
        level.SetInteger("xPos", 4);
        level.SetInteger("zPos", -4);
        root.SetTag("Level", level);

        Assert.True(RegionChunkStorage.HasExpectedCoordinates(root.GetCompoundTag("Level"), 4, -4));
        Assert.False(RegionChunkStorage.HasExpectedCoordinates(root.GetCompoundTag("Level"), 1, -16));
    }

    [Fact]
    public void Terrain_only_read_uses_saved_chunk_without_activating_it_or_writing_the_region()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-lod-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var world = new FakeWorldContext();
            var storage = new RegionChunkStorage(root);
            var chunk = new Chunk(world, new byte[ChuckFormat.ChunkSize], -1, 2);
            chunk[3, 7, 4] = world.Content.Blocks.Get("omniblock:stone").Id;
            chunk.SetBlockMeta(3, 7, 4, 2);
            storage.SaveChunk(world, chunk, null, 1);
            storage.FlushToDisk();
            var region = Path.Combine(root, "region", "r.-1.0.mcr");
            var before = File.ReadAllBytes(region);

            var source = storage.ReadTerrainLodSource(-1, 2, hasSkyLight: true);

            Assert.NotNull(source);
            Assert.Equal(chunk[3, 7, 4], source.GetBlock(3, 7, 4));
            Assert.Equal(2, source.GetMetadata(3, 7, 4));
            Assert.Equal(chunk.TerrainRevision, source.TerrainRevision);
            Assert.False(chunk.Loaded);
            Assert.Equal(before, File.ReadAllBytes(region));
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Missing_terrain_only_read_does_not_create_a_region_file()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-lod-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var storage = new RegionChunkStorage(root);
            Assert.Null(storage.ReadTerrainLodSource(1024, -1024, hasSkyLight: true));
            Assert.False(Directory.Exists(Path.Combine(root, "region")));
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Terrain_only_read_works_while_the_gameplay_region_handle_is_open()
    {
        var root = Path.Combine(Path.GetTempPath(), $"omniblock-lod-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var world = new FakeWorldContext();
            var storage = new RegionChunkStorage(root);
            var chunk = new Chunk(world, new byte[ChuckFormat.ChunkSize], 5, -2);
            chunk[1, 8, 2] = world.Content.Blocks.Get("omniblock:stone").Id;
            storage.SaveChunk(world, chunk, null, 1);

            // SaveChunk leaves the region cached and its exclusive file handle open.
            var source = storage.ReadTerrainLodSource(5, -2, hasSkyLight: true);

            Assert.NotNull(source);
            Assert.Equal(chunk[1, 8, 2], source.GetBlock(1, 8, 2));
            Assert.False(chunk.Loaded);
        }
        finally
        {
            RegionIo.Flush();
            Directory.Delete(root, true);
        }
    }
}
