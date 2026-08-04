using BetaSharp.Blocks.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Inventories;
using BetaSharp.Items;
using BetaSharp.NBT;
using BetaSharp.Network.Chunks;
using BetaSharp.Network.Messages;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Snapshots;
using BetaSharp.Registries;
using BetaSharp.Screens;
using BetaSharp.Screens.Slots;
using BetaSharp.Server;
using BetaSharp.Server.Entities;
using BetaSharp.Server.Network;
using BetaSharp.Stats;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds;
using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core;
using BetaSharp.Worlds.Core.Systems;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Entities;

public class ServerPlayerEntity : EntityPlayer, ScreenHandlerListener
{
    private static readonly ILogger s_logger = Log.Instance.For<ServerPlayerEntity>();
    private readonly ItemStack?[] _equipment = [null, null, null, null, null];
    private readonly PlayerChunkSendQueue _pendingChunkUpdates = new();
    private readonly BetaSharpServer _server;
    public readonly HashSet<ChunkPos> ActiveChunks = [];
    public readonly Queue<ChunkPos> PendingChunkUpdates = new();
    private double _chunkStreamingMotionX;
    private double _chunkStreamingMotionZ;
    private int _joinInvulnerabilityTicks = 60;
    private int _lastHealthScore = -99999999;
    private int _screenHandlerSyncId;
    public ServerPlayerInteractionManager InteractionManager;
    public double LastX;
    public double LastZ;
    public bool SkipPacketSlotUpdates;

    public ServerPlayerEntity(BetaSharpServer server, IWorldContext world, string name, ServerPlayerInteractionManager interactionManager) : base(world)
    {
        GameModeHolder = server.DefaultGameMode;
        interactionManager.player = this;
        InteractionManager = interactionManager;
        Vec3I spawnPos = world.Properties.GetSpawnPos();
        int x = spawnPos.X;
        int y = spawnPos.Z;
        int z = spawnPos.Y;
        if (!world.Dimension.HasCeiling)
        {
            if (world.Properties.TerrainType == WorldType.Sky)
            {
                int validityY = world.Reader.GetSpawnPositionValidityY(x, y);
                if (validityY > 0)
                {
                    z = validityY;
                }
            }
            else
            {
                x += Random.NextInt(20) - 10;
                z = world.Reader.GetSpawnPositionValidityY(x, y);
                y += Random.NextInt(20) - 10;
            }
        }

        SetPositionAndAnglesKeepPrevAngles(x + 0.5, z, y + 0.5, 0.0F, 0.0F);
        _server = server;
        StepHeight = 0.0F;
        Name = name;
        StandingEyeHeight = 0.0F;
    }

    public Dictionary<ChunkPos, long> ChunksTerrainSentToClient { get; } = [];

    /// <summary>
    ///     Chunk hashes this client says it already holds, from <c>ChunkCacheOfferMessage</c>. Empty
    ///     for a vanilla client, a first-time visitor, or one whose cache is unavailable.
    /// </summary>
    public Dictionary<ChunkPos, ulong> OfferedChunkHashes { get; } = [];

    /// <summary>
    ///     What this client has confirmed knowing about every entity it can see, and the source of
    ///     the deltas sent to it. Per player because a delta is only meaningful against a state that
    ///     particular peer holds — see <see cref="PlayerSnapshotStream" />.
    /// </summary>
    public PlayerSnapshotStream SnapshotStream { get; } = new();

    public ServerPlayNetworkHandler? NetworkHandler { get; set; }

    public override ItemStack?[] Equipment => _equipment;


    public override float EyeHeight => 1.62F;


    public void onSlotUpdate(ScreenHandler handler, int slot, ItemStack? stack)
    {
        if (handler.GetSlot(slot) is CraftingResultSlot)
        {
            return;
        }

        if (!SkipPacketSlotUpdates)
        {
            NetworkHandler?.SendMessage(SlotUpdate(handler.SyncId, slot, stack));
        }
    }


