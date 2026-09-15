using OmniBlock.Tests.TestSupport;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Tests.Rendering;

public sealed class WorldRegionSnapshotTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Bulk_capture_preserves_blocks_metadata_and_both_light_nibbles(int minimumY)
    {
        var world = new FakeWorldContext();
        var stone = world.Content.Blocks.Get("omniblock:stone").Id;
        for (var x = -2; x <= 17; x++)
        for (var z = -2; z <= 17; z++)
        for (var y = 0; y <= 18; y++)
        {
            var chunk = world.ChunkHost.GetChunk(x >> 4, z >> 4);
            var localX = x & 15;
            var localZ = z & 15;
            var index = ChuckFormat.GetIndex(localX, y, localZ);
            chunk.Blocks[index] = (byte)stone;
            chunk.Meta.SetNibble(localX, y, localZ, Pattern(x, y, z, 1));
            chunk.SkyLight.SetNibble(localX, y, localZ, Pattern(x, y, z, 5));
            chunk.BlockLight.SetNibble(localX, y, localZ, Pattern(x, y, z, 9));
        }

        using var snapshot = new WorldRegionSnapshot(
            world, -2, minimumY, -2, 17, minimumY + 19, 17);

        var firstY = Math.Max(0, minimumY);
        var lastY = Math.Min(18, minimumY + 19);
        for (var x = -2; x <= 17; x++)
        for (var z = -2; z <= 17; z++)
        for (var y = firstY; y <= lastY; y++)
        {
            Assert.Equal(stone, snapshot.GetBlockId(x, y, z));
            Assert.Equal(Pattern(x, y, z, 1), snapshot.GetBlockMeta(x, y, z));
            var light = snapshot.GetLightLevels(x, y, z, 0);
            Assert.Equal(Pattern(x, y, z, 5), light.Sky);
            Assert.Equal(Pattern(x, y, z, 9), light.Block);
        }
    }

    private static int Pattern(int x, int y, int z, int salt) =>
        (x * 3 + y * 5 + z * 7 + salt) & 15;
}
