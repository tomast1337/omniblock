using Microsoft.Extensions.Logging;
using OmniBlock.Blocks.Entities;
using OmniBlock.Blocks.Materials;
using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.Network;
using OmniBlock.Network.Messages;
using OmniBlock.Network.Packets;
using OmniBlock.Server.Command;
using OmniBlock.Server.Entities;
using OmniBlock.Server.Internal;
using OmniBlock.Server.Worlds;
using OmniBlock.Util;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Lod;

namespace OmniBlock.Server.Network;

public class ServerPlayNetworkHandler : NetHandler, ICommandOutput
{
    /// <summary>
    ///     Ticks a hovering/near-stationary vertical delta is tolerated before <see cref="onPlayerMove" />
    ///     kicks for flying, cut down from vanilla's 80 (~4 s). 80 ticks of unrestricted flight before
    ///     any action is taken is a wide-open window; 20 (~1 s) still tolerates a legitimate lag spike
    ///     or elevator/piston ride without being long enough to be useful as a fly hack.
    /// </summary>
    private const int MaxFloatingTicks = 20;
    private const int MaximumTerrainLodResponsesPerRequest = 4;

    private readonly ILogger<ServerPlayNetworkHandler> _logger = Log.Instance.For<ServerPlayNetworkHandler>();
    private readonly OmniBlockServer server;
    private readonly Dictionary<int, short> transactions = new();
    private readonly TerrainLodSendPacer _terrainLodPacer = new();
    private readonly TerrainLodRequestQueue _terrainLodRequests = new();

    private long _lastMoveBudgetRefillMs = Environment.TickCount64;

    /// <summary>See <see cref="MoveSpeedBudget" /> — squared-distance token bucket for the "moved too quickly" check in <see cref="onPlayerMove" />.</summary>
    private double _moveBudgetSq = MoveSpeedBudget.MaxDistanceSqPerTick;

    public Connection connection;
    public bool disconnected;
    private int floatingTime;
    private int lastKeepAliveTime;
    private bool moved;
    private ServerPlayerEntity player;
    private bool teleported = true;
    private double teleportTargetX;
    private double teleportTargetY;
    private double teleportTargetZ;
    private int ticks;

    public ServerPlayNetworkHandler(OmniBlockServer server, Connection connection, ServerPlayerEntity player)
    {
        this.server = server;
        this.connection = connection;
        connection.setNetworkHandler(this);
        this.player = player;
        player.NetworkHandler = this;

        MessageHandlers.On<TimeSyncRequestMessage>(onTimeSyncRequest);
        MessageHandlers.On<ChunkCacheOfferMessage>(onChunkCacheOffer);
        MessageHandlers.On<TerrainLodTileRequestMessage>(onTerrainLodTileRequest);
        MessageHandlers.On<SnapshotAckMessage>(ack => player.SnapshotStream.Acknowledge(ack.Sequence));
        MessageHandlers.On<InteractEntityMessage>(interact => InteractWithEntity(interact.EntityId, interact.Action, interact.RenderTimeMs));
        MessageHandlers.On<PlayerActionMessage>(onPlayerAction);
        MessageHandlers.On<InteractBlockMessage>(onInteractBlock);
        MessageHandlers.On<SelectedSlotMessage>(onSelectedSlot);
        MessageHandlers.On<ClientCommandMessage>(onClientCommand);
        MessageHandlers.On<PlayerInputMessage>(input => player.updateInput(input));
        MessageHandlers.On<ClickSlotMessage>(onClickSlot);
        MessageHandlers.On<EntityAnimationMessage>(onEntityAnimation);
        MessageHandlers.On<CloseScreenMessage>(_ => player.onHandledScreenClosed());
        MessageHandlers.On<ScreenHandlerAckMessage>(onScreenHandlerAck);
        MessageHandlers.On<UpdateSignMessage>(onUpdateSign);
        MessageHandlers.On<ChatMessage>(onChatMessage);
        MessageHandlers.On<DisconnectMessage>(onDisconnect);
        MessageHandlers.On<PlayerRespawnMessage>(onPlayerRespawn);

        // One handler, four registrations. Dispatch is keyed by concrete type, and the four variants
        // differ only in which fields they carry — which onPlayerMove already reads through
        // IPlayerMovePosition and IPlayerMoveLook rather than by asking what it was handed.
        MessageHandlers.On<PlayerMoveMessage>(onPlayerMove);
        MessageHandlers.On<PlayerMovePositionMessage>(onPlayerMove);
        MessageHandlers.On<PlayerMoveLookMessage>(onPlayerMove);
        MessageHandlers.On<PlayerMoveFullMessage>(onPlayerMove);
    }

    /// <summary>
    ///     The server's table, shared by every connection. Client-to-server messages resolve against
    ///     the same ordering the client was told during configuration.
    /// </summary>
    public override MessageRegistry? Messages => server.Messages;

    /// <summary>
    ///     Whether <see cref="SendMessage" /> would reach this peer.
    ///     <para>
    ///         Exists so a caller with a legacy fallback can choose between the two rather than
    ///         handing over a message and watching it be dropped. <see cref="SendMessage" /> is
    ///         correct to drop silently — a peer not implementing a message is the designed outcome
    ///         — but a chunk that vanishes for that reason is a hole in the world.
    ///     </para>
    /// </summary>
    public bool CanSendMessages => Messages is { Negotiated: true };