    public void onContentsUpdate(ScreenHandler handler, List<ItemStack> stacks)
    {
        NetworkHandler?.SendMessage(new InventoryMessage
        {
            SyncId = (sbyte)handler.SyncId,
            Contents = [.. stacks.Select(s => s?.Copy())],
        });
        NetworkHandler?.SendMessage(SlotUpdate(-1, -1, Inventory.GetCursorStack()));
    }

    public void onPropertyUpdate(ScreenHandler handler, int syncId, int trackedValue) =>
        NetworkHandler?.SendMessage(new ScreenHandlerPropertyMessage
        {
            SyncId = (sbyte)handler.SyncId,
            PropertyId = (short)syncId,
            Value = (short)trackedValue,
        });

    /// <summary>The stack is copied because the screen keeps mutating the one it handed over.</summary>
    private static ScreenHandlerSlotMessage SlotUpdate(int syncId, int slot, ItemStack? stack) => new()
    {
        SyncId = (sbyte)syncId,
        Slot = (short)slot,
        Stack = stack?.Copy(),
    };


    public override void SetWorld(IWorldContext world)
    {
        base.SetWorld(world);
        InteractionManager = new ServerPlayerInteractionManager((ServerWorld)world)
        {
            player = this
        };
    }

    public void initScreenHandler() => CurrentScreenHandler?.AddListener(this);

    protected override void resetEyeHeight() => StandingEyeHeight = 0.0F;

    public override void Tick()
    {
        TickSleep();

        InteractionManager.update();
        _joinInvulnerabilityTicks--;
        CurrentScreenHandler?.SendContentUpdates();

        for (int i = 0; i < 5; i++)
        {
            ItemStack? itemStack = getEquipment(i);
            if (itemStack == _equipment[i])
            {
                continue;
            }

            _server.getEntityTracker(DimensionId).sendToListeners(this, EntityTrackerEntry.Equipment(ID, i, itemStack));
            _equipment[i] = itemStack;
        }
    }

    private ItemStack? getEquipment(int slot) => slot == 0 ? Inventory.ItemInHand : Inventory.Armor[slot - 1];

    public override bool Damage(Entity? damageSource, int amount)
    {
        if (_joinInvulnerabilityTicks > 0)
        {
            return false;
        }

        if (_server.pvpEnabled)
        {
            return base.Damage(damageSource, amount);
        }

        if (damageSource is EntityPlayer || ArrowBehavior.OwnerOf(damageSource) is EntityPlayer)
        {
            return false;
        }

        return base.Damage(damageSource, amount);
    }

    protected override bool isPvpEnabled() => _server.pvpEnabled;

    public void PlayerTick(bool shouldSendChunkUpdates)
    {
        GenericTick();
        PlayerTickPostGeneric(shouldSendChunkUpdates);
    }

    public void IdleTick()
    {
        base.BaseTick();
        AfterLivingTickCosmetics();
        PickupAndInventorySubtick();
        CollideWithPickupEntities();
        PlayerTickPostGeneric(false);
    }

    private void PlayerTickPostGeneric(bool shouldSendChunkUpdates)
    {
        for (int slotIndex = 0; slotIndex < Inventory.Size; slotIndex++)
        {
            ItemStack? itemStack = Inventory.GetStack(slotIndex);
            if (NetworkHandler != null && (itemStack == null || !Item.Items[itemStack.ItemId]!.IsNetworkSynced() || NetworkHandler?.getBlockDataSendQueueSize() > 2))
            {
                continue;
            }

            if (itemStack == null)
            {
                continue;
            }

            Message? packet = Item.Items[itemStack.ItemId]!.GetUpdatePacket(itemStack, World, this);
            if (packet != null)
            {
                NetworkHandler?.SendMessage(packet);
            }
        }

        if (shouldSendChunkUpdates)
        {
            FlushPendingChunkUpdates();
        }

        if (InTeleportationState)
        {
            if (_server.config.GetAllowNether(true))
            {
                if (CurrentScreenHandler != PlayerScreenHandler)
                {
                    CloseHandledScreen();
                }

                if (Vehicle != null)
                {
                    SetVehicle(Vehicle);
                }
                else
                {
                    ChangeDimensionCooldown += 0.0125F;
                    if (ChangeDimensionCooldown == 0.0125F)
                    {
                        s_logger.LogInformation("[DIM] {Name} entered a portal in dim {Dim}", Name, DimensionId);
                    }

                    if (ChangeDimensionCooldown >= 1.0F)
                    {
                        ChangeDimensionCooldown = 1.0F;
                        PortalCooldown = 10;
                        _server.playerManager.changePlayerDimension(this);
                    }
                }

                InTeleportationState = false;
            }
        }
        else
        {
            if (ChangeDimensionCooldown > 0.0F)
            {
                ChangeDimensionCooldown -= 0.05F;
            }

            if (ChangeDimensionCooldown < 0.0F)
            {
                ChangeDimensionCooldown = 0.0F;
            }
        }

        if (PortalCooldown > 0)
        {
            PortalCooldown--;
        }

        if (Health == _lastHealthScore)
        {
            return;
        }

        NetworkHandler?.SendMessage(new HealthUpdateMessage { HealthMp = (short)Health });
        _lastHealthScore = Health;
    }

