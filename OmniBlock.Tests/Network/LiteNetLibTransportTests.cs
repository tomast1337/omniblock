using System.Net;
using OmniBlock.Network.Transport;

namespace OmniBlock.Tests.Network;

/// <summary>
///     The <see cref="ITransport" /> seam, exercised over a real loopback UDP socket.
///     <para>
///         Deliberately not mocked. The interface exists to be swapped, and its whole value is that
///         a second implementation can be checked against the same behaviour — which requires the
///         behaviour to be stated in terms a real socket has to satisfy. A mock would only assert
///         that the wrapper calls the library.
///     </para>
///     <para>
///         Every test binds port 0, so the OS assigns free ports and parallel runs cannot collide.
///     </para>
/// </summary>
public sealed class LiteNetLibTransportTests
{
    /// <summary>Generous: this is a real handshake over a real socket, not a function call.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Polls until a datagram arrives, since delivery is asynchronous to the caller.</summary>
    private static async Task<ReceivedDatagram> ReceiveAsync(ITransportConnection connection)
    {
        var deadline = DateTime.UtcNow + Timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (connection.TryReceive(out var datagram))
            {
                return datagram;
            }

            await Task.Delay(5);
        }

        throw new TimeoutException("No datagram arrived within the timeout.");
    }

    private static async Task<List<ReceivedDatagram>> ReceiveAsync(ITransportConnection connection, int count)
    {
        List<ReceivedDatagram> received = [];
        var deadline = DateTime.UtcNow + Timeout;

        while (received.Count < count && DateTime.UtcNow < deadline)
        {
            if (connection.TryReceive(out var datagram))
            {
                received.Add(datagram);
                continue;
            }

            await Task.Delay(5);
        }

        return received;
    }

    [Fact]
    public async Task A_client_and_server_complete_a_handshake()
    {
        await using var pair = await Pair.ConnectAsync();

        Assert.True(pair.ClientSide.IsConnected);
        Assert.True(pair.ServerSide.IsConnected);
        Assert.NotNull(pair.ClientSide.RemoteEndPoint);
    }

    /// <summary>
    ///     <see cref="ITransport.ConnectAsync" /> must not complete until the peer is usable.
    ///     LiteNetLib hands back a peer the instant Connect is called, and sends on it before the
    ///     handshake lands are dropped without error — a connection whose first sends vanish is a
    ///     far worse failure than one that takes longer to open.
    /// </summary>
    [Fact]
    public async Task The_connection_is_usable_the_moment_connect_returns()
    {
        await using var pair = await Pair.ConnectAsync();

        pair.ClientSide.Send(0, DeliveryMode.ReliableOrdered, [1, 2, 3]);

        Assert.Equal([1, 2, 3], (await ReceiveAsync(pair.ServerSide)).Payload);
    }

    [Theory]
    [InlineData(DeliveryMode.Unreliable)]
    [InlineData(DeliveryMode.UnreliableSequenced)]
    [InlineData(DeliveryMode.Reliable)]
    [InlineData(DeliveryMode.ReliableOrdered)]
    public async Task Every_delivery_mode_carries_a_payload_over_loopback(DeliveryMode mode)
    {
        // Loopback loses nothing, so this checks that each mode is wired to a real LiteNetLib method
        // and round trips — not that the unreliable ones are reliable.
        await using var pair = await Pair.ConnectAsync();

        pair.ClientSide.Send(0, mode, [0xAA, 0xBB]);

        Assert.Equal([0xAA, 0xBB], (await ReceiveAsync(pair.ServerSide)).Payload);
    }

    [Fact]
    public async Task Traffic_flows_in_both_directions()
    {
        await using var pair = await Pair.ConnectAsync();

        pair.ServerSide.Send(0, DeliveryMode.ReliableOrdered, [0x01]);
        pair.ClientSide.Send(0, DeliveryMode.ReliableOrdered, [0x02]);

        Assert.Equal([0x01], (await ReceiveAsync(pair.ClientSide)).Payload);
        Assert.Equal([0x02], (await ReceiveAsync(pair.ServerSide)).Payload);
    }

    /// <summary>
    ///     The channel a payload was sent on has to survive, or the receiver cannot tell bulk
    ///     traffic from game state and the ordering domains are pointless.
    /// </summary>
    [Fact]
    public async Task The_channel_survives_the_round_trip()
    {
        await using var pair = await Pair.ConnectAsync();

        pair.ClientSide.Send(3, DeliveryMode.ReliableOrdered, [0x42]);

        var datagram = await ReceiveAsync(pair.ServerSide);

        Assert.Equal(3, datagram.Channel);
        Assert.Equal([0x42], datagram.Payload);
    }

    [Fact]
    public async Task A_channel_beyond_the_transports_count_is_refused()
    {
        // Caught here rather than inside LiteNetLib, where it is an index out of range on a peer's
        // internal array and says nothing about what the caller did wrong.
        await using var pair = await Pair.ConnectAsync();

        Assert.Throws<ArgumentOutOfRangeException>(() => pair.ClientSide.Send(LiteNetLibTransport.Channels, DeliveryMode.ReliableOrdered, [0]));
    }

    /// <summary>
    ///     Ordering holds within a channel. This is what <c>ReliableOrdered</c> buys and what the
    ///     inventory and chat traffic will depend on.
    /// </summary>
    [Fact]
    public async Task Reliable_ordered_payloads_arrive_in_order_on_their_channel()
    {
        await using var pair = await Pair.ConnectAsync();

        for (byte i = 0; i < 32; i++)
        {
            pair.ClientSide.Send(1, DeliveryMode.ReliableOrdered, [i]);
        }

        var received = await ReceiveAsync(pair.ServerSide, 32);

        Assert.Equal(32, received.Count);
        Assert.Equal(Enumerable.Range(0, 32).Select(i => (byte)i), received.Select(d => d.Payload[0]));
    }

    /// <summary>
    ///     A Beta chunk is 81,920 bytes raw and the payload MTU is around 1,200, so this is roughly
    ///     seventy fragments. It working at all is why chunks can ship over this transport before a
    ///     dedicated chunk transfer protocol exists; it being a single reliable-ordered blob, where
    ///     one lost fragment stalls the rest, is why that protocol is still wanted.
    /// </summary>
    [Fact]
    public async Task A_chunk_sized_payload_is_fragmented_and_reassembled()
    {
        await using var pair = await Pair.ConnectAsync();

        var chunk = new byte[81_920];
        Random.Shared.NextBytes(chunk);

        pair.ServerSide.Send(4, DeliveryMode.ReliableOrdered, chunk);

        var received = await ReceiveAsync(pair.ClientSide);

        Assert.Equal(chunk.Length, received.Payload.Length);
        Assert.Equal(chunk, received.Payload);
    }

    /// <summary>
    ///     Reported rather than asserted precisely: on loopback the round trip is zero or close to
    ///     it, so the property worth pinning is that the numbers are present and sane, not their
    ///     value.
    /// </summary>
    [Fact]
    public async Task Stats_report_a_usable_mtu_and_a_non_negative_round_trip()
    {
        await using var pair = await Pair.ConnectAsync();

        var stats = pair.ClientSide.Stats;

        Assert.True(stats.Mtu > 0, $"MTU was {stats.Mtu}");
        Assert.True(stats.RoundTripMs >= 0, $"round trip was {stats.RoundTripMs}");
    }

    [Fact]
    public async Task Closing_one_end_disconnects_the_other()
    {
        await using var pair = await Pair.ConnectAsync();

        pair.ClientSide.Close(DisconnectReason.Local);
        Assert.False(pair.ClientSide.IsConnected);

        var deadline = DateTime.UtcNow + Timeout;
        while (pair.ServerSide.IsConnected && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.False(pair.ServerSide.IsConnected);
    }

    /// <summary>
    ///     A peer presenting the wrong key is refused during the handshake. Not a security control —
    ///     the key travels in the clear — but it means an unrelated service on a neighbouring port
    ///     is rejected outright instead of confusing something further up the stack.
    /// </summary>
    [Fact]
    public async Task A_peer_with_the_wrong_connection_key_is_refused()
    {
        await using LiteNetLibTransport server = new("expected-key");
        server.Listen(0);

        await using LiteNetLibTransport client = new("wrong-key");
        client.StartClient();

        using CancellationTokenSource cancellation = new(Timeout);

        await Assert.ThrowsAnyAsync<Exception>(async () => await client.ConnectAsync(
            new IPEndPoint(IPAddress.Loopback, server.LocalPort), cancellation.Token));
    }

    [Fact]
    public async Task A_server_accepts_several_peers()
    {
        await using LiteNetLibTransport server = new();
        server.Listen(0);

        using CancellationTokenSource cancellation = new(Timeout);
        IPEndPoint endPoint = new(IPAddress.Loopback, server.LocalPort);

        List<ITransportConnection> accepted = [];
        var accepting = Task.Run(async () =>
        {
            await foreach (var connection in server.AcceptAsync(cancellation.Token))
            {
                accepted.Add(connection);
                if (accepted.Count == 3)
                {
                    return;
                }
            }
        }, cancellation.Token);

        await using LiteNetLibTransport a = new();
        await using LiteNetLibTransport b = new();
        await using LiteNetLibTransport c = new();

        foreach (var client in (LiteNetLibTransport[])[a, b, c])
        {
            client.StartClient();
            await client.ConnectAsync(endPoint, cancellation.Token);
        }

        await accepting;

        Assert.Equal(3, accepted.Count);
        Assert.All(accepted, connection => Assert.True(connection.IsConnected));
    }

    /// <summary>
    ///     A connected pair. Returned server-side connection first, since most assertions are about
    ///     what arrives rather than what is sent.
    /// </summary>
    private sealed class Pair : IAsyncDisposable
    {
        public required LiteNetLibTransport ServerTransport { get; init; }
        public required LiteNetLibTransport ClientTransport { get; init; }
        public required ITransportConnection ServerSide { get; init; }
        public required ITransportConnection ClientSide { get; init; }

        public async ValueTask DisposeAsync()
        {
            await ClientTransport.DisposeAsync();
            await ServerTransport.DisposeAsync();
        }

        public static async Task<Pair> ConnectAsync()
        {
            LiteNetLibTransport server = new();
            server.Listen(0);

            LiteNetLibTransport client = new();
            client.StartClient();

            using CancellationTokenSource cancellation = new(Timeout);

            // Started before connecting: the accept is what completes second, and awaiting it only
            // after the connect returns would race the enumerator's subscription.
            var accepting = FirstAcceptedAsync(server, cancellation.Token);

            var clientSide = await client.ConnectAsync(
                new IPEndPoint(IPAddress.Loopback, server.LocalPort), cancellation.Token);

            return new Pair
            {
                ServerTransport = server,
                ClientTransport = client,
                ServerSide = await accepting,
                ClientSide = clientSide
            };
        }

        private static async Task<ITransportConnection> FirstAcceptedAsync(
            ITransport transport, CancellationToken cancellationToken)
        {
            await foreach (var connection in transport.AcceptAsync(cancellationToken))
            {
                return connection;
            }

            throw new InvalidOperationException("The transport was disposed before a peer connected.");
        }
    }
}
