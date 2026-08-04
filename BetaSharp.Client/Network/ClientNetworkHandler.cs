using System.Net;
using System.Net.Sockets;
using BetaSharp.Blocks;
using BetaSharp.Blocks.Entities;
using BetaSharp.Client.Diagnostics;
using BetaSharp.Client.Entities;
using BetaSharp.Client.Entities.FX;
using BetaSharp.Client.Rendering.Entities;
using BetaSharp.Client.Rendering.Particles;
using BetaSharp.Client.Worlds;
using BetaSharp.Diagnostics;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Inventories;
using BetaSharp.Items;
using BetaSharp.Items.Behaviors;
using BetaSharp.Network;
using BetaSharp.Network.Chunks;
using BetaSharp.Network.Messages;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Snapshots;
using BetaSharp.Network.Transport;
using BetaSharp.Registries;
using BetaSharp.Screens;
using BetaSharp.Stats;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core;
using BetaSharp.Worlds.Mechanics;
using BetaSharp.Worlds.Storage;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Client.Network;

public class ClientNetworkHandler : NetHandler
{
    private readonly ILogger<ClientNetworkHandler> _logger = Log.Instance.For<ClientNetworkHandler>();

    public bool Disconnected { get; private set; }
    private readonly Connection _netManager;
    public string StatusMessage;
    private readonly ClientNetworkContext _context;
    private ClientWorld _worldClient;
    private bool _terrainLoaded;
    public PersistentStateManager ClientPersistentStateManager { get; } = new(null);
    private readonly JavaRandom _rand = new();

    private int _ticks;
    private int _lastKeepAliveTime;

    private readonly ClientRegistryAccess _clientRegistries = new();

    /// <summary>
    ///     This connection's message table. Populated locally at construction, then re-ordered to
    ///     match the server's when <c>MessageRegistrySyncS2CPacket</c> arrives during configuration.
    ///     Per-connection rather than static, since two servers may advertise different tables.
    /// </summary>
    public override MessageRegistry? Messages { get; } = BuildMessageRegistry();

    /// <summary>
    ///     Registers the same set the server does. Both sides go through
    ///     <see cref="DefaultMessages" /> rather than keeping two lists, because a divergence is
    ///     silent in both directions — a key the server advertises and this peer lacks becomes a
    ///     hole and its messages are dropped, and a key registered only here can never be sent.
    /// </summary>
    private static MessageRegistry BuildMessageRegistry()
    {
        MessageRegistry registry = new();
        DefaultMessages.RegisterAll(registry);
        return registry;
    }

    /// <summary>
    ///     Synchronised server clock. Null for the loopback path (<see cref="InternalConnection" />),
    ///     where offset is identically zero and there is no jitter to measure. Polled each tick and
    ///     fed from the time-sync response message.
    /// </summary>
    public ServerClock? Clock { get; }

    /// <summary>
    ///     Render-time interpolation for remote entities. Present on every connection, but only
    ///     does anything once the server is stamping batches and the clock has synchronised — see
    ///     <see cref="ShouldInterpolate" />.
    /// </summary>
    public EntityInterpolator Interpolation { get; } = new();

    /// <summary>
    ///     What this client has been told about every entity it can see, and the baseline incoming
    ///     deltas are measured against. See <see cref="ClientSnapshotStream" />.
    /// </summary>
    public ClientSnapshotStream Snapshots { get; } = new();

    /// <summary>
    ///     Whether there is a shared timeline to interpolate against. False on the loopback path
    ///     (no clock), against a server that does not stamp, and during the login burst before the
    ///     first offset lands. In each case the legacy behaviour stays in charge.
    /// </summary>
    public bool ShouldInterpolate =>
        Clock is { Synchronised: true } && CurrentBatchServerTimeMs != 0;

    /// <summary>
    ///     The transport, kept so it can be shut down with the connection. One instance is one
    ///     socket, and a client's serves exactly this peer.
    /// </summary>
    private readonly LiteNetLibTransport? _transport;

    public ClientNetworkHandler(ClientNetworkContext context, string address, int port)
    {
        _context = context;

        IPAddress[] addresses = Dns.GetHostAddresses(address);
        IPEndPoint endPoint = new(
            addresses.FirstOrDefault(a => a.AddressFamily is AddressFamily.InterNetwork) ?? addresses.First(),
            port);

        _transport = new LiteNetLibTransport();
        _transport.StartClient();

        // Blocking, because this constructor already runs on ThreadConnectToServer rather than on
        // the game thread, and the connecting screen is driven by that thread finishing. The wait
        // is bounded so a black hole of an address fails rather than hanging the screen forever.
        using CancellationTokenSource timeout = new(ConnectTimeout);
        ITransportConnection peer = _transport
            .ConnectAsync(endPoint, timeout.Token)
            .AsTask()
            .GetAwaiter()
            .GetResult();

        _netManager = new UdpConnection(peer, this);
        _cacheKey = $"{address}_{port}";

        Clock = new ServerClock();

        RegisterMessageHandlers();
    }

    /// <summary>
    ///     How long to wait for the UDP handshake. Longer than a round trip on any plausible link,
    ///     and short enough that a wrong address or a closed port reports rather than hangs. UDP has
    ///     no equivalent of a TCP connection refusal, so an unreachable peer can only present as a
    ///     timeout.
    /// </summary>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    public ClientNetworkHandler(ClientNetworkContext context, Connection connection)
    {
        _context = context;
        _netManager = connection;

        RegisterMessageHandlers();
    }

