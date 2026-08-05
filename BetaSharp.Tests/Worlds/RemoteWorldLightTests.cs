using BetaSharp.Blocks;
using BetaSharp.Tests.TestSupport;
using BetaSharp.Worlds.Lighting;

namespace BetaSharp.Tests.Worlds;

/// <summary>
///     That a remote world holds only the light the wire writes, and propagates none of its own.
/// </summary>
/// <remarks>
///     <para>
///         The client used to run the same light engine the server does, over the same block
///         changes, so two writers filled its light with nothing arbitrating them: the wire — the
///         chunk blob on load, the section snapshot on change — and the client's own propagation.
///         They agree where the client's neighbours match the server's, and diverge where one is
///         still loading, which is a wrong frame that the next section only corrects later.
///     </para>
///     <para>
///         The engine is shared, so it cannot stop propagating for the client by being a different
///         engine; it has to refuse to propagate for a world that says it is remote. That is the
///         property these tests pin: setting a block on a remote world queues nothing.
///     </para>
/// </remarks>
public sealed class RemoteWorldLightTests
{
    /// <summary>
    ///     The regression the tests above pin in reverse. A torch placed on a server world
    ///     propagates its block light into the surrounding air once the light queue is drained;
    ///     the same torch placed on a remote world must not, because the client is meant to show
    ///     exactly what the wire will deliver, a tick from now, and any light computed before that
    ///     is light the server did not agree to.
    /// </summary>
    [Fact]
    public void A_remote_world_does_not_propagate_the_light_a_server_world_would()
    {
        LightTestWorld server = Build();
        LightTestWorld remote = Build(remote: true);

        PlaceTorch(server);
        PlaceTorch(remote);

        server.DrainLighting();
        remote.DrainLighting();

        // One cell above the torch. The server's propagation reaches it at the torch's luminance
        // minus one; a remote world has nothing queued, so it stays at the zero the wire left.
        int serverAbove = server.Lighting.GetBrightness(LightType.Block, 8, 2, 8);
        int remoteAbove = remote.Lighting.GetBrightness(LightType.Block, 8, 2, 8);

        Assert.True(
            serverAbove > remoteAbove,
            $"a server world should propagate torch light ({serverAbove}) but a remote one should not ({remoteAbove})");
        Assert.Equal(0, remoteAbove);
    }

    private static LightTestWorld Build(bool remote = false)
    {
        LightTestWorld world = new() { IsRemote = remote };
        world.Chunks.Add(0, 0);
        return world;
    }

    private static void PlaceTorch(LightTestWorld world)
    {
        int torchId = BlockRegistry.Get("torch").Id;
        world.Writer.SetBlock(8, 1, 8, torchId, 0);
    }
}