    /// <summary>
    ///     Whether it is worth encoding a payload smaller before sending it here.
    ///     <para>
    ///         False on loopback, where the message is handed over as an object and never serialised.
    ///         Encoding a chunk smaller for singleplayer costs the encode and the matching decode on
    ///         the other side to save bytes that were never going to exist.
    ///     </para>
    /// </summary>
    public bool WantsCompactPayloads => CanSendMessages && !connection.IsInternal;

    public void SendMessage(string message) => SendMessage(new ChatMessage
    {
        Text = "§7" + message
    });

    public string Name => player.Name;
    public byte PermissionLevel => server.playerManager.isOperator(player.Name) ? (byte)4 : (byte)0;

    public void tick()
    {
        moved = false;
        connection.tick();

        if (!moved) player.IdleTick();

        if (ticks++ - lastKeepAliveTime > 20) SendMessage(new KeepAliveMessage());
    }

    public void disconnect(string reason)
    {
        player.onDisconnect();
        SendMessage(new DisconnectMessage
        {
            Reason = reason
        });
        connection.disconnect();
        server.playerManager.disconnect(player);
        server.playerManager.sendToAll(new PlayerConnectionUpdateMessage
        {
            EntityId = player.ID,
            Type = PlayerConnectionUpdateMessage.UpdateType.Leave,
            Name = player.Name
        });
        server.playerManager.sendToAll(new ChatMessage
        {
            Text = "§e" + player.Name + " left the game."
        });
        disconnected = true;
    }


    /// <summary>
    ///     Chunk hashes the client claims to already hold.
    ///     <para>
    ///         Replaces rather than merges. An offer describes the client's cache around where it now
    ///         is, so a later one supersedes an earlier one, and merging would grow this table for
    ///         the life of the session with entries for places the player has left.
    ///     </para>
    ///     <para>
    ///         Nothing here is trusted. Every hash is checked against the server's own copy of the
    ///         chunk before anything is skipped, so a client that lies only denies itself data.
    ///     </para>
    /// </summary>
    private void onChunkCacheOffer(ChunkCacheOfferMessage offer)
    {
        player.OfferedChunkHashes.Clear();

        foreach (var (position, hash) in offer.Entries)
        {
            player.OfferedChunkHashes[position] = hash;
        }

        _logger.LogDebug(
            "{Player} offered {Count} cached chunk hashes.", player.Name, offer.Entries.Count);
    }

    private void onTerrainLodTileRequest(TerrainLodTileRequestMessage request)
    {
        if (request.Dimension != player.DimensionId) return;
        var world = server.getWorld(player.DimensionId);
        var identity = world.TerrainLodIdentity;
        if (identity is null) return;
        var identityFingerprint = identity.CompatibilityFingerprint;
        if (!string.Equals(request.CacheIdentity, identityFingerprint,
                StringComparison.Ordinal))
        {
            if (request.Keys.Length > 0)
                QueueTerrainLodStatus(
                    request.Keys[0],
                    TerrainLodTileStatus.Incompatible,
                    identityFingerprint,
                    "The requested terrain LOD cache identity does not match this dimension.");
            return;
        }
        if (identity.GetRequestIncompatibility(
                request.MaximumSpatialLevel,
                request.QualityPolicyVersion,
                TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel) is { } incompatibility)
        {
            if (request.Keys.Length > 0)
                QueueTerrainLodStatus(
                    request.Keys[0],
                    TerrainLodTileStatus.Incompatible,
                    identityFingerprint,
                    incompatibility);
            return;
        }
        var playerChunkX = player.X / 16.0;
        var playerChunkZ = player.Z / 16.0;
        var accepted = 0;
        foreach (var key in request.Keys.Distinct())
        {
            // Level-zero/one records reveal almost full chunk detail and belong to ordinary chunk
            // streaming. The distant lane begins at a 4x4-chunk aggregate and never generates on
            // demand; it can only return an already-approved persistent server record.
            if (key.Level < TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel ||
                key.Level > request.MaximumSpatialLevel ||
                key.DistanceTo(playerChunkX, playerChunkZ) >
                TerrainLodSpatialPolicy.MaximumSupportedHorizonChunks)
                continue;
            var queued = new QueuedTerrainLodRequest(
                request.Dimension,
                identityFingerprint,
                request.MaximumSpatialLevel,
                request.QualityPolicyVersion,
                key);
            switch (_terrainLodRequests.Enqueue(queued))
            {
                case TerrainLodRequestEnqueueResult.Added:
                    if (++accepted >= MaximumTerrainLodResponsesPerRequest) return;
                    break;
                case TerrainLodRequestEnqueueResult.Full:
                    // The queue itself is the memory and amplification bound. Do not bypass it to
                    // report that it is full; the client's ordinary retry policy will ask again.
                    return;
            }
        }
    }

    internal int PendingTerrainLodRequestCount => _terrainLodRequests.Count;