    protected override void ReadNbt(NBTTagCompound nbt)
    {
        base.ReadNbt(nbt);
        if (nbt.HasKey("Gamemode"))
        {
            if (_server.RegistryAccess.GetOrThrow(RegistryKeys.GameModes).AsAssetLoader().TryGetHolder(nbt.GetString("Gamemode"), out Holder<GameMode>? holder))
            {
                GameModeHolder = holder;
            }
        }
    }

    protected override void WriteNbt(NBTTagCompound nbt)
    {
        base.WriteNbt(nbt);

        // If default gamemode, clear stored gamemode in case the default gamemode is changed.
        if (GameModeHolder.Value != _server.DefaultGameMode.Value)
        {
            nbt.SetString("Gamemode", GameModeHolder.Value.ToString());
        }
        else
        {
            nbt.RemoveTag("Gamemode");
        }
    }

    /// <summary>
    ///     Paces chunk streaming against the transport's own queue. See <see cref="ChunkSendPacer" />
    ///     for why the queue rather than a bandwidth estimate.
    /// </summary>
    private readonly ChunkSendPacer _chunkPacer = new();

    public void ResetChunkStreamingState()
    {
        _chunkStreamingMotionX = 0.0;
        _chunkStreamingMotionZ = 0.0;
        _pendingChunkUpdates.Clear();
    }

    public void UpdateChunkStreamingMotion(double motionX, double motionZ)
    {
        _chunkStreamingMotionX = motionX;
        _chunkStreamingMotionZ = motionZ;
        _pendingChunkUpdates.ReprioritizeAll(this);
    }

    public void ScheduleChunkSend(ChunkPos chunkPos) => _pendingChunkUpdates.EnqueueOrPromote(this, chunkPos);

    public void CancelChunkSend(ChunkPos chunkPos) => _pendingChunkUpdates.Remove(chunkPos);

    public void FlushPendingChunkUpdates()
    {
        if (_pendingChunkUpdates.Count == 0)
        {
            return;
        }

        ServerWorld world = _server.getWorld(DimensionId);
        _chunkPacer.BeginTick();

        int pending = NetworkHandler?.getWorldPacketBacklog() ?? 0;

        // CanSend is evaluated first, so a refusal leaves the queue untouched: the chunk stays at
        // its priority and is reconsidered next tick, by which time the player may have moved and
        // re-prioritising should decide afresh.
        while (_chunkPacer.CanSend(pending) && _pendingChunkUpdates.TryDequeue(out ChunkPos chunkPos))
        {
            if (!ActiveChunks.Contains(chunkPos))
            {
                continue;
            }

            _chunkPacer.Record(SendChunkData(world, chunkPos));
            ChunksTerrainSentToClient[chunkPos] = Environment.TickCount64;
            SendBlockEntityUpdates(world, chunkPos);
            _server.getEntityTracker(DimensionId).updateListenerForChunk(this, chunkPos.X, chunkPos.Z);

            // Re-read rather than assume: block updates and block entities for the chunk just sent
            // share this queue, so the depth after one chunk is not the depth before it plus one.
            pending = NetworkHandler?.getWorldPacketBacklog() ?? 0;
        }
    }