    public void Tick()
    {
        if (!Disconnected)
        {
            _netManager.tick();

            MetricRegistry.Set(ClientMetrics.UploadBytes, _netManager.BytesWritten);
            MetricRegistry.Set(ClientMetrics.DownloadBytes, _netManager.BytesRead);
            MetricRegistry.Set(ClientMetrics.UploadPackets, _netManager.PacketsWritten);
            MetricRegistry.Set(ClientMetrics.DownloadPackets, _netManager.PacketsRead);
            MetricRegistry.Set(ClientMetrics.ReadQueueDepth, _netManager.ReadQueueDepth);
            MetricRegistry.Set(ClientMetrics.ReadQueuePeak, _netManager.PeakReadQueueDepth);
            MetricRegistry.Set(ClientMetrics.PacketsProcessed, _netManager.PacketsProcessed);
            MetricRegistry.Set(ClientMetrics.DrainBudgetHits, _netManager.DrainBudgetHits);
            MetricRegistry.Set(ClientMetrics.IsInternal, _netManager is InternalConnection);
            MetricRegistry.Set(ClientMetrics.ServerAddress, _netManager.getAddress()?.ToString() ?? "Unknown");
            MetricRegistry.Set(ClientMetrics.PeerProtocolVersion, _netManager.PeerProtocolVersion);

            PacketArrivalHistogram arrivals = _netManager.ReadIntervals;
            MetricRegistry.Set(ClientMetrics.ReadIntervalSamples, arrivals.Count);
            MetricRegistry.Set(ClientMetrics.ReadIntervalMeanMs, arrivals.MeanMs);
            MetricRegistry.Set(ClientMetrics.ReadIntervalP50Ms, arrivals.PercentileMs(50));
            MetricRegistry.Set(ClientMetrics.ReadIntervalP95Ms, arrivals.PercentileMs(95));
            MetricRegistry.Set(ClientMetrics.ReadIntervalP99Ms, arrivals.PercentileMs(99));
            MetricRegistry.Set(ClientMetrics.ReadIntervalMaxMs, arrivals.MaxMs);

            // The distributions themselves, so the overlay can draw the shape rather than infer it
            // from percentiles. Null on the RTT side for a loopback connection, which has no clock.
            MetricRegistry.Set(ClientMetrics.ArrivalHistogram, arrivals);
            MetricRegistry.Set(ClientMetrics.RttHistogram, Clock?.RttHistogram);

            // Tracks the player so the next join can advertise the right region before the server
            // has said where they are. Two fields in memory; it reaches disk on flush.
            if (_chunkCache is not null && _context.PlayerHost.Player is { } located)
            {
                _chunkCache.LastCentre = new ChunkPos(
                    (int)Math.Floor(located.X) >> 4, (int)Math.Floor(located.Z) >> 4);
            }

            // Drive the time-sync state machine: burst during login, then background pacer.
            PollClock();

            if (Clock is { Synchronised: true })
            {
                MetricRegistry.Set(ClientMetrics.ClockOffsetMs, Clock.OffsetMs);
                MetricRegistry.Set(ClientMetrics.ClockRttMs, Clock.RttMedianMs);
                MetricRegistry.Set(ClientMetrics.ClockJitterMs, Clock.JitterMs);
                MetricRegistry.Set(ClientMetrics.ClockSynchronised, true);

                // The jitter term. Fed from here rather than read by the interpolator because the
                // clock is per-connection and the interpolator is handed one number per tick, which
                // keeps it testable without a synchronised clock to stand up.
                Interpolation.NetworkJitterMs = Clock.JitterMs;

                // Only meaningful once the clock is synchronised: before that, ServerTimeMs is a
                // degenerate guess and the age would be a reading of the offset error, not of how
                // stale the newest batch is.
                if (CurrentBatchServerTimeMs != 0)
                {
                    MetricRegistry.Set(ClientMetrics.TickStampAgeMs, Clock.ServerTimeMs - CurrentBatchServerTimeMs);
                }

                // A step moved the timeline. Every buffered stamp is on the old one, so they are
                // dropped rather than interpolated across — gliding through a correction that large
                // is more visible than cutting to it.
                if (Clock.ConsumeSnapshotFlush())
                {
                    Interpolation.Clear();
                }
            }
            else
            {
                MetricRegistry.Set(ClientMetrics.ClockSynchronised, false);
            }

            Interpolation.Available = ShouldInterpolate;
            EntityInterpolator.Current = Interpolation;

            MetricRegistry.Set(ClientMetrics.InterpolationActive, Interpolation.Active);
            MetricRegistry.Set(ClientMetrics.InterpolationDelayMs, Interpolation.MinAppliedDelayMs);
            MetricRegistry.Set(ClientMetrics.InterpolationDelayMaxMs, Interpolation.MaxAppliedDelayMs);
            MetricRegistry.Set(ClientMetrics.InterpolationTracked, Interpolation.TrackedCount);
            MetricRegistry.Set(ClientMetrics.InterpolationInterpolated, Interpolation.InterpolatedCount);
            MetricRegistry.Set(ClientMetrics.InterpolationExtrapolated, Interpolation.ExtrapolatedCount);
            MetricRegistry.Set(ClientMetrics.InterpolationFrozen, Interpolation.FrozenCount);
            MetricRegistry.Set(ClientMetrics.InterpolationAdjusting, Interpolation.AdjustingCount);
            MetricRegistry.Set(ClientMetrics.InterpolationStarvations, Interpolation.StarvationEvents);

            // One acknowledgement per tick, whether or not a snapshot arrived. Sending only on
            // receipt would stop acknowledging exactly when the stream stalls, which is when the
            // server most needs to know which baseline is still good.
            AcknowledgeSnapshots();

            if (_ticks++ - _lastKeepAliveTime > 200)
            {
                SendMessage(new KeepAliveMessage());
            }
        }
    }

    public void SendPacket(Packet packet)
    {
        if (!Disconnected)
        {
            _netManager.sendPacket(packet);
            _lastKeepAliveTime = _ticks;
        }
    }

    private void PollClock()
    {
        if (Clock is null)
        {
            return;
        }

        (uint Sequence, long ClientSendTime)? probe = Clock.Poll();
        if (probe is not null)
        {
            SendMessage(new TimeSyncRequestMessage
            {
                Sequence = probe.Value.Sequence,
                ClientSendTime = probe.Value.ClientSendTime,
            });
        }
    }

    /// <summary>
    ///     Reports a click on another entity, with the instant this client was rendering that entity
    ///     at so the server can check reach against what the player actually saw.
    ///     <para>
    ///         The render time is per-entity, because the delay is: <c>EntityInterpolator</c> gives a
    ///         player 200 ms and a dropped item two seconds, and sending a connection-wide figure
    ///         would misplace every target that is not on the average update rate.
    ///     </para>
    ///     <para>
    ///         Falls back to the legacy packet whenever the answer would be a guess — a server that
    ///         does not implement the message, or interpolation not actually driving this entity, in
    ///         which case it is drawn at the last position received and the present is the honest
    ///         claim.
    ///     </para>
    /// </summary>
    public void SendInteractEntity(int playerId, int entityId, byte action)
    {
        // Zero when the clock has not synchronised or the target is not being interpolated, which
        // the server reads as "no rewind" rather than as the epoch. That is the honest answer: with
        // no synchronised clock there is no instant to name, and judging such a click against the
        // present is what every click got before the rewind existed.
        long renderTimeMs = Clock is { Synchronised: true } && Interpolation.IsInterpolating(entityId)
            ? Clock.ServerTimeMs - Interpolation.AppliedDelayFor(entityId)
            : 0;

        SendMessage(new InteractEntityMessage
        {
            EntityId = entityId,
            Action = action,
            RenderTimeMs = renderTimeMs,
        });
    }

    /// <summary>
    ///     Tells the server which snapshot the next delta may be measured against.
    ///     <para>
    ///         Skipped until a snapshot has actually been applied. Sequence zero is a request to
    ///         resynchronise, and sending it every tick before the first snapshot arrives would keep
    ///         resetting a stream that has not started.
    ///     </para>
    /// </summary>
    private void AcknowledgeSnapshots()
    {
        if (Snapshots.AppliedSequence == 0)
        {
            return;
        }

        SendMessage(new SnapshotAckMessage { Sequence = Snapshots.AppliedSequence });
    }

    /// <summary>
    ///     Applies a delta-compressed batch of entity positions.
    ///     <para>
    ///         The same destination as the position packets it replaces: <c>TrackedPos*</c> in wire
    ///         units, then <see cref="RetargetEntity" />, which records the snapshot against the
    ///         current batch's server time and hands the entity to whichever movement scheme is
    ///         active. Nothing downstream of that knows which encoding it came from.
    ///     </para>
    /// </summary>
    private void onEntitySnapshot(EntitySnapshotMessage message)
    {
        _snapshotBytes += message.Size();
        MetricRegistry.Set(ClientMetrics.SnapshotBytes, _snapshotBytes);

        foreach ((int entityId, EntitySnapshotState state) in Snapshots.Apply(message))
        {
            _snapshotRecords++;
            Entity? entity = GetEntityById(entityId);
            if (entity is null)
            {
                continue;
            }

            entity.TrackedPosX = state.X;
            entity.TrackedPosY = state.Y;
            entity.TrackedPosZ = state.Z;

            RetargetEntity(entity, state.Yaw * 360 / 256.0F, state.Pitch * 360 / 256.0F);
        }

        MetricRegistry.Set(ClientMetrics.SnapshotRecords, _snapshotRecords);
        MetricRegistry.Set(ClientMetrics.SnapshotsDropped, Snapshots.DroppedSnapshots);
    }

    private long _snapshotRecords;
    private long _snapshotBytes;

    /// <summary>
    ///     Sends a message, or drops it when the server never advertised the key — the designed
    ///     outcome for a peer that does not implement it, not an error.
    /// </summary>
    public void SendMessage(Message message)
    {
        MessageRegistry? registry = Messages;
        if (registry is null || !registry.Negotiated)
        {
            return;
        }

        _netManager.sendMessage(registry, message);
    }

    public override void onMessageRegistrySync(MessageRegistrySyncS2CPacket packet)
    {
        base.onMessageRegistrySync(packet);

        // Closes the capability loop. The client declared its own revision inside the login field on
        // the way out; this is the server answering, and it is the first extended packet the server
        // sends, so it is the earliest the client can know. Without it the client could only infer
        // that the server is capable, from having received anything extended at all.
        _netManager.NotePeerProtocol(packet.ProtocolVersion);
    }

