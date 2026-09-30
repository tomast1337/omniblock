using OmniBlock.Blocks;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Tests.Rendering;

public sealed class FenceRenderStateTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(15)]
    public void Every_neighbor_mask_selects_the_existing_post_and_half_rails(int mask)
    {
        FakeWorldContext world = new();
        var fenceId = world.Content.Blocks.Get("fence").Id;
        var stoneId = world.Content.Blocks.Get("stone").Id;
        var neighbors = new (int Bit, int X, int Z)[]
        {
            (FenceShape.West, -1, 0), (FenceShape.East, 1, 0),
            (FenceShape.North, 0, -1), (FenceShape.South, 0, 1)
        };
        world.ReaderWriter.SetInitial(0, 64, 0, fenceId, 7);
        foreach (var (bit, x, z) in neighbors)
            world.ReaderWriter.SetInitial(x, 64, z, (mask & bit) != 0 ? fenceId : stoneId, 3);

        var selected = FenceShape.ConnectionMask(world.Reader, fenceId, 0, 64, 0);

        Assert.Equal(mask, selected);
        var boxes = FenceShape.GetBounds(selected).ToArray();
        Assert.Equal(1 + 2 * System.Numerics.BitOperations.PopCount((uint)mask), boxes.Length);
        Assert.Equal(new Box(6d / 16, 0, 6d / 16, 10d / 16, 1, 10d / 16), boxes[0]);
    }

    [Fact]
    public void Chunk_boundary_uses_captured_neighbors_and_is_stable_after_live_edits()
    {
        FakeWorldContext world = new();
        var fenceId = world.Content.Blocks.Get("fence").Id;
        var left = world.ChunkHost.GetChunk(0, 0);
        var right = world.ChunkHost.GetChunk(1, 0);
        left.Blocks[ChuckFormat.GetIndex(15, 64, 0)] = (byte)fenceId;
        right.Blocks[ChuckFormat.GetIndex(0, 64, 0)] = (byte)fenceId;
        using var before = new WorldRegionSnapshot(world, 14, 63, 0, 17, 65, 1);

        right.Blocks[ChuckFormat.GetIndex(0, 64, 0)] = 0;
        using var after = new WorldRegionSnapshot(world, 14, 63, 0, 17, 65, 1);

        Assert.Equal(FenceShape.East, FenceShape.ConnectionMask(before, fenceId, 15, 64, 0));
        Assert.Equal(0, FenceShape.ConnectionMask(after, fenceId, 15, 64, 0));
    }
}