    internal ChunkPriority GetChunkPriority(ChunkPos chunkPos, long sequence)
    {
        int playerChunkX = (int)X >> 4;
        int playerChunkZ = (int)Z >> 4;
        int deltaX = chunkPos.X - playerChunkX;
        int deltaZ = chunkPos.Z - playerChunkZ;
        int ring = Math.Max(Math.Abs(deltaX), Math.Abs(deltaZ));

        double directionPenalty = 0.0;
        double motionLength = Math.Sqrt(_chunkStreamingMotionX * _chunkStreamingMotionX + _chunkStreamingMotionZ * _chunkStreamingMotionZ);
        if (motionLength > 0.0)
        {
            directionPenalty = -(deltaX * _chunkStreamingMotionX + deltaZ * _chunkStreamingMotionZ) / motionLength;
        }

        return new ChunkPriority(ring, directionPenalty, sequence);
    }

    /// <summary>Sends one chunk and returns the bytes it cost, for the pacer.</summary>
    private int SendChunkData(IWorldContext world, ChunkPos chunkPos)
    {
        ServerPlayNetworkHandler? handler = NetworkHandler;
        if (handler is null)
        {
            return 0;
        }

        // A peer that speaks the protocol gets the palette encoding; a vanilla client, and loopback,
        // get the format Beta 1.7.3 defines. The choice is made per send rather than per session
        // because the capability is only known once the client has declared it, which is after the
        // first chunks are already queued.
        if (handler.WantsCompactPayloads)
        {
            Chunk chunk = world.ChunkHost.GetChunk(chunkPos.X, chunkPos.Z);

            byte[] blob = ChunkBlobCodec.Encode(
                chunk.Blocks, chunk.Meta.Bytes, chunk.BlockLight.Bytes, chunk.SkyLight.Bytes);

            // The client's claim is checked against the chunk as it is right now, so a stale or
            // wrong-world offer simply loses and the chunk goes out in full.
            //
            // The encode above is paid either way, which is the one inefficiency here: discovering
            // that a chunk does not need sending costs 0.2 ms of encoding it. Avoiding that needs a
            // per-chunk cached hash invalidated on modification, and Chunk has no version counter to
            // hang that on — worth doing, but a change to the chunk rather than to the protocol.
            if (OfferedChunkHashes.TryGetValue(chunkPos, out ulong offered)
                && offered == ChunkHash.Of(blob))
            {
                ChunkUnchangedMessage unchanged = new() { ChunkX = chunkPos.X, ChunkZ = chunkPos.Z };
                handler.SendMessage(unchanged);
                return unchanged.Size();
            }

            ChunkDataMessage message = new()
            {
                ChunkX = chunkPos.X,
                ChunkZ = chunkPos.Z,
                Compressed = ChunkDataMessage.Compress(blob),
            };

            handler.SendMessage(message);
            return message.Size();
        }

        // Loopback, and nothing else now that every play packet is a message. The palette encoding
        // is skipped because its saving is measured in wire bytes and there is no wire; the box
        // encoding is kept because the chunk pacer's budget is denominated in the size the message
        // reports, and a message that reports a header is not paced at all.
        RegionDataMessage region = RegionDataMessage.Of(
            chunkPos.X * 16, 0, chunkPos.Z * 16, 16, ChuckFormat.WorldHeight, 16, world);
        handler.SendMessage(region);
        return region.Size();
    }

    private void SendBlockEntityUpdates(IWorldContext world, ChunkPos chunkPos)
    {
        int startX = chunkPos.X * 16;
        int startZ = chunkPos.Z * 16;
        int endX = startX + 16;
        int endZ = startZ + 16;

        List<BlockEntity> blockEntities = world.Entities.GetBlockEntities(startX, 0, startZ, endX, ChuckFormat.WorldHeight, endZ);
        foreach (BlockEntity blockEntity in blockEntities)
        {
            updateBlockEntity(blockEntity);
        }
    }

