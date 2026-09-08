using OmniBlock.Server;
using OmniBlock.Util.Maths;

namespace OmniBlock.Tests.Server;

public sealed class ChunkStreamingPrefetchTests
{
    [Fact]
    public void Stationary_player_uses_only_configured_square()
    {
        var chunks = ChunkMap.GetStreamingChunks(10, -4, 4, 0, 0);

        Assert.Equal(81, chunks.Length);
        Assert.Equal(new ChunkPos(10, -4), chunks[0]);
        Assert.All(chunks, pos =>
        {
            Assert.InRange(pos.X, 6, 14);
            Assert.InRange(pos.Z, -8, 0);
        });
    }

    [Fact]
    public void Cardinal_motion_adds_one_forward_strip()
    {
        var chunks = ChunkMap.GetStreamingChunks(0, 0, 4, 1, 0);

        Assert.Equal(90, chunks.Length);
        Assert.Equal(9, chunks.Count(pos => pos.X == 5));
        Assert.DoesNotContain(chunks, pos => pos.X < -4 || pos.X > 5);
    }

    [Fact]
    public void Diagonal_motion_adds_two_forward_edges_without_duplicates()
    {
        var chunks = ChunkMap.GetStreamingChunks(0, 0, 4, 1, -1);

        Assert.Equal(98, chunks.Length);
        Assert.Equal(chunks.Length, chunks.Distinct().Count());
        Assert.Contains(new ChunkPos(5, -5), chunks);
        Assert.DoesNotContain(new ChunkPos(-5, 5), chunks);
    }

    [Fact]
    public void Reversing_motion_moves_the_speculative_strip()
    {
        var east = ChunkMap.GetStreamingChunks(0, 0, 4, 1, 0).ToHashSet();
        var west = ChunkMap.GetStreamingChunks(0, 0, 4, -1, 0).ToHashSet();

        Assert.Contains(new ChunkPos(5, 0), east);
        Assert.DoesNotContain(new ChunkPos(5, 0), west);
        Assert.Contains(new ChunkPos(-5, 0), west);
        Assert.DoesNotContain(new ChunkPos(-5, 0), east);
    }
}