    /// <summary>
    ///     Declares which messages this peer wants. Called from both constructors: the registration
    ///     set is a property of the handler, not of how its connection was made.
    /// </summary>
    private void RegisterMessageHandlers()
    {
        MessageHandlers.On<TimeSyncResponseMessage>(onTimeSyncResponse);
        MessageHandlers.On<TickStampMessage>(onTickStamp);
        MessageHandlers.On<ServerStatusMessage>(onServerStatus);
        MessageHandlers.On<ChunkDataMessage>(onChunkData);
        MessageHandlers.On<RegionDataMessage>(onRegionData);
        MessageHandlers.On<PlayerMoveMessage>(onPlayerMove);
        MessageHandlers.On<PlayerMovePositionMessage>(onPlayerMove);
        MessageHandlers.On<PlayerMoveLookMessage>(onPlayerMove);
        MessageHandlers.On<PlayerMoveFullMessage>(onPlayerMove);
        MessageHandlers.On<ChunkUnchangedMessage>(onChunkUnchanged);
        MessageHandlers.On<EntitySnapshotMessage>(onEntitySnapshot);
        MessageHandlers.On<EntityMoveMessage>(onEntityMove);
        MessageHandlers.On<EntityTeleportMessage>(onEntityTeleport);
        MessageHandlers.On<EntityDestroyMessage>(onEntityDestroy);
        MessageHandlers.On<EntityStatusMessage>(onEntityStatus);
        MessageHandlers.On<EntityVelocityMessage>(onEntityVelocity);
        MessageHandlers.On<EntityVehicleMessage>(onEntityVehicle);
        MessageHandlers.On<EntityDataMessage>(onEntityData);
        MessageHandlers.On<EntityEquipmentMessage>(onEntityEquipment);
        MessageHandlers.On<EntityAnimationMessage>(onEntityAnimation);
        MessageHandlers.On<ItemPickupMessage>(onItemPickup);
        MessageHandlers.On<EntitySpawnMessage>(onEntitySpawn);
        MessageHandlers.On<ItemEntitySpawnMessage>(onItemEntitySpawn);
        MessageHandlers.On<LivingEntitySpawnMessage>(onLivingEntitySpawn);
        MessageHandlers.On<GlobalEntitySpawnMessage>(onGlobalEntitySpawn);
        MessageHandlers.On<PaintingSpawnMessage>(onPaintingSpawn);
        MessageHandlers.On<PlayerSpawnMessage>(onPlayerSpawn);
        MessageHandlers.On<OpenScreenMessage>(onOpenScreen);
        MessageHandlers.On<CloseScreenMessage>(_ => _context.PlayerHost.Player.CloseHandledScreen());
        MessageHandlers.On<InventoryMessage>(onInventory);
        MessageHandlers.On<ScreenHandlerSlotMessage>(onScreenHandlerSlot);
        MessageHandlers.On<ScreenHandlerPropertyMessage>(onScreenHandlerProperty);
        MessageHandlers.On<ScreenHandlerAckMessage>(onScreenHandlerAck);
        MessageHandlers.On<UpdateSignMessage>(onUpdateSign);
        MessageHandlers.On<ChatMessage>(onChatMessage);
        MessageHandlers.On<DisconnectMessage>(onDisconnect);
        MessageHandlers.On<RegistryDataMessage>(onRegistryData);
        MessageHandlers.On<FinishConfigurationMessage>(onFinishConfiguration);
        MessageHandlers.On<HealthUpdateMessage>(onHealthUpdate);
        MessageHandlers.On<PlayerSleepUpdateMessage>(onPlayerSleepUpdate);
        MessageHandlers.On<PlayerSpawnPositionMessage>(onPlayerSpawnPosition);
        MessageHandlers.On<PlayerGameModeUpdateMessage>(onPlayerGameModeUpdate);
        MessageHandlers.On<PlayerConnectionUpdateMessage>(onPlayerConnectionUpdate);
        MessageHandlers.On<GameStateChangeMessage>(onGameStateChange);
        MessageHandlers.On<IncreaseStatMessage>(onIncreaseStat);
        MessageHandlers.On<PlayerRespawnMessage>(onPlayerRespawn);
        MessageHandlers.On<BlockUpdateMessage>(onBlockUpdate);
        MessageHandlers.On<ChunkDeltaUpdateMessage>(onChunkDeltaUpdate);
        MessageHandlers.On<LightSectionsMessage>(onLightSections);
        MessageHandlers.On<ChunkStatusUpdateMessage>(onChunkStatusUpdate);
        MessageHandlers.On<WorldEventMessage>(onWorldEvent);
        MessageHandlers.On<WorldTimeUpdateMessage>(onWorldTimeUpdate);
        MessageHandlers.On<PlayNoteSoundMessage>(onPlayNoteSound);
        MessageHandlers.On<ExplosionMessage>(onExplosion);
        MessageHandlers.On<MapUpdateMessage>(onMapUpdate);
    }

    /// <summary>
    ///     Publishes the server's own health into the metric registry the debug overlay reads.
    ///     <para>
    ///         Writing to the same handles the server writes in singleplayer is deliberate: the
    ///         overlay then has one source to read and does not need to know which kind of session it
    ///         is in. In singleplayer both writers exist and agree, because they are reporting the
    ///         same numbers from the same process.
    ///     </para>
    /// </summary>
    private static void onServerStatus(ServerStatusMessage message)
    {
        MetricRegistry.Set(ServerMetrics.Tps, message.Tps);
        MetricRegistry.Set(ServerMetrics.Mspt, message.Mspt);
        MetricRegistry.Set(ServerMetrics.EntityCount, message.EntityCount);
        MetricRegistry.Set(ServerMetrics.PlayerCount, message.PlayerCount);
    }

    /// <summary>
    ///     The server accepted a hash this client offered, so the chunk is loaded from disk instead
    ///     of the wire.
    ///     <para>
    ///         A miss here is possible and is not an error: the entry can be evicted or the file can
    ///         go bad between the offer and the reply. There is no recovery message, so it is logged
    ///         and the chunk stays absent until something else causes a full send — which is the
    ///         honest failure for a cache, and rare enough not to warrant a protocol round trip.
    ///     </para>
    /// </summary>
    private void onChunkUnchanged(ChunkUnchangedMessage message)
    {
        byte[]? stored = _chunkCache?.Read(new ChunkPos(message.ChunkX, message.ChunkZ));
        byte[]? blob = null;

        if (stored is not null)
        {
            try
            {
                blob = ChunkDataMessage.Decompress(stored);
            }
            catch (InvalidDataException exception)
            {
                // A cache entry that will not decompress is the cache being wrong, which it is
                // allowed to be. Treated as a miss.
                _logger.LogWarning(exception, "Cached chunk {X},{Z} is unreadable.", message.ChunkX, message.ChunkZ);
            }
        }

        if (blob is null)
        {
            _logger.LogWarning(
                "Server says chunk {X},{Z} is unchanged, but it is no longer cached.",
                message.ChunkX, message.ChunkZ);

            return;
        }

        int worldX = message.ChunkX * 16;
        int worldZ = message.ChunkZ * 16;
        _worldClient.ClearBlockResets(
            worldX, 0, worldZ, worldX + 15, ChuckFormat.WorldHeight - 1, worldZ + 15);

        _worldClient.ApplyChunkBlob(message.ChunkX, message.ChunkZ, blob);

        _chunksFromCache++;
        _chunkBytesSaved += blob.Length;
        _ = stored;
        MetricRegistry.Set(ClientMetrics.ChunksFromCache, _chunksFromCache);
        MetricRegistry.Set(ClientMetrics.ChunkCacheBytesSaved, _chunkBytesSaved);
    }

