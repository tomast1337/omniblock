using OmniBlock.NBT;
using OmniBlock.Worlds.Chunks.Storage;

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
}
