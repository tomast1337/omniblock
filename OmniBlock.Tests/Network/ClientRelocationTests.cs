using OmniBlock.Client.Network;

namespace OmniBlock.Tests.Network;

public sealed class ClientRelocationTests
{
    [Theory]
    [InlineData(0, 0, 31, 31, false)]
    [InlineData(0, 0, 32, 0, true)]
    [InlineData(-1, -1, -33, -1, true)]
    [InlineData(160, 160, 145, 145, false)]
    public void Relocation_uses_chunk_distance_not_block_distance(
        double oldX, double oldZ, double newX, double newZ, bool expected)
    {
        Assert.Equal(expected, ClientNetworkHandler.IsRelocation(oldX, oldZ, newX, newZ));
    }
}
