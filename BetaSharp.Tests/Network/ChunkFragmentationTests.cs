using System.Net;
using BetaSharp.Network.Transport;
using LiteNetLib;
using Xunit.Abstractions;

namespace BetaSharp.Tests.Network;

/// <summary>
///     How many datagrams a chunk actually becomes.
///     <para>
///         The case for building a custom chunk transfer protocol rests on this number, and it was
///         originally argued from the inherited format. <see cref="ChunkBlobCodec" /> changed it by
///         more than an order of magnitude, so it is pinned here: whether selective repeat is worth
///         building follows directly from this figure.
///     </para>
/// </summary>
public sealed class ChunkFragmentationTests(ITestOutputHelper output)
{
    /// <summary>Average compressed chunk, from the 200-chunk measurement.</summary>
    private const int TypicalChunkBytes = 1966;

    /// <summary>What the inherited format cost after compression, and what the case was argued from.</summary>
    private const int AssumedChunkBytes = 81_920;

    [Fact]
    public async Task A_chunk_is_a_handful_of_datagrams_not_seventy()
    {
        using CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(10));

        await using LiteNetLibTransport server = new();
        server.Listen(0);

        await using LiteNetLibTransport client = new();
        client.StartClient();

        Task<ITransportConnection> accepting = FirstAsync(server, cancellation.Token);
        ITransportConnection peer = await client.ConnectAsync(
            new IPEndPoint(IPAddress.Loopback, server.LocalPort), cancellation.Token);

        await accepting;

        int mtu = peer.Stats.Mtu;
        Assert.True(mtu > 0, "the peer reported no MTU");

        int fragments = (TypicalChunkBytes + mtu - 1) / mtu;
        int assumed = (AssumedChunkBytes + mtu - 1) / mtu;

        output.WriteLine($"MTU {mtu}: a {TypicalChunkBytes} byte chunk is {fragments} datagram(s); "
            + $"the inherited {AssumedChunkBytes} byte form would be {assumed}");

        // The claim the transfer protocol rests on. A lost fragment can only block what is behind it
        // within its own message, so at this depth head-of-line blocking inside one chunk is not a
        // problem worth a bespoke protocol to solve.
        Assert.True(
            fragments <= 4,
            $"a chunk is {fragments} datagrams at MTU {mtu}; past a handful, selective repeat "
            + "starts being worth building after all");
    }

    private static async Task<ITransportConnection> FirstAsync(ITransport transport, CancellationToken token)
    {
        await foreach (ITransportConnection connection in transport.AcceptAsync(token))
        {
            return connection;
        }

        throw new InvalidOperationException("disposed before a peer connected");
    }
}