    /// <summary>
    ///     Attempts one queued distant-terrain reply. Called by the player manager's round-robin
    ///     scheduler only after gameplay chunks have had their tick's send opportunity.
    /// </summary>
    internal TerrainLodFlushResult FlushOneTerrainLodResponse(TerrainLodGlobalSendPacer globalPacer)
    {
        if (!_terrainLodRequests.TryPeek(out var request))
            return TerrainLodFlushResult.NoWork;

        // A dimension transfer invalidates the old disclosure context. Discard it without emitting
        // a response from the new dimension; the new identity message causes the client to replan.
        if (request.Dimension != player.DimensionId)
        {
            _terrainLodRequests.TryDequeue(out _);
            return TerrainLodFlushResult.Progress;
        }

        // UDP needs the ordered-channel depth here because handing another fragmented payload to
        // the socket can still consume link capacity. Loopback already has an isolated bulk inbox
        // which is promoted only after its normal inbox drains; treating that normal inbox as a
        // producer gate as well would starve LOD whenever tick/entity traffic is continuous.
        var gameplayBacklog = connection.IsInternal ? 0 : getWorldPacketBacklog();
        var bulkBacklog = connection.getBulkPacketBacklog();
        if (!_terrainLodPacer.CanSend(
                0, player.PendingChunkSendCount, gameplayBacklog, bulkBacklog))
            return TerrainLodFlushResult.Blocked;

        var world = server.getWorld(player.DimensionId);
        var identity = world.TerrainLodIdentity;
        if (request.ImmediateStatus is { } immediateStatus)
            return TrySendQueuedTerrainLodResponse(
                CreateTerrainLodStatus(
                    request.Tile,
                    immediateStatus,
                    request.CacheIdentity,
                    request.Diagnostic),
                globalPacer,
                gameplayBacklog,
                bulkBacklog);

        if (identity is null ||
            !string.Equals(request.CacheIdentity, identity.CompatibilityFingerprint,
                StringComparison.Ordinal) ||
            identity.GetRequestIncompatibility(
                request.MaximumSpatialLevel,
                request.QualityPolicyVersion,
                TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel) is not null)
        {
            _terrainLodRequests.TryDequeue(out _);
            return TerrainLodFlushResult.Progress;
        }

        // Disclosure is decided when bytes leave, not merely when the request arrived. A player
        // can teleport or run far enough while this request waits that the tile no longer belongs
        // to the server-approved horizon around their current position.
        if (request.Tile.Level < TerrainLodSpatialPolicy.MinimumRemoteSpatialLevel ||
            request.Tile.Level > request.MaximumSpatialLevel ||
            request.Tile.DistanceTo(player.X / 16.0, player.Z / 16.0) >
            TerrainLodSpatialPolicy.MaximumSupportedHorizonChunks)
        {
            _terrainLodRequests.TryDequeue(out _);
            return TerrainLodFlushResult.Progress;
        }

        try
        {
            TerrainLodTileAvailability availability;
            Message response;
            if (WantsCompactPayloads)
            {
                availability = world.GetTerrainLodPayload(request.Tile, out var payload);
                response = availability == TerrainLodTileAvailability.Ready && payload is not null
                    ? TerrainLodTileMessage.FromCompressed(
                        player.DimensionId, payload, request.CacheIdentity)
                    : CreateTerrainLodStatus(
                        request.Tile,
                        availability == TerrainLodTileAvailability.Missing
                            ? TerrainLodTileStatus.Missing
                            : TerrainLodTileStatus.Pending,
                        request.CacheIdentity);
            }
            else
            {
                availability = world.GetTerrainLodCoverage(request.Tile, out var tile);
                response = availability == TerrainLodTileAvailability.Ready && tile is not null
                    ? TerrainLodTileMessage.Loopback(
                        player.DimensionId, tile, request.CacheIdentity)
                    : CreateTerrainLodStatus(
                        request.Tile,
                        availability == TerrainLodTileAvailability.Missing
                            ? TerrainLodTileStatus.Missing
                            : TerrainLodTileStatus.Pending,
                        request.CacheIdentity);
            }

            return TrySendQueuedTerrainLodResponse(
                response, globalPacer, gameplayBacklog, bulkBacklog);
        }
        catch (InvalidDataException error)
        {
            _terrainLodRequests.TryDequeue(out _);
            _logger.LogWarning(error, "Terrain LOD tile {Tile} could not be sent to {Player}.",
                request.Tile, player.Name);
            return TerrainLodFlushResult.Progress;
        }
    }

    private void QueueTerrainLodStatus(
        TerrainLodTileKey key,
        TerrainLodTileStatus status,
        string cacheIdentity,
        string diagnostic = "")
    {
        var identity = server.getWorld(player.DimensionId).TerrainLodIdentity;
        _terrainLodRequests.Enqueue(new QueuedTerrainLodRequest(
            player.DimensionId,
            cacheIdentity,
            identity?.MaximumSpatialLevel ?? TerrainLodSpatialPolicy.MaximumSupportedSpatialLevel,
            identity?.QualityPolicyVersion ?? TerrainLodSpatialPolicy.CurrentQualityPolicyVersion,
            key,
            status,
            diagnostic));
    }