    /// <summary>
    ///     A whole chunk in the palette encoding. The legacy <c>handleChunkData</c> path stays for
    ///     vanilla servers and for the batched region updates <c>ChunkMap</c> still sends as packets.
    /// </summary>
    private void onChunkData(ChunkDataMessage message)
    {
        int worldX = message.ChunkX * 16;
        int worldZ = message.ChunkZ * 16;

        // Pending single-block corrections for this chunk are superseded by a full send, exactly as
        // they are on the legacy path. Leaving them would re-apply a change the chunk already
        // contains, on top of data that is newer than they are.
        _worldClient.ClearBlockResets(
            worldX, 0, worldZ,
            worldX + 15, ChuckFormat.WorldHeight - 1, worldZ + 15);

        byte[] blob = message.Decompress();
        _worldClient.ApplyChunkBlob(message.ChunkX, message.ChunkZ, blob);

        // Stored compressed, exactly as it arrived. The blob is six times larger and we already
        // hold the small version, so decompressing to store it would spend disk to save a
        // decompression that the network path pays anyway.
        //
        // The hash is still over the *decoded* blob, because that is what the server hashes. It goes
        // in the record, so reading back never has to re-derive it from bytes it no longer has.
        _chunkCache?.Write(
            new ChunkPos(message.ChunkX, message.ChunkZ), ChunkHash.Of(blob), message.Compressed);

        _chunksViaMessage++;
        _chunkMessageBytes += message.Compressed.Length;
        MetricRegistry.Set(ClientMetrics.ChunksViaMessage, _chunksViaMessage);
        MetricRegistry.Set(ClientMetrics.ChunkMessageBytes, _chunkMessageBytes);
    }

    /// <summary>Session totals behind the chunk metrics, which are gauges rather than counters.</summary>
    private long _chunksViaMessage;

    private long _chunkMessageBytes;

    private long _chunksFromCache;

    private long _chunkBytesSaved;

    private void onTimeSyncResponse(TimeSyncResponseMessage response)
    {
        // T2 and T3 rode in on the envelope: T2 stamped inside the server's write path, T3 on this
        // client's read thread before queueing. Neither is read here, because this runs on the game
        // thread up to a tick later and would measure the tick phase rather than the network.
        Clock?.Complete(
            response.Sequence,
            response.ClientSendTime,
            response.ServerRecvTime,
            response.TransportSentAtMs,
            response.TransportReceivedAtMs);
    }

    /// <summary>
    ///     Samples every interpolated entity onto the current instant on the server timeline. Called
    ///     once per tick, immediately before the entities tick — see
    ///     <see cref="EntityInterpolator.Apply" /> for why the ordering is load-bearing.
    /// </summary>
    public void ApplyInterpolation(World world)
    {
        if (Clock is not { Synchronised: true })
        {
            return;
        }

        Interpolation.Apply(world, Clock.ServerTimeMs);
    }

    private void onTickStamp(TickStampMessage stamp)
    {
        // Every entity update read after this and before the next stamp describes this instant.
        // RetargetEntity attaches it to each snapshot it records.
        CurrentBatchServerTimeMs = stamp.ServerTimeMs;
        TickStampsReceived++;
        MetricRegistry.Set(ClientMetrics.TickStampsReceived, TickStampsReceived);
    }

    /// <summary>
    ///     Server-clock instant of the most recent <see cref="TickStampMessage" />, or 0 if the
    ///     stream has never been stamped. Zero is the signal that this server does not stamp — an
    ///     older OmniBlock build, or the loopback path — and that interpolation must fall back to
    ///     the move-toward-target behaviour rather than interpolate against a timeline that does
    ///     not exist.
    /// </summary>
    public long CurrentBatchServerTimeMs { get; private set; }

    /// <summary>Count of stamps seen, for the overlay. Also distinguishes "not stamped" from
    ///     "stamped once, long ago".</summary>
    public long TickStampsReceived { get; private set; }

    /// <summary>
    ///     Identifies the server for cache-file naming, or null on a loopback connection, which does
    ///     not cache. Address and port only — the world seed and dimension are appended once known.
    /// </summary>
    private readonly string? _cacheKey;

    private ChunkBlobCache? _chunkCache;


    /// <summary>Whether the cache for the current world has been advertised yet.</summary>
    private bool _cacheOffered;

