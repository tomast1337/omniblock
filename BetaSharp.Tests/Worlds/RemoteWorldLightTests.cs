using BetaSharp.Blocks;
using BetaSharp.Tests.TestSupport;
using BetaSharp.Worlds.Lighting;

namespace BetaSharp.Tests.Worlds;

/// <summary>
///     A remote world holds only the light the wire writes and propagates none of its own —
///     setting a block on it must queue nothing for <see cref="LightTestWorld.DrainLighting" />
///     to drain.
/// </summary>
public sealed class RemoteWorldLightTests
{
    /// <summary>
    ///     A torch propagates block light into the surrounding air on a server world once the
    ///     queue drains. On a remote world it must not: the client shows what the wire delivers a
    ///     tick from now, and any light computed ahead of that is light the server never agreed to.
    /// </summary>
    [Fact]
    public void PlacingATorchLightsTheAirOnAServerWorldButNotOnARemoteOne()
    {
        LightTestWorld server = BuildWorldWithTorch(remote: false);
        LightTestWorld remote = BuildWorldWithTorch(remote: true);

        server.DrainLighting();
        remote.DrainLighting();

        // One cell above the torch: the server propagates to the torch's luminance minus one,
        // a remote world has nothing queued and stays at whatever the wire already left there.
        int serverAbove = server.Lighting.GetBrightness(LightType.Block, 8, 2, 8);
        int remoteAbove = remote.Lighting.GetBrightness(LightType.Block, 8, 2, 8);

        Assert.True(
            serverAbove > remoteAbove,
            $"server world should propagate torch light ({serverAbove}) but remote world should not ({remoteAbove})");
        Assert.Equal(0, remoteAbove);
    }

    private static LightTestWorld BuildWorldWithTorch(bool remote)
    {
        LightTestWorld world = new() { IsRemote = remote };
        world.Chunks.Add(0, 0);

        int torchId = BlockRegistry.Get("torch").Id;
        world.Writer.SetBlock(8, 1, 8, torchId, 0);

        return world;
    }
}