    private TerrainLodFlushResult TrySendQueuedTerrainLodResponse(
        Message response,
        TerrainLodGlobalSendPacer globalPacer,
        int gameplayBacklog,
        int bulkBacklog)
    {
        var bytes = WantsCompactPayloads ? response.Size() : 0;
        if (!_terrainLodPacer.CanSend(
                bytes, player.PendingChunkSendCount, gameplayBacklog, bulkBacklog) ||
            !globalPacer.CanSend(bytes))
            return TerrainLodFlushResult.Blocked;

        _terrainLodPacer.Record(bytes);
        globalPacer.Record(bytes);
        _terrainLodRequests.TryDequeue(out _);
        SendMessage(response);
        return TerrainLodFlushResult.Progress;
    }

    private TerrainLodTileStatusMessage CreateTerrainLodStatus(
        TerrainLodTileKey key,
        TerrainLodTileStatus status,
        string cacheIdentity,
        string diagnostic = "") =>
        new()
        {
            Dimension = player.DimensionId,
            CacheIdentity = cacheIdentity,
            Tile = key,
            Status = status,
            Diagnostic = diagnostic
        };

    /// <summary>
    ///     Echoes a probe. The server is stateless here: it returns every field it cannot derive and
    ///     lets the client validate the response against its own pending table.
    /// </summary>
    private void onTimeSyncRequest(TimeSyncRequestMessage request)
    {
        // T1 came in on the envelope, stamped on the read thread before queueing. T2 is not set
        // here at all — the response declares NeedsSendTimestamp and Connection.WritePacket fills it
        // in immediately before the bytes reach the socket, which is as late as it can be placed.
        SendMessage(new TimeSyncResponseMessage
        {
            Sequence = request.Sequence,
            ClientSendTime = request.ClientSendTime,
            ServerRecvTime = request.TransportReceivedAtMs
        });
    }

    /// <summary>
    ///     Sends a message over this connection, or drops it when the client never advertised the
    ///     key. Dropping is the designed outcome for a peer that does not implement a message, not
    ///     an error to report.
    /// </summary>
    public void SendMessage(Message message)
    {
        var registry = Messages;
        if (registry is null || !registry.Negotiated)
        {
            return;
        }

        connection.sendMessage(registry, message);
    }

    internal void SendTerrainLodIdentity()
    {
        if (server.CreateTerrainLodIdentityMessage(player.DimensionId) is { } message)
            SendMessage(message);
    }

