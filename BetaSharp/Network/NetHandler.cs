using BetaSharp.Network.Messages;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Packets.Play;
using BetaSharp.Network.Packets.S2CPlay;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Network;

public abstract class NetHandler
{
    private static readonly ILogger<NetHandler> s_logger = Log.Instance.For<NetHandler>();

    /// <summary>Keys already reported as unknown, so a repeating message logs once, not per packet.</summary>
    private readonly HashSet<int> _reportedUnknownMessages = [];

    public abstract bool isServerSide();

    /// <summary>
    ///     The session's negotiated message table, or null on a handler that does not participate in
    ///     the extensible layer. Null makes <see cref="onOmniMessage" /> a no-op drop, which is the
    ///     correct behaviour during login, before negotiation has happened.
    /// </summary>
    public virtual MessageRegistry? Messages => null;

    public virtual void handleChunkData(ChunkDataS2CPacket packet)
    {
    }

    /// <summary>
    ///     Adopts the server's message ordering. Client side; a server receiving this is a protocol
    ///     error and ignores it.
    /// </summary>
    public virtual void onMessageRegistrySync(MessageRegistrySyncS2CPacket packet)
    {
        if (isServerSide())
        {
            return;
        }

        Messages?.AdoptOrdering(packet.Keys);
    }

    /// <summary>
    ///     Resolves an envelope against the negotiated table and dispatches it to
    ///     <see cref="onMessage" />.
    ///     <para>
    ///         The unknown-message path is the reason this layer exists, so it lives here rather
    ///         than in each subclass: an ID this peer cannot decode is logged once and dropped. The
    ///         envelope already consumed exactly its declared length, so the stream stays aligned
    ///         and the connection survives — which is precisely what the legacy byte-ID framing
    ///         cannot offer.
    ///     </para>
    /// </summary>
    public virtual void onOmniMessage(OmniMessagePacket packet)
    {
        // Loopback: the sender handed the object over rather than bytes, so there is nothing to
        // resolve and nothing to parse. Skipping both is the point — see OmniMessagePacket.Carried.
        if (packet.Carried is { } carried)
        {
            onMessage(carried);
            return;
        }

        MessageRegistry? registry = Messages;
        if (registry is null || !registry.Negotiated)
        {
            return;
        }

        Message? message = registry.Create(packet.MessageId);
        if (message is null)
        {
            if (_reportedUnknownMessages.Add(packet.MessageId))
            {
                s_logger.LogInformation(
                    "Dropping unknown message id {Id} ({Key}); this peer does not implement it. Further occurrences are not logged.",
                    packet.MessageId,
                    registry.GetKey(packet.MessageId)?.ToString() ?? "not in table");
            }

            return;
        }

        // Transport timing rides on the envelope rather than in the payload, so it is transferred
        // before the message is handed on. Anything that measures the network needs values taken at
        // the transport edge; a payload field would be serialised before the moment it describes.
        message.TransportSentAtMs = packet.SentAtMs;
        message.TransportReceivedAtMs = packet.ReceivedAtMs;

        try
        {
            using MemoryStream payload = new(packet.Payload, writable: false);
            message.Read(payload);
        }
        catch (Exception e) when (e is InvalidDataException or EndOfStreamException or ArgumentException)
        {
            // A malformed payload is contained: the envelope bounded it, so only this message is
            // lost rather than the connection.
            s_logger.LogWarning(e, "Malformed payload for message {Key}; dropped.", message.Key);
            return;
        }

        onMessage(message);
    }

    /// <summary>
    ///     Where this peer declares which messages it wants. Populated by the subclass, and open to
    ///     content and mods for the same reason the registry is: nobody has to edit a switch in the
    ///     engine to receive a message they defined.
    /// </summary>
    public MessageDispatcher MessageHandlers { get; } = new();

    /// <summary>
    ///     Handles a decoded message. Virtual for the rare handler that wants to see everything;
    ///     the ordinary way to receive one is to register on <see cref="MessageHandlers" />.
    /// </summary>
    public virtual void onMessage(Message message) => MessageHandlers.Dispatch(message);

    public virtual void handle(Packet packet)
    {
    }

    public virtual void onDisconnected(string reason, object[]? details)
    {
    }

    public virtual void onDisconnect(DisconnectPacket packet)
    {
        handle(packet);
    }

    public virtual void onHello(LoginHelloPacket packet)
    {
        handle(packet);
    }

    public virtual void onPlayerMove(PacketPlayerMoveAbstract packet)
    {
        handle(packet);
    }

    public virtual void onChunkDeltaUpdate(ChunkDeltaUpdateS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onBlockUpdate(BlockUpdateS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onChunkStatusUpdate(ChunkStatusUpdateS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onPlayerSpawn(PlayerSpawnS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onItemEntitySpawn(ItemEntitySpawnS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onChatMessage(ChatMessagePacket packet)
    {
        handle(packet);
    }

    public virtual void onEntitySpawn(EntitySpawnS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onHandshake(HandshakePacket packet)
    {
        handle(packet);
    }

    public virtual void onLivingEntitySpawn(LivingEntitySpawnS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onWorldTimeUpdate(WorldTimeUpdateS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onPlayerSpawnPosition(PlayerSpawnPositionS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onHealthUpdate(HealthUpdateS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onPlayerRespawn(PlayerRespawnPacket packet)
    {
        handle(packet);
    }

    public virtual void onExplosion(ExplosionS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onOpenScreen(OpenScreenS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onCloseScreen(CloseScreenS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onScreenHandlerSlotUpdate(ScreenHandlerSlotUpdateS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onInventory(InventoryS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void handleUpdateSign(UpdateSignPacket packet)
    {
        handle(packet);
    }

    public virtual void onScreenHandlerPropertyUpdate(ScreenHandlerPropertyUpdateS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onScreenHandlerAcknowledgement(ScreenHandlerAcknowledgementPacket packet)
    {
        handle(packet);
    }

    public virtual void onPaintingEntitySpawn(PaintingEntitySpawnS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onPlayNoteSound(PlayNoteSoundS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onPlayerConnectionUpdate(PlayerConnectionUpdateS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onPlayerGameModeUpdate(PlayerGameModeUpdateS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onRegistryData(RegistryDataS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onFinishConfiguration(FinishConfigurationS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onIncreaseStat(IncreaseStatS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onPlayerSleepUpdate(PlayerSleepUpdateS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onGameStateChange(GameStateChangeS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onLightningEntitySpawn(GlobalEntitySpawnS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onMapUpdate(MapUpdateS2CPacket packet)
    {
        handle(packet);
    }

    public virtual void onWorldEvent(WorldEventS2CPacket packet)
    {
        handle(packet);
    }
}