    private void updateBlockEntity(BlockEntity? blockEntity)
    {
        if (blockEntity?.CreateUpdateMessage() is { } message)
        {
            NetworkHandler?.SendMessage(message);
        }
    }

    public override void sendPickup(Entity item, int count)
    {
        if (!GameMode.CanPickup)
        {
            return;
        }

        if (!item.Dead)
        {
            EntityTracker et = _server.getEntityTracker(DimensionId);
            if (ArrowBehavior.IsArrow(item) || item.Behaviors.Find<DroppedItemBehavior>() is not null)
            {
                et.sendToListeners(item, new ItemPickupMessage { EntityId = item.ID, CollectorEntityId = ID });
            }
        }

        base.sendPickup(item, count);
        CurrentScreenHandler?.SendContentUpdates();
    }

    public override void SwingHand()
    {
        if (HandSwinging)
        {
            return;
        }

        HandSwingTicks = -1;
        HandSwinging = true;
        EntityTracker et = _server.getEntityTracker(DimensionId);
        et.sendToListeners(this, Animate(EntityAnimationMessage.EntityAnimation.SwingHand));
    }

    private EntityAnimationMessage Animate(EntityAnimationMessage.EntityAnimation animation) =>
        new() { EntityId = ID, AnimationId = (byte)animation };

    public override SleepAttemptResult TrySleep(int x, int y, int z)
    {
        SleepAttemptResult sleepAttemptResult = base.TrySleep(x, y, z);
        if (sleepAttemptResult != SleepAttemptResult.OK)
        {
            return sleepAttemptResult;
        }

        EntityTracker et = _server.getEntityTracker(DimensionId);
        var sleepMessage = new PlayerSleepUpdateMessage
        {
            PlayerId = ID,
            Status = 0,
            X = x,
            Y = (sbyte)y,
            Z = z
        };
        et.sendToAround(this, sleepMessage);
        NetworkHandler?.teleport(x, y, z, Yaw, Pitch);

        return sleepAttemptResult;
    }

    public override void WakeUp(bool resetSleepTimer, bool updateSleepingPlayers, bool setSpawnPos)
    {
        if (IsSleeping)
        {
            EntityTracker et = _server.getEntityTracker(DimensionId);
            et.sendToAround(this, Animate(EntityAnimationMessage.EntityAnimation.WakeUp));
        }

        base.WakeUp(resetSleepTimer, updateSleepingPlayers, setSpawnPos);
        NetworkHandler?.teleport(X, Y, Z, Yaw, Pitch);
    }


    public override void SetVehicle(Entity? entity)
    {
        base.SetVehicle(entity);
        NetworkHandler?.SendMessage(new EntityVehicleMessage { EntityId = ID, VehicleEntityId = Vehicle?.ID ?? -1 });
        NetworkHandler?.teleport(X, Y, Z, Yaw, Pitch);
    }


    protected override void Fall(double heightDifference, bool onGround)
    {
    }

    public void handleFall(double heightDifference, bool onGround) => base.Fall(heightDifference, onGround);

    private void incrementScreenHandlerSyncId() => _screenHandlerSyncId = (_screenHandlerSyncId % 100) + 1;

    private OpenScreenMessage OpenScreen(int screenHandlerId, string name, int slots) => new()
    {
        SyncId = (sbyte)_screenHandlerSyncId,
        ScreenHandlerId = (sbyte)screenHandlerId,
        Name = name,
        SlotsCount = (sbyte)slots,
    };


    public override void openCraftingScreen(int x, int y, int z)
    {
        incrementScreenHandlerSyncId();
        NetworkHandler?.SendMessage(OpenScreen(1, "Crafting", 9));
        CurrentScreenHandler = new CraftingScreenHandler(Inventory, World, x, y, z);
        CurrentScreenHandler.SyncId = _screenHandlerSyncId;
        CurrentScreenHandler.AddListener(this);
    }