    private void onPlayerMove(IPlayerMove packet)
    {
        var sWorld = server.getWorld(player.DimensionId);
        moved = true;
        if (!teleported)
        {
            var moveX = 0.0;
            var moveY = 0.0;
            var moveZ = 0.0;
            if (packet is IPlayerMovePosition packetMove)
            {
                moveX = packetMove.X;
                moveY = packetMove.Y;
                moveZ = packetMove.Z;
            }

            var teleportDeltaY = moveY - teleportTargetY;
            if (teleportDeltaY * teleportDeltaY < 0.01 && Math.Abs(moveZ - teleportTargetZ) + Math.Abs(moveX - teleportTargetX) < 0.001)
            {
                teleported = true;
            }
        }

        if (teleported)
        {
            var yaw = player.Yaw;
            var pitch = player.Pitch;

            if (packet is IPlayerMoveLook packetLook)
            {
                yaw = packetLook.Yaw;
                pitch = packetLook.Pitch;
            }

            if (player.Vehicle != null)
            {
                player.Vehicle.UpdatePassengerPosition();
                var vehicleX = player.X;
                var vehicleY = player.Y;
                var vehicleZ = player.Z;
                var moveX = 0.0;
                var moveZ = 0.0;

                if (packet is IPlayerMovePosition packetMove && packetMove.Y <= -999.0 && packetMove.EyeHeight <= -999.0)
                {
                    moveX = packetMove.X;
                    moveZ = packetMove.Z;
                }

                player.OnGround = packet.OnGround;
                player.PlayerTick(false);
                player.Move(moveX, 0.0, moveZ);
                player.SetPositionAndAngles(vehicleX, vehicleY, vehicleZ, yaw, pitch);
                player.VelocityX = moveX;
                player.VelocityZ = moveZ;
                if (player.Vehicle != null)
                {
                    sWorld.Entities.TickVehicleBypassingFilter(player.Vehicle, true);
                }

                if (player.Vehicle != null)
                {
                    player.Vehicle.UpdatePassengerPosition();
                }

                server.playerManager.updatePlayerChunks(player);
                teleportTargetX = player.X;
                teleportTargetY = player.Y;
                teleportTargetZ = player.Z;
                sWorld.Entities.UpdateEntity(player, true);
                return;
            }

            if (player.IsSleeping)
            {
                player.PlayerTick(false);
                player.SetPositionAndAngles(teleportTargetX, teleportTargetY, teleportTargetZ, player.Yaw, player.Pitch);
                sWorld.Entities.UpdateEntity(player, true);
                return;
            }

            var previousY = player.Y;
            teleportTargetX = player.X;
            teleportTargetY = player.Y;
            teleportTargetZ = player.Z;
            var targetX = player.X;
            var targetY = player.Y;
            var targetZ = player.Z;

            if (packet is IPlayerMovePosition packetMove2)
            {
                if (!(packetMove2.Y <= -999.0 && packetMove2.EyeHeight <= -999.0))
                {
                    targetX = packetMove2.X;
                    targetY = packetMove2.Y;
                    targetZ = packetMove2.Z;
                    var stanceHeight = packetMove2.EyeHeight - packetMove2.Y;
                    if (!player.IsSleeping && (stanceHeight > 1.65 || stanceHeight < 0.1))
                    {
                        disconnect("Illegal stance");
                        _logger.LogWarning($"{player.Name} had an illegal stance: {stanceHeight}");
                        return;
                    }

                    if (Math.Abs(packetMove2.X) > 3.2E7 || Math.Abs(packetMove2.Z) > 3.2E7)
                    {
                        disconnect("Illegal position");
                        return;
                    }
                }
            }

            player.PlayerTick(false);
            player.CameraOffset = 0.0F;
            player.SetPositionAndAngles(teleportTargetX, teleportTargetY, teleportTargetZ, yaw, pitch);
            if (!teleported)
            {
                return;
            }

            var deltaX = targetX - player.X;
            var deltaY = targetY - player.Y;
            var deltaZ = targetZ - player.Z;
            var movedDistanceSq = deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;

            var nowMs = Environment.TickCount64;
            double elapsedMs = nowMs - _lastMoveBudgetRefillMs;
            _lastMoveBudgetRefillMs = nowMs;

            var budgetResult = MoveSpeedBudget.Evaluate(_moveBudgetSq, elapsedMs, movedDistanceSq);
            _moveBudgetSq = budgetResult.RemainingBudgetSq;
            if (budgetResult.ExceededBudget)
            {
                _logger.LogWarning($"{player.Name} moved too quickly!");
                disconnect("You moved too quickly :( (Hacking?)");
                return;
            }

            var collisionPadding = 1 / 16f;
            var wasClear = sWorld.Entities.GetEntityCollisionsScratch(player, player.BoundingBox.Contract(collisionPadding, collisionPadding, collisionPadding)).Count == 0;
            player.Move(deltaX, deltaY, deltaZ);
            deltaX = targetX - player.X;
            deltaY = targetY - player.Y;
            if (deltaY > -0.5 || deltaY < 0.5)
            {
                deltaY = 0.0;
            }

            deltaZ = targetZ - player.Z;
            movedDistanceSq = deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
            var validMove = false;
            if (movedDistanceSq > 0.0625 && !player.IsSleeping)
            {
                validMove = true;
                _logger.LogWarning($"{player.Name} moved wrongly!");
                _logger.LogInformation($"Got position {targetX}, {targetY}, {targetZ}");
                _logger.LogInformation($"Expected {player.X}, {player.Y}, {player.Z}");
            }

            player.SetPositionAndAngles(targetX, targetY, targetZ, yaw, pitch);
            var isClearNow = sWorld.Entities.GetEntityCollisionsScratch(player, player.BoundingBox.Contract(collisionPadding, collisionPadding, collisionPadding)).Count == 0;
            if (wasClear && (validMove || !isClearNow) && !player.IsSleeping)
            {
                teleport(teleportTargetX, teleportTargetY, teleportTargetZ, yaw, pitch);
                return;
            }

            var flightCheckBox = player.BoundingBox.Expand(collisionPadding, collisionPadding, collisionPadding).Stretch(0.0, -0.55, 0.0);
            if (server.flightEnabled || sWorld.Reader.IsMaterialInBox(flightCheckBox, m => m != Material.Air))
            {
                floatingTime = 0;
            }
            else if (deltaY >= -0.03125)
            {
                floatingTime++;
                if (floatingTime > MaxFloatingTicks && player.GameMode.DisallowFlying)
                {
                    _logger.LogWarning($"{player.Name} was kicked for floating too long!");
                    disconnect("Flying is not enabled on this server");
                    return;
                }
            }

            player.OnGround = packet.OnGround;
            server.playerManager.updatePlayerChunks(player);
            player.handleFall(player.Y - previousY, packet.OnGround);
        }
    }

    public void teleport(double x, double y, double z, float yaw, float pitch)
    {
        teleported = false;
        teleportTargetX = x;
        teleportTargetY = y;
        teleportTargetZ = z;
        player.SetPositionAndAngles(x, y, z, yaw, pitch);

        // The server already owns the authoritative destination. Recenter chunk streaming now
        // instead of waiting for the client to echo this teleport in a movement packet. Waiting
        // creates a circular dependency at an unloaded destination: the client can be unable to
        // tick/send its acknowledgement until terrain arrives, while terrain would not be queued
        // until that acknowledgement (or a later movement) arrived.
        server.playerManager.updatePlayerChunks(player);

        player.NetworkHandler.SendMessage(new PlayerMoveFullMessage
        {
            X = x,
            Y = y + 1.62F,
            EyeHeight = y,
            Z = z,
            Yaw = yaw,
            Pitch = pitch,
            OnGround = false
        });
    }