    /// <summary>
    ///     Opens the chunk cache for the world just joined, keyed so that two worlds cannot be
    ///     confused for one another.
    ///     <para>
    ///         A mismatched key would not corrupt anything — the server checks every hash against its
    ///         own chunk and simply sends the chunk when one does not match — but it would make the
    ///         cache useless, so the seed and dimension are part of the name rather than trusted to
    ///         coincide.
    ///     </para>
    /// </summary>
    private void OpenChunkCache(long worldSeed, int dimensionId)
    {
        _chunkCache?.Dispose();
        _chunkCache = null;
        _cacheOffered = false;

        if (_cacheKey is null)
        {
            return;
        }

        string safeKey = string.Concat(_cacheKey.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string path = Path.Combine(
            _context.ChunkCacheDirectory, safeKey, $"{worldSeed:X16}_dim{dimensionId}.bin");

        _chunkCache = ChunkBlobCache.Open(path);
        _logger.LogInformation("Chunk cache holds {Count} chunks for this world.", _chunkCache.Count);
    }

    /// <summary>
    ///     Advertises the cached chunks near where the player was last in this world.
    ///     <para>
    ///         <b>Sent from configuration, not from the first position packet.</b> The server starts
    ///         streaming chunks immediately after it places the player, so an offer triggered by that
    ///         placement arrives after the chunks it was meant to save. Configuration is the last
    ///         point that is both after message-registry negotiation — without which this would be
    ///         silently dropped — and before any chunk is queued.
    ///     </para>
    ///     <para>
    ///         Which means there is no position yet, hence <see cref="ChunkBlobCache.LastCentre" />.
    ///         Rejoining puts the player where they logged out, so the stored centre is the right
    ///         one; on a first visit the cache is empty and the centre does not matter.
    ///     </para>
    /// </summary>
    private void OfferChunkCache()
    {
        if (_cacheOffered || _chunkCache is null)
        {
            return;
        }

        // Set before the emptiness check, not after. An empty cache has nothing to offer and that is
        // the final answer for this world — leaving the flag clear made this retry on every later
        // trigger, and by then the cache was full of chunks the server had just sent, so the offer
        // told it precisely what it already knew. Measured at 925 chunks advertised on a first join.
        _cacheOffered = true;

        if (_chunkCache.Count == 0)
        {
            return;
        }

        int centreX = _chunkCache.LastCentre.X;
        int centreZ = _chunkCache.LastCentre.Z;

        // Nearest first, then take as many as the message allows.
        //
        // There is no distance cutoff, deliberately. One was tried at a radius chosen to be
        // "generous against any view distance" and a server running view-distance 32 immediately
        // exceeded it: 4,225 chunks cached, 2,401 advertised, and the missing ring re-sent in full
        // on every join. The client cannot know the server's view distance at this point, so any
        // constant here is a guess that some server invalidates.
        //
        // Offering too much is nearly free by comparison: sixteen bytes to maybe save two thousand.
        // The only real bound is upstream cost, which is what MaxEntries is for, and ordering by
        // distance means hitting it discards the least likely to be wanted rather than an arbitrary
        // subset.
        ChunkCacheOfferMessage offer = new();

        offer.Entries.AddRange(_chunkCache.Entries
            .OrderBy(entry => Math.Max(
                Math.Abs(entry.Key.X - centreX),
                Math.Abs(entry.Key.Z - centreZ)))
            .Take(ChunkCacheOfferMessage.MaxEntries));

        if (offer.Entries.Count > 0)
        {
            SendMessage(offer);
            _logger.LogInformation("Offered {Count} cached chunks to the server.", offer.Entries.Count);
        }
    }

    public override void onHello(LoginHelloPacket packet)
    {
        _logger.LogInformation($"[Client] Received onHello from server (id: {packet.ProtocolVersion})");
        OpenChunkCache(packet.WorldSeed, packet.DimensionId);
        _context.PlayerHost.SetPlayerController(_context.Factory.CreatePlayerController(this));
        _context.StatFileWriter.ReadStat(Stats.Stats.JoinMultiplayerStat, 1);
        _worldClient = new ClientWorld(this, packet.WorldSeed, packet.DimensionId)
        {
            IsRemote = true
        };
        _context.WorldHost.ChangeWorld(_worldClient);
        _context.PlayerHost.Player.DimensionId = packet.DimensionId;
        _context.Navigator.Navigate(_context.Factory.CreateTerrainScreen(this));
        _context.PlayerHost.Player.ID = packet.ProtocolVersion;
    }

    private void onItemEntitySpawn(ItemEntitySpawnMessage packet)
    {
        double x = packet.X / 32.0D;
        double y = packet.Y / 32.0D;
        double z = packet.Z / 32.0D;
        Entity entityItem = DroppedItemBehavior.Create(_worldClient, x, y, z, new ItemStack(packet.ItemRawId, packet.ItemCount, packet.ItemDamage));
        entityItem.VelocityX = packet.VelocityX / 128.0D;
        entityItem.VelocityY = packet.VelocityY / 128.0D;
        entityItem.VelocityZ = packet.VelocityZ / 128.0D;
        entityItem.TrackedPosX = packet.X;
        entityItem.TrackedPosY = packet.Y;
        entityItem.TrackedPosZ = packet.Z;
        _worldClient.ForceEntity(packet.EntityId, entityItem);
    }

    private void onEntitySpawn(EntitySpawnMessage packet)
    {
        double x = packet.X / 32.0D;
        double y = packet.Y / 32.0D;
        double z = packet.Z / 32.0D;
        Entity? entity = null;
        if (packet.EntityType == 63)
        {
            entity = EntityRegistry.ByName("fireball").Create(_worldClient);
            entity.SetPositionAndAngles(x, y, z, 0.0F, 0.0F);
            entity.Behaviors.Find<FireballBehavior>()!.SetDirection(entity, packet.VelocityX / 8000.0D, packet.VelocityY / 8000.0D, packet.VelocityZ / 8000.0D);
            packet.EntityData = 0;
        }

        // Entities that declare their object-spawn id resolve through the registry rather than a
        // per-id branch; the branches above are the ones whose constructors still need arguments.
        if (entity == null && EntityRegistry.BySpawnObjectId(packet.EntityType) is { } declaredType)
        {
            entity = declaredType.Create(_worldClient);
            entity.SetPositionAndAngles(x, y, z, 0.0F, 0.0F);
        }

        // Falling blocks share one entity type across several object ids, one per block — the
        // behavior's declared wire ids say which block this spawn carries.
        if (entity == null)
        {
            foreach (EntityType candidate in DefaultRegistries.EntityTypes)
            {
                if (candidate.Behaviors.Find<SettleAsBlockBehavior>() is not { } settle) continue;
                if (settle.BlockForSpawnObjectId(packet.EntityType) is not { } carriedBlockId) continue;

                entity = candidate.Create(_worldClient);
                settle.SetBlock(entity, carriedBlockId);
                entity.SetPositionAndAngles(x, y, z, 0.0F, 0.0F);
                break;
            }
        }

        // Minecarts do the same across their three kinds, and the kind decides how they are drawn.
        if (entity == null)
        {
            foreach (EntityType candidate in DefaultRegistries.EntityTypes)
            {
                if (candidate.Behaviors.Find<MinecartBehavior>() is not { } cart) continue;
                if (cart.TypeForSpawnObjectId(packet.EntityType) is not { } cartType) continue;

                entity = MinecartBehavior.Place(_worldClient, x, y, z, cartType);
                break;
            }
        }

        if (entity != null)
        {
            entity.TrackedPosX = packet.X;
            entity.TrackedPosY = packet.Y;
            entity.TrackedPosZ = packet.Z;
            entity.Yaw = 0.0F;
            entity.Pitch = 0.0F;
            entity.ID = packet.EntityId;
            _worldClient.ForceEntity(packet.EntityId, entity);
            if (packet.EntityData > 0)
            {
                if (entity.Behaviors.Find<ArrowBehavior>() is { } flight && GetEntityById(packet.EntityData) is EntityLiving shooter)
                {
                    flight.SetOwner(entity, shooter);
                }

                entity.SetVelocityClient(packet.VelocityX / 8000.0D, packet.VelocityY / 8000.0D, packet.VelocityZ / 8000.0D);
            }
        }

    }

    private void onGlobalEntitySpawn(GlobalEntitySpawnMessage packet)
    {
        double x = packet.X / 32.0D;
        double y = packet.Y / 32.0D;
        double z = packet.Z / 32.0D;
        Entity? ent = null;
        if (EntityRegistry.ByGlobalSpawnId(packet.Type) is { } globalType)
        {
            ent = globalType.Create(_worldClient);
            ent.SetPositionAndAnglesKeepPrevAngles(x, y, z, 0.0F, 0.0F);
        }

        if (ent != null)
        {
            ent.TrackedPosX = packet.X;
            ent.TrackedPosY = packet.Y;
            ent.TrackedPosZ = packet.Z;
            ent.Yaw = 0.0F;
            ent.Pitch = 0.0F;
            ent.ID = packet.EntityId;
            _worldClient.Entities.SpawnGlobalEntity(ent);
        }

    }

    private void onPaintingSpawn(PaintingSpawnMessage packet)
    {
        Entity ent = HangingArtBehavior.HangAt(_worldClient, packet.X, packet.Y, packet.Z, packet.Direction, packet.Title);
        _worldClient.ForceEntity(packet.EntityId, ent);
    }

    private void onEntityVelocity(EntityVelocityMessage packet)
    {
        Entity? ent = GetEntityById(packet.EntityId);
        ent?.SetVelocityClient(packet.MotionX / 8000.0D, packet.MotionY / 8000.0D, packet.MotionZ / 8000.0D);
    }

    private void onEntityData(EntityDataMessage packet)
    {
        Entity? ent = GetEntityById(packet.EntityId);
        if (ent == null || packet.Data.Length == 0)
        {
            return;
        }

        ent.DataSynchronizer.ApplyChanges(new MemoryStream(packet.Data));
    }

    private void onPlayerSpawn(PlayerSpawnMessage packet)
    {
        double x = packet.X / 32.0D;
        double y = packet.Y / 32.0D;
        double z = packet.Z / 32.0D;
        float rotation = packet.Yaw * 360 / 256.0F;
        float pitch = packet.Pitch * 360 / 256.0F;
        OtherPlayerEntity ent = new(_context.WorldHost.World, packet.Name);
        ent.PrevX = ent.LastTickX = ent.TrackedPosX = packet.X;
        ent.PrevY = ent.LastTickY = ent.TrackedPosY = packet.Y;
        ent.PrevZ = ent.LastTickZ = ent.TrackedPosZ = packet.Z;
        int currentItem = packet.CurrentItem;
        if (currentItem == 0)
        {
            ent.Inventory.Main[ent.Inventory.SelectedSlot] = null;
        }
        else
        {
            ent.Inventory.Main[ent.Inventory.SelectedSlot] = new ItemStack(currentItem, 1, 0);
        }

        ent.SetPositionAndAngles(x, y, z, rotation, pitch);
        _worldClient.ForceEntity(packet.EntityId, ent);
    }

    private void onEntityTeleport(EntityTeleportMessage packet)
    {
        Entity? ent = GetEntityById(packet.EntityId);
        if (ent != null)
        {
            ent.TrackedPosX = packet.X;
            ent.TrackedPosY = packet.Y;
            ent.TrackedPosZ = packet.Z;
            float yaw = packet.Yaw * 360 / 256.0F;
            float pitch = packet.Pitch * 360 / 256.0F;
            RetargetEntity(ent, yaw, pitch);
        }
    }

    /// <summary>
    ///     Hands an entity's new server position to whichever movement scheme is active.
    ///     <para>
    ///         The snapshot is always recorded, so toggling <see cref="EntityInterpolator.Enabled" />
    ///         mid-session takes effect on the next frame rather than after a buffer refills. The
    ///         legacy retarget is skipped while interpolating: leaving it running would have
    ///         <c>EntityLiving</c> stepping toward its own target every tick underneath the sampled
    ///         position, and its collision-and-step block would shove entities around between
    ///         frames.
    ///     </para>
    /// </summary>
    private void RetargetEntity(Entity entity, float yaw, float pitch)
    {
        // TrackedPos* is fixed-point in 1/32 blocks, the same conversion the legacy overload does.
        Interpolation.Record(
            entity.ID,
            CurrentBatchServerTimeMs,
            entity.TrackedPosX / 32.0D,
            entity.TrackedPosY / 32.0D,
            entity.TrackedPosZ / 32.0D,
            yaw,
            pitch);

        if (!Interpolation.IsInterpolating(entity.ID))
        {
            entity.SetPositionAndAnglesAvoidEntities(yaw, pitch, 5);
        }
    }

    /// <summary>
    ///     The four position packets collapsed into one message and a mask, so the four handlers
    ///     that differed only in which of these two lines they ran collapse with them.
    /// </summary>
    private void onEntityMove(EntityMoveMessage packet)
    {
        Entity? ent = GetEntityById(packet.EntityId);
        if (ent is null)
        {
            return;
        }

        if (packet.Mask.HasFlag(EntityMoveMessage.Field.Moved))
        {
            ent.TrackedPosX += packet.DeltaX;
            ent.TrackedPosY += packet.DeltaY;
            ent.TrackedPosZ += packet.DeltaZ;
        }

        // An unrotated update keeps whatever angle the entity already had, which is what the two
        // position-only packets did by having no rotation field to read.
        float yaw = packet.Mask.HasFlag(EntityMoveMessage.Field.Rotated) ? packet.Yaw * 360 / 256.0F : ent.Yaw;
        float pitch = packet.Mask.HasFlag(EntityMoveMessage.Field.Rotated) ? packet.Pitch * 360 / 256.0F : ent.Pitch;

        RetargetEntity(ent, yaw, pitch);
    }

    private void onEntityDestroy(EntityDestroyMessage packet)
    {
        Interpolation.Forget(packet.EntityId);

        // The server drops it from its baseline on the same event, so both ends stop holding a state
        // for it in the same order. Leaving it here would leave the two disagreeing about whether the
        // entity is present, which the delta encoding has no way to detect.
        Snapshots.Forget(packet.EntityId);

        _worldClient.RemoveEntityFromWorld(packet.EntityId);
    }

    private void onPlayerMove(IPlayerMove packet)
    {
        // Previously this called ent.SetPositionAndAngles(x, y, z, yaw, pitch);

        ClientPlayerEntity? ent = _context.PlayerHost.Player;
        if (ent == null) return;

        ent.CameraOffset = 0.0F;

        if (packet is IPlayerMovePosition packetMove)
        {
            ent.PrevX = ent.X = packetMove.X;
            ent.PrevY = ent.Y = packetMove.Y;
            ent.PrevZ = ent.Z = packetMove.Z;

            ent.VelocityX = ent.VelocityY = ent.VelocityZ = 0.0D;

            ent.UpdateBoundingBox();

            packetMove.Y = ent.BoundingBox.MinY;
            packetMove.EyeHeight = ent.Y;
        }

        if (packet is IPlayerMoveLook packetLook)
        {
            ent.PrevYaw = ent.Yaw = packetLook.Yaw % 360.0F;
            ent.PrevPitch = ent.Pitch = packetLook.Pitch % 360.0F;
        }

        SendMessage((Message)packet);
        if (!_terrainLoaded)
        {
            ent.PrevX = ent.X;
            ent.PrevY = ent.Y;
            ent.PrevZ = ent.Z;
            _terrainLoaded = true;
            _context.Navigator.Navigate(null);
        }

    }

    private void onChunkStatusUpdate(ChunkStatusUpdateMessage packet)
    {
        _worldClient.UpdateChunk(packet.X, packet.Z, packet.Loaded);
    }

    private void onChunkDeltaUpdate(ChunkDeltaUpdateMessage packet)
    {
        Chunk chunk = _worldClient.BlockHost.GetChunk(packet.X, packet.Z);
        int x = packet.X * 16;
        int y = packet.Z * 16;

        for (int i = 0; i < packet.Positions.Length; ++i)
        {
            short positions = packet.Positions[i];
            int blockRawId = packet.BlockRawIds[i] & 255;
            byte metadata = packet.BlockMetadata[i];
            int blockX = positions >> 12 & 15;
            int blockZ = positions >> 8 & 15;
            int blockY = positions & 255;
            chunk.SetBlock(blockX, blockY, blockZ, blockRawId, metadata);

            // Applied separately from the block, because SetBlock refuses an update that leaves the
            // block identical and a light-only change is exactly that.
            if (i < packet.Light.Length)
            {
                chunk.SetPackedLight(blockX, blockY, blockZ, packet.Light[i]);
            }

            _worldClient.ClearBlockResets(blockX + x, blockY, blockZ + y, blockX + x, blockY, blockZ + y);
            _worldClient.setBlocksDirty(blockX + x, blockY, blockZ + y, blockX + x, blockY, blockZ + y);
        }

    }

    private void onRegionData(RegionDataMessage message)
    {
        _worldClient.ClearBlockResets(message.X, message.Y, message.Z, message.X + message.SizeX - 1, message.Y + message.SizeY - 1, message.Z + message.SizeZ - 1);
        _worldClient.HandleChunkDataUpdate(message.X, message.Y, message.Z, message.SizeX, message.SizeY, message.SizeZ, message.Decompress());
    }

    private void onBlockUpdate(BlockUpdateMessage packet)
    {
        _worldClient.SetBlockWithMetaFromPacket(packet.X, packet.Y, packet.Z, packet.BlockRawId, packet.BlockMetadata, packet.Light);
    }

    /// <summary>
    ///     Overwrites whole sections of a chunk's light with the server's copy.
    /// </summary>
    /// <remarks>
    ///     Dropped rather than queued when the chunk is absent. The section is a snapshot of an
    ///     array rather than an edit to it, so nothing depends on this one having been applied —
    ///     the chunk arrives carrying light of its own, and the next write to any of these sections
    ///     sends them again.
    /// </remarks>
    private void onLightSections(LightSectionsMessage message)
    {
        if (!_worldClient.BlockHost.HasChunk(message.ChunkX, message.ChunkZ))
        {
            return;
        }

        message.ApplyTo(_worldClient.BlockHost.GetChunk(message.ChunkX, message.ChunkZ));

        _worldClient.setBlocksDirty(
            message.ChunkX * 16, 0, message.ChunkZ * 16,
            message.ChunkX * 16 + 15, ChuckFormat.WorldHeight - 1, message.ChunkZ * 16 + 15);
    }

    private void onDisconnect(DisconnectMessage packet)
    {
        _netManager.disconnect("disconnect.kicked");
        Disconnected = true;
        ReleaseResources();
        _context.WorldHost.ChangeWorld(null);
        _context.Navigator.Navigate(_context.Factory.CreateFailedScreen("disconnect.disconnected", string.Format(Translations.Get("disconnect.genericReason"), packet.Reason), [packet.Reason]));
    }

    public override void onDisconnected(string reason, object[]? args)
    {
        if (!Disconnected)
        {
            Disconnected = true;
            ReleaseResources();
            _context.WorldHost.ChangeWorld(null);
            _context.Navigator.Navigate(_context.Factory.CreateFailedScreen("disconnect.lost", reason, args));
        }
    }

    public void SendPacketAndDisconnect(Packet packet)
    {
        if (!Disconnected)
        {
            SendPacket(packet);
            _netManager.disconnect();

            // This is the path quitting to the title screen actually takes, so teardown belongs here
            // as much as anywhere. Disconnected is not set: the caller leaves that to the disconnect
            // handling that follows, and ReleaseResources is safe to run twice.
            ReleaseResources();
        }
    }

    public void AddToSendQueue(Packet packet)
    {
        SendPacket(packet);
    }

    private void onItemPickup(ItemPickupMessage packet)
    {
        Entity? ent = GetEntityById(packet.EntityId);
        Entity collector = GetEntityById(packet.CollectorEntityId) as EntityLiving ?? _context.PlayerHost.Player;

        if (ent != null && collector != null)
        {
            _worldClient.Broadcaster.PlaySoundAtEntity(ent, "random.pop", 0.2F, ((_rand.NextFloat() - _rand.NextFloat()) * 0.7F + 1.0F) * 2.0F);
            _context.ParticleManager.AddSpecialParticle(new LegacyParticleAdapter(new EntityPickupFX(_context.WorldHost.World, ent, collector, -0.5F)));
            _worldClient.RemoveEntityFromWorld(packet.EntityId);
        }

    }

    private void onChatMessage(ChatMessage packet)
    {
        _context.AddChatMessage(packet.Text);
    }

    private void onEntityAnimation(EntityAnimationMessage packet)
    {
        Entity? ent = GetEntityById(packet.EntityId);
        if (ent != null)
        {
            if (packet.AnimationId == 1)
            {
                if (ent is EntityPlayer player)
                    player.SwingHand();
            }
            else if (packet.AnimationId == 2)
            {
                ent.AnimateHurt();
            }
            else if (packet.AnimationId == 3)
            {
                if (ent is EntityPlayer player)
                    player.WakeUp(false, false, false);
            }
            else if (packet.AnimationId == 4)
            {
                if (ent is EntityPlayer player)
                    player.Spawn();
            }

        }
    }

    private void onPlayerSleepUpdate(PlayerSleepUpdateMessage packet)
    {
        Entity? ent = GetEntityById(packet.PlayerId);
        if (ent is EntityPlayer player)
        {
            if (packet.Status == 0)
            {
                player.TrySleep(packet.X, packet.Y, packet.Z);
            }

        }
    }

    public override void onHandshake(HandshakePacket packet)
    {
        AddToSendQueue(LoginHelloPacket.Get(
            _context.Session.username, 14, ProtocolHandshake.Encode(ProtocolHandshake.Version), 0));
    }

    public void Disconnect()
    {
        Disconnected = true;
        _netManager.disconnect("disconnect.closed");
        ReleaseResources();
    }

    /// <summary>
    ///     Releases everything this connection owns outside managed memory. Idempotent, and called
    ///     from every path that ends a connection rather than from one of them.
    ///     <para>
    ///         That matters because the obvious path is not the common one. Quitting to the title
    ///         screen goes through <c>ClientWorld.Disconnect</c> and
    ///         <see cref="SendPacketAndDisconnect" />, never through <see cref="Disconnect" />, which
    ///         only the connecting screen calls. Hanging teardown off that one left a bound UDP port,
    ///         a receive thread, and an exclusively-held cache file behind on every ordinary
    ///         disconnect — the cache made it visible, because the next join could not open its own
    ///         file and said so.
    ///     </para>
    /// </summary>
    private void ReleaseResources()
    {
        // The transport owns a bound port and a receive thread. Leaving them behind leaks both per
        // server the player joins in a session, which the stream transport did not do because
        // closing its socket was the whole of its teardown.
        _transport?.DisposeAsync().AsTask().GetAwaiter().GetResult();

        // Flushes, compacts, and releases the exclusive handle. Losing this costs the session's
        // chunks since the last periodic flush, not the whole cache.
        _chunkCache?.Dispose();
        _chunkCache = null;
    }

    private void onLivingEntitySpawn(LivingEntitySpawnMessage packet)
    {
        double x = packet.X / 32.0D;
        double y = packet.Y / 32.0D;
        double z = packet.Z / 32.0D;
        float yaw = packet.Yaw * 360 / 256.0F;
        float pitch = packet.Pitch * 360 / 256.0F;
        EntityLiving ent = (EntityLiving)EntityRegistry.Create(packet.Type, _context.WorldHost.World);
        ent.TrackedPosX = packet.X;
        ent.TrackedPosY = packet.Y;
        ent.TrackedPosZ = packet.Z;
        ent.ID = packet.EntityId;
        ent.SetPositionAndAngles(x, y, z, yaw, pitch);
        ent.LastTickX = ent.X;
        ent.LastTickY = ent.Y;
        ent.LastTickZ = ent.Z;
        ent.InterpolateOnly = true;
        _worldClient.ForceEntity(packet.EntityId, ent);
        ent.DataSynchronizer.ApplyChanges(new MemoryStream(packet.Data));
    }

    private void onWorldTimeUpdate(WorldTimeUpdateMessage packet)
    {
        _context.WorldHost.World?.SetTime(packet.Time);
    }

    private void onPlayerSpawnPosition(PlayerSpawnPositionMessage packet)
    {
        _context.PlayerHost.Player.SetSpawnPos(new Vec3I(packet.X, packet.Y, packet.Z));
        _context.WorldHost.World?.Properties.SetSpawn(packet.X, packet.Y, packet.Z);
    }

    private void onEntityVehicle(EntityVehicleMessage packet)
    {
        object? rider = GetEntityById(packet.EntityId);
        Entity? ent = GetEntityById(packet.VehicleEntityId);
        if (packet.EntityId == _context.PlayerHost.Player.ID)
        {
            rider = _context.PlayerHost.Player;
        }

        if (rider is Entity riderEntity)
        {
            riderEntity.SetVehicle(ent);
        }
    }

    private void onEntityStatus(EntityStatusMessage packet)
    {
        Entity? ent = GetEntityById(packet.EntityId);
        ent?.ProcessServerEntityStatus(packet.Status);

    }

    private Entity? GetEntityById(int entityId)
    {
        if (_context.PlayerHost.Player == null || _worldClient == null)
        {
            return null;
        }

        return entityId == _context.PlayerHost.Player.ID ? _context.PlayerHost.Player : _worldClient.GetEntity(entityId);
    }

    private void onHealthUpdate(HealthUpdateMessage packet)
    {
        _context.PlayerHost.Player.setHealth(packet.HealthMp);
    }

    private void onPlayerRespawn(PlayerRespawnMessage packet)
    {
        if (packet.DimensionId != _context.PlayerHost.Player.DimensionId)
        {
            // Every entity in the old world is about to go away without a destroy packet each, so
            // the baseline is asked to start over rather than left holding states for entities whose
            // IDs the new world is free to reuse. Sequence zero is what tells the server to agree.
            Snapshots.Reset();

            _terrainLoaded = false;
            _worldClient = new ClientWorld(this, _worldClient.Properties.RandomSeed, packet.DimensionId)
            {
                IsRemote = true
            };
            _context.WorldHost.ChangeWorld(_worldClient);
            _context.PlayerHost.Player.DimensionId = packet.DimensionId;
            _context.Navigator.Navigate(_context.Factory.CreateTerrainScreen(this));
        }

        _context.PlayerHost.Respawn(true, packet.DimensionId);
    }

    private void onExplosion(ExplosionMessage packet)
    {
        Explosion explosion = new(_context.WorldHost.World, null, packet.X, packet.Y, packet.Z, packet.Radius)
        {
            destroyedBlockPositions = [.. packet.DestroyedBlocks]
        };
        explosion.doExplosionB(true);
    }

    private void onOpenScreen(OpenScreenMessage packet)
    {
        ClientPlayerEntity player = _context.PlayerHost.Player;
        if (!player.GameMode.CanInteract) return;

        if (packet.ScreenHandlerId == 0)
        {
            InventoryBasic inventory = new(packet.Name, packet.SlotsCount);
            player.openChestScreen(inventory);
            player.CurrentScreenHandler.SyncId = packet.SyncId;
        }
        else if (packet.ScreenHandlerId == 2)
        {
            BlockEntityFurnace furnace = new();
            player.openFurnaceScreen(furnace);
            player.CurrentScreenHandler.SyncId = packet.SyncId;
        }
        else if (packet.ScreenHandlerId == 3)
        {
            BlockEntityDispenser dispenser = new();
            player.openDispenserScreen(dispenser);
            player.CurrentScreenHandler.SyncId = packet.SyncId;
        }
        else if (packet.ScreenHandlerId == 1)
        {
            player.openCraftingScreen(MathHelper.Floor(player.X), MathHelper.Floor(player.Y), MathHelper.Floor(player.Z));
            player.CurrentScreenHandler.SyncId = packet.SyncId;
        }

    }

    private void onScreenHandlerSlot(ScreenHandlerSlotMessage packet)
    {
        ClientPlayerEntity? player = _context.PlayerHost.Player;
        if (packet.SyncId == -1)
        {
            player.Inventory.SetCursorStack(packet.Stack);
        }
        else if (packet.SyncId == 0 && packet.Slot >= 36 && packet.Slot < 45)
        {
            ItemStack? itemStack = player.PlayerScreenHandler.GetSlot(packet.Slot).getStack();
            if (packet.Stack != null && (itemStack == null || itemStack.Count < packet.Stack.Count))
            {
                packet.Stack.AnimationTime = 5;
            }

            player.PlayerScreenHandler.setStackInSlot(packet.Slot, packet.Stack);
        }
        else if (packet.SyncId == player.CurrentScreenHandler.SyncId)
        {
            player.CurrentScreenHandler.setStackInSlot(packet.Slot, packet.Stack);
        }

    }

    private void onScreenHandlerAck(ScreenHandlerAckMessage packet)
    {
        ClientPlayerEntity player = _context.PlayerHost.Player;
        ScreenHandler? screenHandler = null;
        if (packet.SyncId == 0)
        {
            screenHandler = player.PlayerScreenHandler;
        }
        else if (packet.SyncId == player.CurrentScreenHandler.SyncId)
        {
            screenHandler = player.CurrentScreenHandler;
        }

        if (screenHandler != null)
        {
            if (packet.Accepted)
            {
                ScreenHandler.onAcknowledgementAccepted(packet.ActionType);
            }
            else
            {
                ScreenHandler.onAcknowledgementDenied(packet.ActionType);
                SendMessage(new ScreenHandlerAckMessage
                {
                    SyncId = packet.SyncId,
                    ActionType = packet.ActionType,
                    Accepted = true,
                });
            }
        }

    }

    private void onInventory(InventoryMessage packet)
    {
        ClientPlayerEntity? player = _context.PlayerHost.Player;
        if (packet.SyncId == 0)
        {
            player.PlayerScreenHandler.updateSlotStacks(packet.Contents);
        }
        else if (packet.SyncId == player.CurrentScreenHandler.SyncId)
        {
            player.CurrentScreenHandler.updateSlotStacks(packet.Contents);
        }

    }

    private void onUpdateSign(UpdateSignMessage packet)
    {
        if (_context.WorldHost.World.BlockHost.IsPosLoaded(packet.X, packet.Y, packet.Z))
        {
            BlockEntitySign? signEntity = _context.WorldHost.World.Entities.GetBlockEntity<BlockEntitySign>(packet.X, packet.Y, packet.Z);

            if (signEntity != null)
            {
                for (int i = 0; i < 4; ++i)
                {
                    signEntity.Texts[i] = packet.Lines[i];
                }

                signEntity.MarkDirty();
            }
        }
    }

    private void onScreenHandlerProperty(ScreenHandlerPropertyMessage packet)
    {
        ClientPlayerEntity player = _context.PlayerHost.Player;
        if (player.CurrentScreenHandler != null && player.CurrentScreenHandler.SyncId == packet.SyncId)
        {
            player.CurrentScreenHandler.setProperty(packet.PropertyId, packet.Value);
        }

    }

    private void onEntityEquipment(EntityEquipmentMessage packet)
    {
        Entity? ent = GetEntityById(packet.EntityId);
        ent?.SetEquipmentStack(packet.Slot, packet.ItemRawId, packet.ItemDamage);

    }

    private void onPlayNoteSound(PlayNoteSoundMessage packet)
    {
        _context.WorldHost.World?.Broadcaster.PlayNote(packet.X, packet.Y, packet.Z, packet.Instrument, packet.Pitch);
    }

    private void onGameStateChange(GameStateChangeMessage packet)
    {
        int reason = packet.Reason;
        if (reason >= 0 && reason < GameStateChangeMessage.Reasons.Length && GameStateChangeMessage.Reasons[reason] != null)
        {
            _context.PlayerHost.Player.SendMessage(GameStateChangeMessage.Reasons[reason]);
        }

        if (reason == 1)
        {
            _worldClient.Properties.IsRaining = true;
            _worldClient.Environment.SetRainGradient(1.0F);
        }
        else if (reason == 2)
        {
            _worldClient.Properties.IsRaining = false;
            _worldClient.Environment.SetRainGradient(0.0F);
        }
        else if (reason == 7)
        {
            _worldClient.Properties.IsThundering = true;
            _worldClient.Environment.SetThunderGradient(1.0F);
        }
        else if (reason == 8)
        {
            _worldClient.Properties.IsThundering = false;
            _worldClient.Environment.SetThunderGradient(0.0F);
        }
    }

    private void onMapUpdate(MapUpdateMessage packet)
    {
        if (packet.ItemRawId == Item.ByName("map").Id)
        {
            MapBehavior.GetMapState(packet.MapId, _context.WorldHost.World).UpdateData(packet.Data);
        }
        else
        {
            _logger.LogInformation($"Unknown itemid: {packet.MapId}");
        }

    }

    private void onWorldEvent(WorldEventMessage packet)
    {
        _context.WorldHost.World?.Broadcaster.WorldEvent(packet.EventId, packet.X, packet.Y, packet.Z, packet.Data);
    }

    private void onIncreaseStat(IncreaseStatMessage packet)
    {
        try
        {
            StatBase stat = Stats.Stats.GetStatById(packet.StatId);
            ((EntityClientPlayerMP)_context.PlayerHost.Player).IncreaseRemoteStat(stat, packet.Amount);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Unknown stat id in IncreaseStatMessage: {StatId}", packet.StatId);
        }
    }

    private void onPlayerConnectionUpdate(PlayerConnectionUpdateMessage packet)
    {
        if (packet.Type == PlayerConnectionUpdateMessage.UpdateType.Leave)
        {
            Entity? ent = _worldClient.GetEntity(packet.EntityId);
            EntityRenderDispatcher.Instance.SkinManager?.Release(packet.Name);
        }
    }

    private void onRegistryData(RegistryDataMessage packet)
    {
        _clientRegistries.Accumulate(packet);
    }

    private void onFinishConfiguration(FinishConfigurationMessage packet)
    {
        _logger.LogInformation("Configuration finished");

        // After registry negotiation and before the server queues a single chunk. See OfferChunkCache.
        OfferChunkCache();
    }

    private void onPlayerGameModeUpdate(PlayerGameModeUpdateMessage packet)
    {
        Holder<GameMode>? gameMode = _clientRegistries.Get(RegistryKeys.GameModes,
            new ResourceLocation(packet.GameModeNamespace, packet.GameModeName));
        if (gameMode is not null && _context.PlayerHost.Player is { } player)
        {
            player.GameModeHolder = gameMode;
        }
        else if (gameMode is null)
        {
            _logger.LogWarning("Received unknown game mode name '{Name}'", packet.GameModeName);
        }
    }

    public override bool isServerSide()
    {
        return false;
    }
}
