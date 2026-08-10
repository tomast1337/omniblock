using OmniBlock.Network;
using OmniBlock.Network.Messages;

namespace OmniBlock.Tests.Network;

/// <summary>
///     The server-health heartbeat behind the debug overlay's Server Info panel, which read N/A on
///     every remote session because nothing pushed the numbers it wanted.
/// </summary>
public sealed class ServerStatusMessageTests
{
    private static ServerStatusMessage RoundTrip(ServerStatusMessage message)
    {
        using MemoryStream stream = new();
        message.Write(stream);
        stream.Position = 0;

        ServerStatusMessage read = new();
        read.Read(stream);

        Assert.Equal(stream.Length, stream.Position);
        Assert.Equal(stream.Length, message.Size());
        return read;
    }

    [Fact]
    public void Carries_every_field_the_panel_shows()
    {
        ServerStatusMessage read = RoundTrip(new ServerStatusMessage
        {
            Tps = 19.75f,
            Mspt = 42.5f,
            EntityCount = 1337,
            PlayerCount = 7
        });

        Assert.Equal(19.75f, read.Tps);
        Assert.Equal(42.5f, read.Mspt);
        Assert.Equal(1337, read.EntityCount);
        Assert.Equal(7, read.PlayerCount);
    }

    /// <summary>
    ///     A struggling server is exactly when somebody opens this panel, so the numbers that say so
    ///     have to survive the encoding rather than being clamped into a healthy-looking range.
    /// </summary>
    [Fact]
    public void Survives_a_server_that_is_not_keeping_up()
    {
        ServerStatusMessage read = RoundTrip(new ServerStatusMessage
        {
            Tps = 0.0f,
            Mspt = 2000.0f,
            EntityCount = 0,
            PlayerCount = 0
        });

        Assert.Equal(0.0f, read.Tps);
        Assert.Equal(2000.0f, read.Mspt);
    }

    /// <summary>
    ///     Safe to overtake world data because nothing in it refers to world data: unlike a block
    ///     update or a server-sent position, it cannot be applied to a chunk the client does not
    ///     have. Left at Normal it queues behind the chunk stream, and a join saturates that for
    ///     longer than the overlay's staleness window.
    /// </summary>
    [Fact]
    public void Is_not_held_behind_the_chunk_stream()
    {
        Assert.Equal(SendPriority.High, new ServerStatusMessage().Priority);
    }

    [Fact]
    public void Is_registered_so_both_peers_agree_on_its_id()
    {
        MessageRegistry registry = new();
        DefaultMessages.RegisterAll(registry);
        registry.NegotiateAsServer();

        int id = registry.GetId(new ServerStatusMessage().Key);

        Assert.True(id >= 0);
        Assert.IsType<ServerStatusMessage>(registry.Create(id));
    }
}