    private void onPlayerAction(PlayerActionMessage packet)
    {
        var world = server.getWorld(player.DimensionId);
        if (packet.Action == 4)
        {
            player.DropSelectedItem();
        }
        else
        {
            var x = packet.X;
            int y = packet.Y;
            var z = packet.Z;

            if (packet.Action == 3)
            {
                if (MathHelper.GetDistSqr(player.X, player.Y, player.Z, x, y, z) < 256.0)
                {
                    player.NetworkHandler.SendMessage(new BlockUpdateMessage
                    {
                        X = x,
                        Y = (sbyte)y,
                        Z = z,
                        BlockRawId = (byte)world.Reader.GetBlockId(x, y, z),
                        BlockMetadata = (byte)world.Reader.GetBlockMeta(x, y, z)
                    });
                }

                return;
            }

            if (packet.Action == (byte)PlayerActionMessage.Actions.BlockClick || packet.Action == (byte)PlayerActionMessage.Actions.BlockBroken)
            {
                if (player.GameMode.BlockReach <= 0) return;
                var reach = player.GameMode.BlockReach + 1f;
                if (MathHelper.GetDistSqr(player.X, player.Y, player.Z, x, y, z) > reach * reach)
                {
                    return;
                }
            }

            if (packet.Action == (byte)PlayerActionMessage.Actions.BlockClick)
            {
                if (!CanBypassSpawnProtection(x, z, world))
                {
                    player.NetworkHandler.SendMessage(new BlockUpdateMessage
                    {
                        X = x,
                        Y = (sbyte)y,
                        Z = z,
                        BlockRawId = (byte)world.Reader.GetBlockId(x, y, z),
                        BlockMetadata = (byte)world.Reader.GetBlockMeta(x, y, z)
                    });
                }
                else
                {
                    player.InteractionManager.onBlockBreakingAction(x, y, z, packet.Direction);
                }
            }
            else if (packet.Action == (byte)PlayerActionMessage.Actions.BlockBroken)
            {
                player.InteractionManager.continueMining(x, y, z);
                if (world.Reader.GetBlockId(x, y, z) != 0)
                {
                    player.NetworkHandler.SendMessage(new BlockUpdateMessage
                    {
                        X = x,
                        Y = (sbyte)y,
                        Z = z,
                        BlockRawId = (byte)world.Reader.GetBlockId(x, y, z),
                        BlockMetadata = (byte)world.Reader.GetBlockMeta(x, y, z)
                    });
                }
            }
        }
    }

    private bool CanBypassSpawnProtection(int x, int z, ServerWorld world)
    {
        const int spawnProtection = 16;
        var spawnPos = world.Properties.GetSpawnPos();
        var notBlockedFromSpawnProtection = Math.Abs(x - spawnPos.X) > spawnProtection || Math.Abs(z - spawnPos.Z) > spawnProtection;
        notBlockedFromSpawnProtection = notBlockedFromSpawnProtection || world.BypassSpawnProtection || server is InternalServer || server.playerManager.isOperator(player.Name);
        return notBlockedFromSpawnProtection;
    }

    private void onInteractBlock(InteractBlockMessage packet)
    {
        var world = server.getWorld(player.DimensionId);
        var stack = player.Inventory.ItemInHand;
        if (packet.Side == 255)
        {
            if (stack == null)
            {
                return;
            }

            player.InteractionManager.interactItem(player, world, stack);
        }
        else
        {
            var x = packet.X;
            int y = packet.Y;
            var z = packet.Z;
            int side = packet.Side;

            if (teleported && CanBypassSpawnProtection(x, z, world) && player.GetSquaredDistance(x + 0.5, y + 0.5, z + 0.5) < 64.0)
            {
                player.InteractionManager.interactBlock(player, world, stack, x, y, z, side);
            }

            player.NetworkHandler.SendMessage(new BlockUpdateMessage
            {
                X = x,
                Y = (sbyte)y,
                Z = z,
                BlockRawId = (byte)world.Reader.GetBlockId(x, y, z),
                BlockMetadata = (byte)world.Reader.GetBlockMeta(x, y, z)
            });
            switch (side)
            {
                case 0:
                    y--;
                    break;
                case 1:
                    y++;
                    break;
                case 2:
                    z--;
                    break;
                case 3:
                    z++;
                    break;
                case 4:
                    x--;
                    break;
                case 5:
                    x++;
                    break;
            }

            player.NetworkHandler.SendMessage(new BlockUpdateMessage
            {
                X = x,
                Y = (sbyte)y,
                Z = z,
                BlockRawId = (byte)world.Reader.GetBlockId(x, y, z),
                BlockMetadata = (byte)world.Reader.GetBlockMeta(x, y, z)
            });
        }

        stack = player.Inventory.ItemInHand;
        if (stack != null && stack.Count == 0)
        {
            player.Inventory.Main[player.Inventory.SelectedSlot] = null;
        }

        player.SkipPacketSlotUpdates = true;
        player.Inventory.Main[player.Inventory.SelectedSlot] = ItemStack.Clone(player.Inventory.Main[player.Inventory.SelectedSlot]);
        var slot = player.CurrentScreenHandler.GetSlot(player.Inventory, player.Inventory.SelectedSlot);
        player.CurrentScreenHandler.SendContentUpdates();
        player.SkipPacketSlotUpdates = false;
        if (!ItemStack.AreEqual(player.Inventory.ItemInHand, packet.Stack))
        {
            SendMessage(new ScreenHandlerSlotMessage
            {
                SyncId = (sbyte)player.CurrentScreenHandler.SyncId,
                Slot = (short)slot.id,
                Stack = player.Inventory.ItemInHand
            });
        }
    }

