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
            EntityId = 3
        });
        sender.RemoteConnection.tick();

        Assert.Equal(3, Assert.IsType<EntityDestroyMessage>(Assert.Single(handler.Received)).EntityId);
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
}
