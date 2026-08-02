using BetaSharp.Network;
using BetaSharp.Network.Messages;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Transport;

namespace BetaSharp.Tests.Network;

/// <summary>
///     Which ordering domain a packet travels in.
///     <para>
///         The classification began as two send queues on the stream transport, where the best it
///         could do was let a waiting packet go next — a chunk already being written still had to
///         finish. On UDP the same rule selects a channel, and channels are genuinely independent,
///         so a stalled chunk does not delay entity updates at all. The rule did not change because
///         the mechanism did: it answers "what may safely overtake world data", and that answer is
///         a property of the packets.
///     </para>
/// </summary>
public sealed class SendPriorityTests
{
    /// <summary>Wraps a message the way the send path does, so its declared priority is what is read.</summary>
    private static OmniMessagePacket Envelope(Message message)
    {
        MessageRegistry registry = new();
        DefaultMessages.RegisterAll(registry);
        registry.NegotiateAsServer();

        return OmniMessagePacket.For(registry, message)!;
    }

    private static UdpConnection Connected(FakeTransportConnection transport, bool capable = true)
    {
        UdpConnection connection = new(transport) { betaSharpClient = capable };
        return connection;
    }

    [Fact]
    public void Entity_and_timing_packets_are_high_priority()
    {
        Assert.Equal(SendPriority.High, PacketPriorities.Of(Packet.Get(PacketId.LivingEntitySpawnS2C)));
        Assert.Equal(SendPriority.High, PacketPriorities.Of(Packet.Get(PacketId.KeepAlive)));

        // The per-entity updates left the allowlist when they left PacketId. Their priority now
        // comes from the message declaring it, which is the extensibility the table could not give:
        // a mod says its message is latency-sensitive rather than hoping for a slot in this switch.
        Assert.Equal(SendPriority.High, PacketPriorities.Of(Envelope(new EntityMoveMessage())));
        Assert.Equal(SendPriority.High, PacketPriorities.Of(Envelope(new EntityDestroyMessage())));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Envelope(new ChunkDataMessage())));
    }

    /// <summary>
    ///     The safety property, and the reason this is an allowlist rather than "everything except
    ///     chunks". A block update that overtakes the chunk it edits is applied to a chunk the
    ///     client does not have and is silently lost; a server-sent player position that overtakes
    ///     the login chunk batch places the player in unloaded terrain. Both have to stay in the
    ///     same ordering domain as chunk data.
    /// </summary>
    [Fact]
    public void World_data_and_anything_ordered_against_it_stays_normal()
    {
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.ChunkDataS2C)));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.BlockUpdateS2C)));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.ChunkDeltaUpdateS2C)));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.ChunkStatusUpdateS2C)));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.PlayerMoveFull)));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.MapUpdateS2C)));
        Assert.Equal(SendPriority.Normal, PacketPriorities.Of(Packet.Get(PacketId.ChatMessage)));
    }

    /// <summary>
    ///     Every extensible-layer message shares one packet ID, so the ID cannot say how urgent one
    ///     is. A clock probe and a mod's bulk transfer arrive here as the same packet type and must
    ///     still be routed differently.
    /// </summary>
    [Fact]
    public void A_message_envelope_takes_its_priority_from_the_message()
    {
        Assert.Equal(
            SendPriority.High,
            PacketPriorities.Of(OmniMessagePacket.Get(0, [], carriesSendTime: false, SendPriority.High)));
        Assert.Equal(
            SendPriority.Normal,
            PacketPriorities.Of(OmniMessagePacket.Get(0, [], carriesSendTime: false, SendPriority.Normal)));
    }

    [Fact]
    public void The_migrated_time_sync_messages_are_high_priority()
    {
        Assert.Equal(SendPriority.High, new TimeSyncRequestMessage().Priority);
        Assert.Equal(SendPriority.High, new TimeSyncResponseMessage().Priority);
        Assert.Equal(SendPriority.High, new TickStampMessage().Priority);
    }

    // ---- channel assignment ----

    [Fact]
    public void Entity_and_timing_packets_take_the_state_channel()
    {
        Assert.Equal(UdpConnection.StateChannel, UdpConnection.ChannelFor(Envelope(new EntityMoveMessage())));
        Assert.Equal(UdpConnection.StateChannel, UdpConnection.ChannelFor(Packet.Get(PacketId.LivingEntitySpawnS2C)));
        Assert.Equal(UdpConnection.StateChannel, UdpConnection.ChannelFor(Packet.Get(PacketId.KeepAlive)));
    }

    /// <summary>
    ///     Chunks and block updates must share a channel, or a block update can overtake the chunk
    ///     it edits — ordering holds within a channel and never across one.
    /// </summary>
    [Fact]
    public void World_data_and_block_updates_share_the_ordered_channel()
    {
        Assert.Equal(UdpConnection.OrderedChannel, UdpConnection.ChannelFor(Packet.Get(PacketId.ChunkDataS2C)));
        Assert.Equal(UdpConnection.OrderedChannel, UdpConnection.ChannelFor(Packet.Get(PacketId.BlockUpdateS2C)));
        Assert.Equal(UdpConnection.OrderedChannel, UdpConnection.ChannelFor(Packet.Get(PacketId.ChatMessage)));
    }

    [Fact]
    public void A_packet_goes_out_on_the_channel_its_class_selects()
    {
        FakeTransportConnection transport = new();
        UdpConnection connection = Connected(transport);

        connection.sendPacket(Packet.Get(PacketId.ChunkDataS2C));
        connection.sendPacket(Envelope(new EntityMoveMessage()));

        Assert.Equal(
            [UdpConnection.OrderedChannel, UdpConnection.StateChannel],
            transport.Sent.Select(s => s.Channel));
    }

    /// <summary>
    ///     Reliable and ordered for everything, which is the phase 2 position and not the end state.
    ///     Sending entity updates sequenced is the obvious next move and would be wrong today:
    ///     sequenced keeps only the newest payload on the channel, so one entity's update would
    ///     discard another's and spawns would be dropped outright.
    /// </summary>
    [Fact]
    public void Everything_is_sent_reliably_and_in_order_for_now()
    {
        FakeTransportConnection transport = new();
        UdpConnection connection = Connected(transport);

        connection.sendPacket(Envelope(new EntityMoveMessage()));
        connection.sendPacket(Packet.Get(PacketId.ChunkDataS2C));

        Assert.All(transport.Sent, sent => Assert.Equal(DeliveryMode.ReliableOrdered, sent.Mode));
    }

    /// <summary>
    ///     Order within a channel is exactly what it was, which is what keeps a move from overtaking
    ///     its own spawn. Only the two classes are independent of each other.
    /// </summary>
    [Fact]
    public void Order_within_a_channel_is_preserved()
    {
        FakeTransportConnection transport = new();
        UdpConnection connection = Connected(transport);

        connection.sendPacket(Packet.Get(PacketId.LivingEntitySpawnS2C));
        connection.sendPacket(Packet.Get(PacketId.ChunkDataS2C));
        connection.sendPacket(Envelope(new EntityMoveMessage()));
        connection.sendPacket(Envelope(new EntityDestroyMessage()));

        // Both messages share the envelope's packet ID, so the assertion is on how many landed on
        // the state channel and in which order relative to the spawn, not on distinguishable bytes.
        Assert.Equal(
            [(byte)PacketId.LivingEntitySpawnS2C, (byte)PacketId.OmniMessage, (byte)PacketId.OmniMessage],
            transport.Sent.Where(s => s.Channel == UdpConnection.StateChannel).Select(s => s.Payload[0]));
    }
}
