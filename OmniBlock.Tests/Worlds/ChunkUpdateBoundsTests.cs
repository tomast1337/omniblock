using OmniBlock.Worlds.Core;

namespace OmniBlock.Tests.Worlds;

public sealed class ChunkUpdateBoundsTests
{
    [Theory]
    [InlineData(0, 16, 15)]
    [InlineData(32, 16, 47)]
    [InlineData(-32, 16, -17)]
    [InlineData(0, 128, 127)]
    [InlineData(7, 1, 7)]
    public void Count_based_update_extent_is_converted_to_an_inclusive_maximum(
        int start,
        int length,
        int expected)
    {
        Assert.Equal(expected, World.InclusiveUpdateEnd(start, length));
    }

    [Fact]
    public void Empty_update_extent_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => World.InclusiveUpdateEnd(0, 0));
    }
}