    public override void onDisconnected(string reason, object[]? objects)
    {
        _logger.LogInformation($"{player.Name} lost connection: {reason}");
        server.playerManager.disconnect(player);
        server.playerManager.sendToAll(new PlayerConnectionUpdateMessage
        {
            EntityId = player.ID,
            Type = PlayerConnectionUpdateMessage.UpdateType.Leave,
            Name = player.Name
        });
        server.playerManager.sendToAll(new ChatMessage
        {
            Text = "§e" + player.Name + " left the game."
        });
        disconnected = true;
    }

    public override void handle(Packet packet)
    {
        _logger.LogWarning($"{GetType()} wasn't prepared to deal with a {packet.GetType()}");
        disconnect("Protocol error, unexpected packet");
    }

    public void SendPacket(Packet packet)
    {
        connection.sendPacket(packet);
        lastKeepAliveTime = ticks;
    }

    private void onSelectedSlot(SelectedSlotMessage packet)
    {
        if (packet.Slot >= 0 && packet.Slot <= InventoryPlayer.HotbarSize)
        {
            player.InteractionManager.UpdateMiningTool();
            player.Inventory.SelectedSlot = packet.Slot;
        }
        else
        {
            _logger.LogWarning($"{player.Name} tried to set an invalid carried item");
        }
    }

    private void onChatMessage(ChatMessage packet)
    {
        var msg = packet.Text;
        if (msg.Length > 100)
        {
            disconnect("Chat message too long");
        }
        else
        {
            msg = msg.Trim();

            for (var charIndex = 0; charIndex < msg.Length; charIndex++)
            {
                // Allow the section sign (§) for color/style codes as well as the standard allowed characters
                if (msg[charIndex] == (char)167) // '§'
                {
                    continue;
                }

                if (!ChatAllowedCharacters.IsAllowedCharacter(msg[charIndex]))
                {
                    disconnect("Illegal characters in chat");
                    return;
                }
            }

            if (msg.StartsWith("/"))
            {
                handleCommand(msg);
            }
            else
            {
                msg = "<" + player.Name + "> " + msg;
                _logger.LogInformation(msg);
                server.playerManager.sendToAll(new ChatMessage
                {
                    Text = msg
                });
            }
        }
    }

    private void handleCommand(string message)
    {
        if (message.ToLower().StartsWith("/me "))
        {
            var emote = "* " + player.Name + " " + message[message.IndexOf(" ")..].Trim();
            _logger.LogInformation(emote);
            server.playerManager.sendToAll(new ChatMessage
            {
                Text = emote
            });
        }
        else if (server is InternalServer || server.playerManager.isOperator(player.Name))
        {
            var commandText = message[1..];
            _logger.LogInformation($"{player.Name} issued server command: {commandText}");
            server.QueueCommands(commandText, this);
        }
        else
        {
            var commandText = message[1..];
            _logger.LogInformation($"{player.Name} tried command: {commandText}");
            SendMessage(new ChatMessage
            {
                Text = "§cYou do not have permission to use this command."
            });
        }
    }

    private void onEntityAnimation(EntityAnimationMessage message)
    {
        if (message.AnimationId == (byte)EntityAnimationMessage.EntityAnimation.SwingHand)
        {
            player.SwingHand();
        }
    }

    private void onClientCommand(ClientCommandMessage packet)
    {
        if (packet.Mode == 1)
        {
            player.SetSneaking(true);
        }
        else if (packet.Mode == 2)
        {
            player.SetSneaking(false);
        }
        else if (packet.Mode == 3)
        {
            player.WakeUp(false, true, true);
            teleported = false;
        }
    }

    private void onDisconnect(DisconnectMessage packet) => connection.disconnect("disconnect.quitting");

    public int getBlockDataSendQueueSize() => getWorldPacketBacklog();

    public int getWorldPacketBacklog() => connection.getWorldPacketBacklog();

