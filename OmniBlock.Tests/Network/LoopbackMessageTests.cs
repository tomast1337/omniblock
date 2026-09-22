using OmniBlock.Entities;
using OmniBlock.Network;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;

namespace OmniBlock.Tests.Network;

/// <summary>
///     Loopback must not serialise.
///     <para>
///         Singleplayer runs the whole game through <c>InternalConnection</c>, which hands packets
///         over as objects. Migrating a packet to a message would have quietly taken that away —
///         <c>OmniMessagePacket.For</c> serialises unconditionally — and at one message per tracked
///         entity per tick, that is an encode and a matching parse of the entire entity set every
///         tick, on a connection with no wire. These tests are what stop it coming back.
///     </para>
/// </summary>
public sealed class LoopbackMessageTests
{
    private static (InternalConnection Sender, Recorder Handler) Pair()
    {
        Recorder handler = new();
        InternalConnection receiver = new(handler, "receiver");
        InternalConnection sender = new(null, "sender");

        sender.AssignRemote(receiver);

        return (sender, handler);
    }

    private static MessageRegistry Negotiated()
    {
        MessageRegistry registry = new();
        DefaultMessages.RegisterAll(registry, ContentRuntime.Current.Items);
        registry.NegotiateAsServer();
        return registry;
    }

    [Fact]
    public void A_message_arrives_as_the_very_object_that_was_sent()
    {
        var (sender, handler) = Pair();
        EntityMoveMessage sent = new()
        {
            EntityId = 99,
            Mask = EntityMoveMessage.Field.Moved,
            DeltaX = 5
        };

        sender.sendMessage(Negotiated(), sent);
        sender.RemoteConnection.tick();

        // Reference equality, deliberately. An equal-but-distinct instance would mean it went
        // through bytes, which is the cost this path exists to avoid.
        Assert.Same(sent, Assert.Single(handler.Received));
    }

    /// <summary>
    ///     A message the local registry has never heard of still travels on loopback. There is no ID
    ///     to look up because no ID is ever written, which is what makes this path independent of
    ///     negotiation having happened at all.
    /// </summary>
    [Fact]
    public void An_unregistered_message_still_travels_on_loopback()
    {
        var (sender, handler) = Pair();

        sender.sendMessage(new MessageRegistry(), new EntityDestroyMessage
        {
            EntityId = 3,
            Reason = EntityRemovalReason.DistanceDespawn
        });
        sender.RemoteConnection.tick();

        var received = Assert.IsType<EntityDestroyMessage>(Assert.Single(handler.Received));
        Assert.Equal(3, received.EntityId);
        Assert.Equal(EntityRemovalReason.DistanceDespawn, received.Reason);
    }

    [Fact]
    public void Bulk_messages_wait_for_gameplay_on_loopback_too()
    {
        var (sender, handler) = Pair();
        TerrainLodTileStatusMessage bulk = new();
        EntityMoveMessage gameplay = new() { EntityId = 7 };

        // Deliberately enqueue bulk first: lane priority, not call order, must decide which is
        // applied first. Loopback has no UDP channels, so InternalConnection emulates the contract
        // with an isolated application queue.
        sender.sendMessage(Negotiated(), bulk);
        sender.sendMessage(Negotiated(), gameplay);

        Assert.Equal(1, sender.getWorldPacketBacklog());
        Assert.Equal(1, sender.getBulkPacketBacklog());

        sender.RemoteConnection.tick();
        Assert.Equal(2, handler.Received.Count);
        Assert.Same(gameplay, handler.Received[0]);
        Assert.Same(bulk, handler.Received[1]);
    }

    [Fact]
    public void Bulk_drain_has_a_finite_loopback_catch_up_ceiling()
    {
        var (sender, handler) = Pair();
        var bulk = Enumerable.Range(0, InternalConnection.MaximumBulkPacketsPerTick + 1)
            .Select(static _ => new TerrainLodTileStatusMessage())
            .ToArray();
        foreach (var message in bulk) sender.sendMessage(Negotiated(), message);

        Assert.Equal(bulk.Length, sender.RemoteConnection.BulkReadQueueDepth);
        Assert.Equal(bulk.Length, sender.RemoteConnection.PeakBulkReadQueueDepth);

        sender.RemoteConnection.tick();

        Assert.Equal(InternalConnection.MaximumBulkPacketsPerTick, handler.Received.Count);
        Assert.Same(bulk[0], handler.Received[0]);
        Assert.Same(bulk[1], handler.Received[1]);
        Assert.Equal(1, sender.getBulkPacketBacklog());
        Assert.Equal(1, sender.RemoteConnection.BulkReadQueueDepth);
        Assert.Equal(bulk.Length, sender.RemoteConnection.PeakBulkReadQueueDepth);
    }

    [Fact]
    public void Bulk_gets_a_bounded_turn_after_gameplay_uses_its_time_budget()
    {
        ManualClock clock = new();
        Recorder handler = new();
        InternalConnection receiver = new(handler, "receiver", clock);
        InternalConnection sender = new(null, "sender");
        sender.AssignRemote(receiver);
        CountingPacket.Applied = 0;

        // The first normal packet consumes the entire normal-lane budget, leaving the second one
        // queued. Bulk must still receive its separate bounded turn in this tick; waiting for the
        // normal queue to become empty starves LOD throughout initial chunk streaming.
        sender.sendPacket(new CountingPacket(clock, Connection.DrainBudgetMs));
        sender.sendPacket(new CountingPacket(clock, 0));
        TerrainLodTileStatusMessage bulk = new();
        sender.sendMessage(Negotiated(), bulk);

        receiver.tick();

        Assert.Equal(1, CountingPacket.Applied);
        Assert.Same(bulk, Assert.Single(handler.Received));
        Assert.Equal(1, sender.getWorldPacketBacklog());
        Assert.Equal(0, sender.getBulkPacketBacklog());
    }

    /// <summary>
    ///     A real connection does serialise, and the receiving side gets a distinct instance. Stated
    ///     here so the loopback assertion above reads as a property of that transport rather than of
    ///     the message layer.
    /// </summary>
    [Fact]
    public void A_serialised_envelope_carries_no_live_message()
    {
        var registry = Negotiated();

        Assert.Null(OmniMessagePacket.For(registry, new EntityMoveMessage())!.Carried);
    }

    private sealed class Recorder : NetHandler
    {
        public List<Message> Received { get; } = [];

        public override bool isServerSide() => true;

        public override void onMessage(Message message) => Received.Add(message);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _microseconds;

        public override long TimestampFrequency => 1_000_000;

        public override long GetTimestamp() => _microseconds;

        public void Advance(double milliseconds) =>
            _microseconds += (long)(milliseconds * 1000.0);
    }

    private sealed class CountingPacket(ManualClock clock, double costMs) : Packet(PacketId.Handshake)
    {
        public static int Applied;

        public override void Read(Stream stream)
        {
        }

        public override void Write(Stream stream)
        {
        }

        public override int Size() => 0;

        public override void Apply(NetHandler handler)
        {
            Applied++;
            clock.Advance(costMs);
        }
    }
}
