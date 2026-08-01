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
using BetaSharp.Inventorys;
using BetaSharp.Items;
using BetaSharp.Items.Behaviors;
using BetaSharp.Network;
using BetaSharp.Network.Messages;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Packets.C2SPlay;
using BetaSharp.Network.Packets.Play;
using BetaSharp.Network.Packets.S2CPlay;
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
    public override MessageRegistry? Messages { get; } = new();

    /// <summary>
    ///     Synchronised server clock. Null for the loopback path (<see cref="InternalConnection" />),
    ///     where offset is identically zero and there is no jitter to measure. Polled each tick and
    ///     fed from <see cref="onTimeSyncResponse" />.
    /// </summary>
    public ServerClock? Clock { get; }

    /// <summary>
    ///     Render-time interpolation for remote entities. Present on every connection, but only
    ///     does anything once the server is stamping batches and the clock has synchronised — see
    ///     <see cref="ShouldInterpolate" />.
    /// </summary>
    public EntityInterpolator Interpolation { get; } = new();

    /// <summary>
    ///     Whether there is a shared timeline to interpolate against. False on the loopback path
    ///     (no clock), against a server that does not stamp, and during the login burst before the
    ///     first offset lands. In each case the legacy behaviour stays in charge.
    /// </summary>
    public bool ShouldInterpolate =>
        Clock is { Synchronised: true } && CurrentBatchServerTimeMs != 0;

    public ClientNetworkHandler(ClientNetworkContext context, string address, int port)
    {
        _context = context;

        IPAddress[] addresses = Dns.GetHostAddresses(address);
        var endPoint = new IPEndPoint(addresses.FirstOrDefault(a => a.AddressFamily is AddressFamily.InterNetwork) ?? addresses.First(), port);

        Socket socket = new(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };

        socket.Connect(endPoint);

        _netManager = new Connection(socket, "Client", this);

        Clock = new ServerClock();
    }

    public ClientNetworkHandler(ClientNetworkContext context, Connection connection)
    {
        _context = context;
        _netManager = connection;
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
            MetricRegistry.Set(ClientMetrics.IsInternal, _netManager is InternalConnection);
            MetricRegistry.Set(ClientMetrics.ServerAddress, _netManager.getAddress()?.ToString() ?? "Unknown");

            PacketArrivalHistogram arrivals = _netManager.ReadIntervals;
            MetricRegistry.Set(ClientMetrics.ReadIntervalSamples, arrivals.Count);
            MetricRegistry.Set(ClientMetrics.ReadIntervalMeanMs, arrivals.MeanMs);
            MetricRegistry.Set(ClientMetrics.ReadIntervalP50Ms, arrivals.PercentileMs(50));
            MetricRegistry.Set(ClientMetrics.ReadIntervalP95Ms, arrivals.PercentileMs(95));
            MetricRegistry.Set(ClientMetrics.ReadIntervalP99Ms, arrivals.PercentileMs(99));
            MetricRegistry.Set(ClientMetrics.ReadIntervalMaxMs, arrivals.MaxMs);

            // Drive the time-sync state machine: burst during login, then background pacer.
            PollClock();

            if (Clock is { Synchronised: true })
            {
                MetricRegistry.Set(ClientMetrics.ClockOffsetMs, Clock.OffsetMs);
                MetricRegistry.Set(ClientMetrics.ClockRttMs, Clock.RttMedianMs);
                MetricRegistry.Set(ClientMetrics.ClockJitterMs, Clock.JitterMs);
                MetricRegistry.Set(ClientMetrics.ClockSynchronised, true);

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

            if (_ticks++ - _lastKeepAliveTime > 200)
            {
                SendPacket(KeepAlivePacket.Get());
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
            SendPacket(TimeSyncRequestC2SPacket.Get(probe.Value.Sequence, probe.Value.ClientSendTime));
        }
    }

    public override void onTimeSyncResponse(TimeSyncResponseS2CPacket packet)
    {
        // T3 was stamped by Connection.Reading before queueing. The handler reads it rather than
        // stamping its own, because it runs on the game thread up to a tick later.
        Clock?.Complete(packet.Sequence, packet.ClientSendTime, packet.ServerRecvTime,
            packet.ServerSendTime, packet.ClientRecvTime);
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

    public override void onTickStamp(TickStampS2CPacket packet)
    {
        // Every entity update read after this and before the next stamp describes this instant.
        // Phase 4 will attach it to snapshots; for now it is recorded so the F3 overlay can show
        // that the stream really is stamped, and how far behind the client's estimate of server
        // time the newest batch is.
        CurrentBatchServerTimeMs = packet.ServerTimeMs;
        TickStampsReceived++;
        MetricRegistry.Set(ClientMetrics.TickStampsReceived, TickStampsReceived);
    }

    /// <summary>
    ///     Server-clock instant of the most recent <see cref="TickStampS2CPacket" />, or 0 if the
    ///     stream has never been stamped. Zero is the signal that this server does not stamp — an
    ///     older OmniBlock build, or the loopback path — and that phase 4's interpolation must fall
    ///     back to the legacy move-toward-target behaviour rather than interpolate against a
    ///     timeline that does not exist.
    /// </summary>
    public long CurrentBatchServerTimeMs { get; private set; }

    /// <summary>Count of stamps seen, for the overlay. Also distinguishes "not stamped" from
    ///     "stamped once, long ago".</summary>
    public long TickStampsReceived { get; private set; }

    public override void onHello(LoginHelloPacket packet)
    {
        _logger.LogInformation($"[Client] Received onHello from server (id: {packet.ProtocolVersion})");
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

    public override void onItemEntitySpawn(ItemEntitySpawnS2CPacket packet)
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

    public override void onEntitySpawn(EntitySpawnS2CPacket packet)
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

    public override void onLightningEntitySpawn(GlobalEntitySpawnS2CPacket packet)
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

    public override void onPaintingEntitySpawn(PaintingEntitySpawnS2CPacket packet)
    {
        Entity ent = HangingArtBehavior.HangAt(_worldClient, packet.XPosition, packet.YPosition, packet.ZPosition, packet.Direction, packet.Title);
        _worldClient.ForceEntity(packet.EntityId, ent);
    }

    public override void onEntityVelocityUpdate(EntityVelocityUpdateS2CPacket packet)
    {
        Entity? ent = GetEntityById(packet.EntityId);
        ent?.SetVelocityClient(packet.MotionX / 8000.0D, packet.MotionY / 8000.0D, packet.MotionZ / 8000.0D);
    }

    public override void onEntityTrackerUpdate(EntityTrackerUpdateS2CPacket packet)
    {
        Entity? ent = GetEntityById(packet.EntityId);
        if (ent == null || packet.Data == null || packet.Data.Length == 0)
        {
            return;
        }

        ent.DataSynchronizer.ApplyChanges(new MemoryStream(packet.Data));
    }

    public override void onPlayerSpawn(PlayerSpawnS2CPacket packet)
    {
        double x = packet.XPosition / 32.0D;
        double y = packet.YPosition / 32.0D;
        double z = packet.ZPosition / 32.0D;
        float rotation = packet.Rotation * 360 / 256.0F;
        float pitch = packet.Pitch * 360 / 256.0F;
        OtherPlayerEntity ent = new(_context.WorldHost.World, packet.Name);
        ent.PrevX = ent.LastTickX = ent.TrackedPosX = packet.XPosition;
        ent.PrevY = ent.LastTickY = ent.TrackedPosY = packet.YPosition;
        ent.PrevZ = ent.LastTickZ = ent.TrackedPosZ = packet.ZPosition;
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

    public override void onEntityPosition(EntityPositionS2CPacket packet)
    {
        Entity? ent = GetEntityById(packet);
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

    public override void onEntity(EntityS2CPacket packet)
    {
        Entity? ent = GetEntityById(packet);
        if (ent != null)
        {
            RetargetEntity(ent, ent.Yaw, ent.Pitch);
        }
    }

    public override void onEntity(EntityRotateS2CPacket packet)
    {
        Entity? ent = GetEntityById(packet);
        if (ent != null)
        {
            float yaw = packet.Yaw * 360 / 256.0F;
            float pitch = packet.Pitch * 360 / 256.0F;
            RetargetEntity(ent, yaw, pitch);
        }
    }

    public override void onEntity(EntityMoveRelativeS2CPacket s2CPacket)
    {
        Entity? ent = GetEntityById(s2CPacket);
        if (ent != null)
        {
            ent.TrackedPosX += s2CPacket.DeltaX;
            ent.TrackedPosY += s2CPacket.DeltaY;
            ent.TrackedPosZ += s2CPacket.DeltaZ;
            RetargetEntity(ent, ent.Yaw, ent.Pitch);
        }
    }

    public override void onEntity(EntityRotateAndMoveRelativeS2CPacket s2CPacket)
    {
        Entity? ent = GetEntityById(s2CPacket);
        if (ent != null)
        {
            ent.TrackedPosX += s2CPacket.DeltaX;
            ent.TrackedPosY += s2CPacket.DeltaY;
            ent.TrackedPosZ += s2CPacket.DeltaZ;
            float yaw = s2CPacket.Yaw * 360 / 256.0F;
            float pitch = s2CPacket.Pitch * 360 / 256.0F;
            RetargetEntity(ent, yaw, pitch);
        }
    }

    public override void onEntityDestroy(EntityDestroyS2CPacket packet)
    {
        Interpolation.Forget(packet.EntityId);
        _worldClient.RemoveEntityFromWorld(packet.EntityId);
    }

    public override void onPlayerMove(PacketPlayerMoveAbstract packet)
    {
        // Previously this called ent.SetPositionAndAngles(x, y, z, yaw, pitch);

        ClientPlayerEntity? ent = _context.PlayerHost.Player;
        if (ent == null) return;

        ent.CameraOffset = 0.0F;

        if (packet is IPlayerMovePos packetMove)
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

        SendPacket(packet);
        if (!_terrainLoaded)
        {
            ent.PrevX = ent.X;
            ent.PrevY = ent.Y;
            ent.PrevZ = ent.Z;
            _terrainLoaded = true;
            _context.Navigator.Navigate(null);
        }

    }

    public override void onChunkStatusUpdate(ChunkStatusUpdateS2CPacket packet)
    {
        _worldClient.UpdateChunk(packet.X, packet.Z, packet.Load);
    }

    public override void onChunkDeltaUpdate(ChunkDeltaUpdateS2CPacket packet)
    {
        Chunk chunk = _worldClient.BlockHost.GetChunk(packet.X, packet.Z);
        int x = packet.X * 16;
        int y = packet.Z * 16;

        for (int i = 0; i < packet.Count; ++i)
        {
            short positions = packet.Positions[i];
            int blockRawId = packet.BlockRawIds[i] & 255;
            byte metadata = packet.BlockMetadata[i];
            int blockX = positions >> 12 & 15;
            int blockZ = positions >> 8 & 15;
            int blockY = positions & 255;
            chunk.SetBlock(blockX, blockY, blockZ, blockRawId, metadata);
            _worldClient.ClearBlockResets(blockX + x, blockY, blockZ + y, blockX + x, blockY, blockZ + y);
            _worldClient.setBlocksDirty(blockX + x, blockY, blockZ + y, blockX + x, blockY, blockZ + y);
        }

    }

    public override void handleChunkData(ChunkDataS2CPacket packet)
    {
        _worldClient.ClearBlockResets(packet.X, packet.Y, packet.Z, packet.X + packet.SizeX - 1, packet.Y + packet.SizeY - 1, packet.Z + packet.SizeZ - 1);
        _worldClient.HandleChunkDataUpdate(packet.X, packet.Y, packet.Z, packet.SizeX, packet.SizeY, packet.SizeZ, packet.ChunkData);
    }

    public override void onBlockUpdate(BlockUpdateS2CPacket packet)
    {
        _worldClient.SetBlockWithMetaFromPacket(packet.X, packet.Y, packet.Z, packet.BlockRawId, packet.BlockMetadata);
    }

    public override void onDisconnect(DisconnectPacket packet)
    {
        _netManager.disconnect("disconnect.kicked");
        Disconnected = true;
        _context.WorldHost.ChangeWorld(null);
        _context.Navigator.Navigate(_context.Factory.CreateFailedScreen("disconnect.disconnected", string.Format(Translations.Get("disconnect.genericReason"), packet.Reason), [packet.Reason]));
    }

    public override void onDisconnected(string reason, object[]? args)
    {
        if (!Disconnected)
        {
            Disconnected = true;
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
        }
    }

    public void AddToSendQueue(Packet packet)
    {
        SendPacket(packet);
    }

    public override void onItemPickupAnimation(ItemPickupAnimationS2CPacket packet)
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

    public override void onChatMessage(ChatMessagePacket packet)
    {
        _context.AddChatMessage(packet.ChatMessage);
    }

    public override void onEntityAnimation(EntityAnimationPacket packet)
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

    public override void onPlayerSleepUpdate(PlayerSleepUpdateS2CPacket packet)
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
        AddToSendQueue(LoginHelloPacket.Get(_context.Session.username, 14, LoginHelloPacket.BETASHARP_CLIENT_SIGNATURE, 0));
    }

    public void Disconnect()
    {
        Disconnected = true;
        _netManager.disconnect("disconnect.closed");
    }

    public override void onLivingEntitySpawn(LivingEntitySpawnS2CPacket packet)
    {
        double x = packet.XPosition / 32.0D;
        double y = packet.YPosition / 32.0D;
        double z = packet.ZPosition / 32.0D;
        float yaw = packet.Yaw * 360 / 256.0F;
        float pitch = packet.Pitch * 360 / 256.0F;
        EntityLiving ent = (EntityLiving)EntityRegistry.Create(packet.Type, _context.WorldHost.World);
        ent.TrackedPosX = packet.XPosition;
        ent.TrackedPosY = packet.YPosition;
        ent.TrackedPosZ = packet.ZPosition;
        ent.ID = packet.EntityId;
        ent.SetPositionAndAngles(x, y, z, yaw, pitch);
        ent.LastTickX = ent.X;
        ent.LastTickY = ent.Y;
        ent.LastTickZ = ent.Z;
        ent.InterpolateOnly = true;
        _worldClient.ForceEntity(packet.EntityId, ent);
        ent.DataSynchronizer.ApplyChanges(new MemoryStream(packet.Data));
    }

    public override void onWorldTimeUpdate(WorldTimeUpdateS2CPacket packet)
    {
        _context.WorldHost.World?.SetTime(packet.Time);
    }

    public override void onPlayerSpawnPosition(PlayerSpawnPositionS2CPacket packet)
    {
        _context.PlayerHost.Player.SetSpawnPos(new Vec3i(packet.X, packet.Y, packet.Z));
        _context.WorldHost.World?.Properties.SetSpawn(packet.X, packet.Y, packet.Z);
    }

    public override void onEntityVehicleSet(EntityVehicleSetS2CPacket packet)
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

    public override void onEntityStatus(EntityStatusS2CPacket packet)
    {
        Entity? ent = GetEntityById(packet.EntityId);
        ent?.ProcessServerEntityStatus(packet.EntityStatus);

    }

    private Entity? GetEntityById(IPacketEntity entityId) => GetEntityById(entityId.EntityId);
    private Entity? GetEntityById(int entityId)
    {
        if (_context.PlayerHost.Player == null || _worldClient == null)
        {
            return null;
        }

        return entityId == _context.PlayerHost.Player.ID ? _context.PlayerHost.Player : _worldClient.GetEntity(entityId);
    }

    public override void onHealthUpdate(HealthUpdateS2CPacket packet)
    {
        _context.PlayerHost.Player.setHealth(packet.HealthMp);
    }

    public override void onPlayerRespawn(PlayerRespawnPacket packet)
    {
        if (packet.DimensionId != _context.PlayerHost.Player.DimensionId)
        {
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

    public override void onExplosion(ExplosionS2CPacket packet)
    {
        Explosion explosion = new(_context.WorldHost.World, null, packet.ExplosionX, packet.ExplosionY, packet.ExplosionZ, packet.ExplosionSize)
        {
            destroyedBlockPositions = packet.DestroyedBlockPositions
        };
        explosion.doExplosionB(true);
    }

    public override void onOpenScreen(OpenScreenS2CPacket packet)
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

    public override void onScreenHandlerSlotUpdate(ScreenHandlerSlotUpdateS2CPacket packet)
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

    public override void onScreenHandlerAcknowledgement(ScreenHandlerAcknowledgementPacket packet)
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
                AddToSendQueue(ScreenHandlerAcknowledgementPacket.Get(packet.SyncId, packet.ActionType, true));
            }
        }

    }

    public override void onInventory(InventoryS2CPacket packet)
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

    public override void handleUpdateSign(UpdateSignPacket packet)
    {
        if (_context.WorldHost.World.BlockHost.IsPosLoaded(packet.X, packet.Y, packet.Z))
        {
            BlockEntitySign? signEntity = _context.WorldHost.World.Entities.GetBlockEntity<BlockEntitySign>(packet.X, packet.Y, packet.Z);

            if (signEntity != null)
            {
                for (int i = 0; i < 4; ++i)
                {
                    signEntity.Texts[i] = packet.Text[i];
                }

                signEntity.MarkDirty();
            }
        }
    }

    public override void onScreenHandlerPropertyUpdate(ScreenHandlerPropertyUpdateS2CPacket packet)
    {
        handle(packet);
        ClientPlayerEntity player = _context.PlayerHost.Player;
        if (player.CurrentScreenHandler != null && player.CurrentScreenHandler.SyncId == packet.SyncId)
        {
            player.CurrentScreenHandler.setProperty(packet.PropertyId, packet.Value);
        }

    }

    public override void onEntityEquipmentUpdate(EntityEquipmentUpdateS2CPacket packet)
    {
        Entity? ent = GetEntityById(packet.EntityId);
        ent?.SetEquipmentStack(packet.Slot, packet.ItemRawId, packet.ItemDamage);

    }

    public override void onCloseScreen(CloseScreenS2CPacket packet)
    {
        _context.PlayerHost.Player.closeHandledScreen();
    }

    public override void onPlayNoteSound(PlayNoteSoundS2CPacket packet)
    {
        _context.WorldHost.World?.Broadcaster.PlayNote(packet.XLocation, packet.YLocation, packet.ZLocation, packet.InstrumentType, packet.Pitch);
    }

    public override void onGameStateChange(GameStateChangeS2CPacket packet)
    {
        int reason = packet.Reason;
        if (reason >= 0 && reason < GameStateChangeS2CPacket.Reasons.Length && GameStateChangeS2CPacket.Reasons[reason] != null)
        {
            _context.PlayerHost.Player.SendMessage(GameStateChangeS2CPacket.Reasons[reason]);
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

    public override void onMapUpdate(MapUpdateS2CPacket packet)
    {
        if (packet.ItemRawId == Item.ByName("map").Id)
        {
            MapBehavior.GetMapState(packet.MapId, _context.WorldHost.World).UpdateData(packet.UpdateData);
        }
        else
        {
            _logger.LogInformation($"Unknown itemid: {packet.MapId}");
        }

    }

    public override void onWorldEvent(WorldEventS2CPacket packet)
    {
        _context.WorldHost.World?.Broadcaster.WorldEvent(packet.EventId, packet.X, packet.Y, packet.Z, packet.Data);
    }

    public override void onIncreaseStat(IncreaseStatS2CPacket packet)
    {
        try
        {
            StatBase stat = Stats.Stats.GetStatById(packet.StatId);
            ((EntityClientPlayerMP)_context.PlayerHost.Player).IncreaseRemoteStat(stat, packet.Amount);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex, "Unknown stat id in IncreaseStatS2CPacket: {StatId}", packet.StatId);
        }
    }

    public override void onPlayerConnectionUpdate(PlayerConnectionUpdateS2CPacket packet)
    {
        if (packet.Type == PlayerConnectionUpdateS2CPacket.ConnectionUpdateType.Leave)
        {
            Entity? ent = _worldClient.GetEntity(packet.EntityId);
            EntityRenderDispatcher.Instance.SkinManager?.Release(packet.Name);
        }
    }

    public override void onRegistryData(RegistryDataS2CPacket packet)
    {
        _clientRegistries.Accumulate(packet);
    }

    public override void onFinishConfiguration(FinishConfigurationS2CPacket packet)
    {
        _logger.LogInformation("Configuration finished");
    }

    public override void onPlayerGameModeUpdate(PlayerGameModeUpdateS2CPacket packet)
    {
        Holder<GameMode>? gameMode = _clientRegistries.Get(RegistryKeys.GameModes, new ResourceLocation(packet.Namespace, packet.GameModeName));
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