    /// <summary>
    ///     Resolves a click on an entity, checking reach against where the clicking player actually
    ///     saw the target rather than against where it is now.
    ///     <para>
    ///         <b>Only the reach check is rewound.</b> The effect — damage, knockback, whatever the
    ///         interaction does — lands on the entity in the present, because that is the entity that
    ///         exists. Rewinding the world to apply an effect in the past would mean reconciling
    ///         everything that happened since, which is a different and much larger problem than the
    ///         one worth solving here: a hit that was visibly on target being rejected for latency
    ///         the player did not cause.
    ///     </para>
    ///     <para>
    ///         The attacker's own position is not rewound and must not be. They are authoritative
    ///         over it and it is already the position they had when they clicked; rewinding it too
    ///         would double-count the latency and hand back reach.
    ///     </para>
    /// </summary>
    private void InteractWithEntity(int entityId, byte action, long renderTimeMs)
    {
        var playerWorld = server.getWorld(player.DimensionId);
        var targetEntity = playerWorld.getEntity(entityId);

        if (targetEntity is null || !player.CanSee(targetEntity))
        {
            return;
        }

        var reach = player.GameMode.EntityReach + 1f;

        var rewindMs = EntityPositionHistory.ClampRewind(renderTimeMs, server.SimulationTimeMs);
        var history = server.getEntityTracker(player.DimensionId).HistoryFor(entityId);

        // Falls through to the present when the entity has no history — it was spawned this tick, or
        // nothing tracks it — which is the same answer as no rewind and needs no separate branch.
        var squaredDistance =
            history is not null && history.Sample(rewindMs, out var pastX, out var pastY, out var pastZ)
                ? player.GetSquaredDistance(pastX, pastY, pastZ)
                : player.GetSquaredDistance(targetEntity);

        if (squaredDistance >= reach * reach)
        {
            return;
        }

        if (action == 0)
        {
            player.Interact(targetEntity);
        }
        else if (action == 1)
        {
            player.Attack(targetEntity);
        }
    }

    private void onPlayerRespawn(PlayerRespawnMessage packet)
    {
        if (player.Health <= 0)
        {
            player = server.playerManager.respawnPlayer(player, packet.DimensionId);
        }
    }

    private void onClickSlot(ClickSlotMessage packet)
    {
        if (player.CurrentScreenHandler.SyncId == packet.SyncId && player.CurrentScreenHandler.canOpen(player))
        {
            var clickedStack = player.CurrentScreenHandler.onSlotClick(packet.Slot, packet.Button, packet.HoldingShift, player);
            if (ItemStack.AreEqual(packet.Stack, clickedStack))
            {
                player.NetworkHandler.SendMessage(Acknowledge(packet.SyncId, packet.ActionType, true));
                player.SkipPacketSlotUpdates = true;
                player.CurrentScreenHandler.SendContentUpdates();
                player.updateCursorStack();
                player.SkipPacketSlotUpdates = false;
            }
            else
            {
                // should something be done adding fails?
                transactions.TryAdd(player.CurrentScreenHandler.SyncId, packet.ActionType);
                player.NetworkHandler.SendMessage(Acknowledge(packet.SyncId, packet.ActionType, false));
                player.CurrentScreenHandler.updatePlayerList(player, false);

                var size = player.CurrentScreenHandler.Slots.Count;
                var slotStacks = new List<ItemStack>(size);

                for (var i = 0; i < size; i++)
                {
                    slotStacks.Add(player.CurrentScreenHandler.Slots[i].getStack());
                }

                player.onContentsUpdate(player.CurrentScreenHandler, slotStacks);
            }
        }
    }

    private static ScreenHandlerAckMessage Acknowledge(sbyte syncId, short actionType, bool accepted) => new()
    {
        SyncId = syncId,
        ActionType = actionType,
        Accepted = accepted
    };

    private void onScreenHandlerAck(ScreenHandlerAckMessage packet)
    {
        if (transactions.TryGetValue(player.CurrentScreenHandler.SyncId, out var value)
            && packet.ActionType == value
            && player.CurrentScreenHandler.SyncId == packet.SyncId
            && !player.CurrentScreenHandler.canOpen(player))
        {
            player.CurrentScreenHandler.updatePlayerList(player, true);
        }
    }

    private void onUpdateSign(UpdateSignMessage packet)
    {
        var playerWorld = server.getWorld(player.DimensionId);
        if (playerWorld.Reader.IsPosLoaded(packet.X, packet.Y, packet.Z))
        {
            BlockEntity blockEntity = playerWorld.Entities.GetBlockEntity<BlockEntitySign>(packet.X, packet.Y, packet.Z);
            var sign = blockEntity as BlockEntitySign;
            if (sign != null)
            {
                if (!sign.IsEditable())
                {
                    server.Warn("Player " + player.Name + " just tried to change non-editable sign");
                    return;
                }
            }

            // The length bound is already enforced by the reader, before the string is allocated.
            // What is left here is the character filter, which is a content rule rather than a
            // framing one and has to stay on the server: a client that skips it is the case this
            // exists for.
            var lines = packet.Lines;
            for (var lineIndex = 0; lineIndex < 4; lineIndex++)
            {
                if (!lines[lineIndex].All(ChatAllowedCharacters.IsAllowedCharacter))
                {
                    lines[lineIndex] = "!?";
                }
            }

            packet.Lines = lines;

            if (sign != null)
            {
                var x = packet.X;
                int y = packet.Y;
                var z = packet.Z;

                for (var textLineIndex = 0; textLineIndex < 4; textLineIndex++)
                {
                    sign.Texts[textLineIndex] = packet.Lines[textLineIndex];
                }

                sign.SetEditable(false);
                sign.MarkDirty();
                playerWorld.Broadcaster.BlockUpdateEvent(x, y, z);
            }
        }
    }

    public override bool isServerSide() => true;
}