    public override void openChestScreen(IInventory inventory)
    {
        incrementScreenHandlerSyncId();
        NetworkHandler?.SendMessage(OpenScreen(0, inventory.Name, inventory.Size));
        CurrentScreenHandler = new GenericContainerScreenHandler(Inventory, inventory);
        CurrentScreenHandler.SyncId = _screenHandlerSyncId;
        CurrentScreenHandler.AddListener(this);
    }


    public override void openFurnaceScreen(BlockEntityFurnace furnace)
    {
        incrementScreenHandlerSyncId();
        NetworkHandler?.SendMessage(OpenScreen(2, furnace.Name, furnace.Size));
        CurrentScreenHandler = new FurnaceScreenHandler(Inventory, furnace);
        CurrentScreenHandler.SyncId = _screenHandlerSyncId;
        CurrentScreenHandler.AddListener(this);
    }


    public override void openDispenserScreen(BlockEntityDispenser dispenser)
    {
        incrementScreenHandlerSyncId();
        NetworkHandler?.SendMessage(OpenScreen(3, dispenser.Name, dispenser.Size));
        CurrentScreenHandler = new DispenserScreenHandler(Inventory, dispenser);
        CurrentScreenHandler.SyncId = _screenHandlerSyncId;
        CurrentScreenHandler.AddListener(this);
    }

    public void onContentsUpdate(ScreenHandler screenHandler) => onContentsUpdate(screenHandler, screenHandler.GetStacks());

    public override void OnCursorStackChanged(ItemStack? stack)
    {
    }

    public override void CloseHandledScreen()
    {
        NetworkHandler?.SendMessage(new CloseScreenMessage { SyncId = (sbyte)CurrentScreenHandler.SyncId });
        onHandledScreenClosed();
    }

    public void updateCursorStack()
    {
        if (!SkipPacketSlotUpdates)
        {
            NetworkHandler?.SendMessage(SlotUpdate(-1, -1, Inventory.GetCursorStack()));
        }
    }

    public void onHandledScreenClosed()
    {
        CurrentScreenHandler.onClosed(this);
        CurrentScreenHandler = PlayerScreenHandler;
    }

    public void updateInput(float sidewaysSpeed, float forwardSpeed, bool jumping, bool sneaking, float pitch, float yaw)
    {
        SidewaysSpeed = sidewaysSpeed;
        ForwardSpeed = forwardSpeed;
        Jumping = jumping;
        SetSneaking(sneaking);
        Pitch = pitch;
        Yaw = yaw;
    }

    public void updateInput(PlayerInputMessage packet)
    {
        if (GameMode is { CanWalk: false, DisallowFlying: true })
        {
            SidewaysSpeed = packet.Sideways;
            ForwardSpeed = packet.Forward;
        }
        else
        {
            SidewaysSpeed = 0;
            ForwardSpeed = 0;
        }

        Jumping = packet.Jumping;
        SetSneaking(packet.Sneaking);
        Pitch = packet.Pitch;
        Yaw = packet.Yaw;
    }


    public override void IncreaseStat(StatBase stat, int amount)
    {
        if (stat is not { LocalOnly: false })
        {
            return;
        }

        if (stat.IsAchievement())
        {
            s_logger.LogInformation("Player {PlayerName} unlocked {AchievementName}", Name, stat.StatName);
        }

        while (amount > 100)
        {
            NetworkHandler?.SendMessage(new IncreaseStatMessage { StatId = stat.Id, Amount = 100 });
            amount -= 100;
        }

        NetworkHandler?.SendMessage(new IncreaseStatMessage { StatId = stat.Id, Amount = (sbyte)amount });
    }

    public void onDisconnect()
    {
        if (Vehicle != null)
        {
            SetVehicle(Vehicle);
        }

        Passenger?.SetVehicle(this);

        if (Sleeping)
        {
            WakeUp(true, false, false);
        }
    }

    public void markHealthDirty() => _lastHealthScore = -99999999;

    public override void SendMessage(string message)
    {
        string translatedMessage = Translations.Get(message);
        NetworkHandler?.SendMessage(new ChatMessage { Text = translatedMessage });
    }

    //client only
    public override void Spawn() => throw new NotImplementedException();
}
