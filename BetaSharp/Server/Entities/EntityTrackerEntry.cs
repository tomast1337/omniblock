using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.Network.Messages;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Packets.S2CPlay;
using BetaSharp.Network.Snapshots;
using BetaSharp.Util;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds.Core;

namespace BetaSharp.Server.Entities;

internal class EntityTrackerEntry
{
    public Entity currentTrackedEntity;
    public int trackedDistance;
    public int trackingFrequency;
    public int lastX;
    public int lastY;
    public int lastZ;
    public int lastYaw;
    public int lastPitch;
    public double velocityX;
    public double velocityY;
    public double velocityZ;
    public int ticks;
    private double x;
    private double y;
    private double z;
    private bool isInitialized;
    private bool alwaysUpdateVelocity;
    private int ticksSinceLastDismount;
    private int _ticksSinceLastAbsoluteSync = 0;
    public bool newPlayerDataUpdated;
    public HashSet<ServerPlayerEntity> listeners = [];

    /// <summary>
    ///     Where this entity has been for the last couple of seconds, for lag-compensated hit
    ///     registration. Lives on the tracker entry because its lifetime is exactly the entry's — an
    ///     entity nobody tracks is one nobody can have aimed at.
    /// </summary>
    public EntityPositionHistory History { get; } = new();

    /// <summary>
    ///     Whether this tick was one of this entity's tracking ticks, so <see cref="SnapshotState" />
    ///     describes it. The snapshot path is offered a state on exactly the ticks the legacy path
    ///     considers sending a packet, so the two carry the same fidelity and
    ///     <c>trackingFrequency</c> keeps meaning what it meant.
    /// </summary>
    public bool OfferedThisTick { get; private set; }

    /// <summary>
    ///     This entity's position and facing as of the last tracking tick, in wire units. Only
    ///     meaningful while <see cref="OfferedThisTick" /> is set.
    /// </summary>
    public EntitySnapshotState SnapshotState { get; private set; }

    public EntityTrackerEntry(Entity entity, int trackedDistance, int trackedFrequency, bool alwaysUpdateVelocity)
    {
        currentTrackedEntity = entity;
        this.trackedDistance = trackedDistance;
        trackingFrequency = trackedFrequency;
        this.alwaysUpdateVelocity = alwaysUpdateVelocity;
        lastX = MathHelper.Floor(entity.X * 32.0);
        lastY = MathHelper.Floor(entity.Y * 32.0);
        lastZ = MathHelper.Floor(entity.Z * 32.0);
        lastYaw = MathHelper.Floor(entity.Yaw * 256.0F / 360.0F);
        lastPitch = MathHelper.Floor(entity.Pitch * 256.0F / 360.0F);
    }

    public override bool Equals(object? obj)
    {
        return obj is EntityTrackerEntry entry && entry.currentTrackedEntity.ID == currentTrackedEntity.ID;
    }

    public override int GetHashCode()
    {
        return currentTrackedEntity.ID;
    }

    public void notifyNewLocation(IEnumerable<ServerPlayerEntity> players, long simulationTimeMs)
    {
        // Before any of the send decisions below, and unconditionally. What gets broadcast is a
        // question of bandwidth; where the entity actually was is a question of fact, and hit
        // registration needs the latter at full tick resolution even for an entity the tracker has
        // decided is not worth a packet this tick.
        History.Record(simulationTimeMs, currentTrackedEntity.X, currentTrackedEntity.Y, currentTrackedEntity.Z);

        OfferedThisTick = false;
        newPlayerDataUpdated = false;
        if (!isInitialized || currentTrackedEntity.GetSquaredDistance(x, y, z) > 16.0)
        {
            x = currentTrackedEntity.X;
            y = currentTrackedEntity.Y;
            z = currentTrackedEntity.Z;
            isInitialized = true;
            newPlayerDataUpdated = true;
            updateListeners(players);
        }

        ticksSinceLastDismount++;

        // Update velocity before checking for changes and sending packet updates
        // this make it so velocity based updates happen within the same tick
        // tracking window instead of waiting for the next and possibly drifting client side.
        if (alwaysUpdateVelocity)
        {
            double velDeltaX = currentTrackedEntity.VelocityX - velocityX;
            double velDeltaY = currentTrackedEntity.VelocityY - velocityY;
            double velDeltaZ = currentTrackedEntity.VelocityZ - velocityZ;
            double velocityTolerance = 0.02;
            double velDeltaSqr = velDeltaX * velDeltaX + velDeltaY * velDeltaY + velDeltaZ * velDeltaZ;
            if (velDeltaSqr > velocityTolerance * velocityTolerance
                || velDeltaSqr > 0.0
                && currentTrackedEntity.VelocityX == 0.0
                && currentTrackedEntity.VelocityY == 0.0
                && currentTrackedEntity.VelocityZ == 0.0)
            {
                velocityX = currentTrackedEntity.VelocityX;
                velocityY = currentTrackedEntity.VelocityY;
                velocityZ = currentTrackedEntity.VelocityZ;
                sendToListeners(Velocity(currentTrackedEntity.ID, velocityX, velocityY, velocityZ));
            }
        }

        if (++ticks % trackingFrequency == 0)
        {
            int posX = MathHelper.Floor(currentTrackedEntity.X * 32.0);
            int posY = MathHelper.Floor(currentTrackedEntity.Y * 32.0);
            int posZ = MathHelper.Floor(currentTrackedEntity.Z * 32.0);
            int rotYaw = MathHelper.Floor(currentTrackedEntity.Yaw * 256.0F / 360.0F);
            int rotPitch = MathHelper.Floor(currentTrackedEntity.Pitch * 256.0F / 360.0F);
            int deltaX = posX - lastX;
            int deltaY = posY - lastY;
            int deltaZ = posZ - lastZ;

            // Offered to the snapshot encoder unconditionally, before the movement thresholds below.
            // Those thresholds exist because the packets they gate cost 8 to 10 bytes to say that
            // something moved slightly; the encoder's field mask says the same thing in one byte per
            // coordinate and nothing at all when a coordinate did not change, so it does not need
            // protecting from small movements — and the rotation threshold in particular was
            // discarding every turn under eleven degrees.
            OfferedThisTick = true;
            SnapshotState = new EntitySnapshotState(posX, posY, posZ, (byte)rotYaw, (byte)rotPitch);
            bool hasMoved = Math.Abs(deltaX) >= 1 || Math.Abs(deltaY) >= 1 || Math.Abs(deltaZ) >= 1;
            bool hasRotated = Math.Abs(rotYaw - lastYaw) >= 8 || Math.Abs(rotPitch - lastPitch) >= 8;
            Message? positionMessage = null;
            if (deltaX < -128 || deltaX >= 128 || deltaY < -128 || deltaY >= 128 || deltaZ < -128 || deltaZ >= 128 || ticksSinceLastDismount > 400)
            {
                ticksSinceLastDismount = 0;
                currentTrackedEntity.X = posX / 32.0;
                currentTrackedEntity.Y = posY / 32.0;
                currentTrackedEntity.Z = posZ / 32.0;
                positionMessage = new EntityTeleportMessage
                {
                    EntityId = currentTrackedEntity.ID,
                    X = posX,
                    Y = posY,
                    Z = posZ,
                    Yaw = (sbyte)rotYaw,
                    Pitch = (sbyte)rotPitch,
                };
            }
            else if (hasMoved || hasRotated)
            {
                // Some entities want their angle on every step rather than only when they visibly
                // turn — an arrow's flight is all arc and bounce, so it declares as much.
                bool sendsRotation =
                    hasRotated || currentTrackedEntity.Type?.Definition is { AlwaysSyncsRotation: true };

                positionMessage = new EntityMoveMessage
                {
                    EntityId = currentTrackedEntity.ID,
                    Mask = (hasMoved ? EntityMoveMessage.Field.Moved : EntityMoveMessage.Field.None)
                        | (sendsRotation ? EntityMoveMessage.Field.Rotated : EntityMoveMessage.Field.None),
                    DeltaX = (sbyte)deltaX,
                    DeltaY = (sbyte)deltaY,
                    DeltaZ = (sbyte)deltaZ,
                    Yaw = (sbyte)rotYaw,
                    Pitch = (sbyte)rotPitch,
                };
            }

            if (positionMessage is not null)
            {
                sendPositionToLegacyListeners(positionMessage);
            }

            DataSynchronizer dataSync = currentTrackedEntity.DataSynchronizer;
            if (dataSync.Dirty)
            {
                var stream = new MemoryStream();
                dataSync.WriteChanges(stream);
                sendToAround(new EntityDataMessage { EntityId = currentTrackedEntity.ID, Data = stream.ToArray() });
            }

            if (hasMoved)
            {
                lastX = posX;
                lastY = posY;
                lastZ = posZ;
            }

            if (hasRotated)
            {
                lastYaw = rotYaw;
                lastPitch = rotPitch;
            }
        }

        if (currentTrackedEntity.VelocityModified)
        {
            sendToAround(Velocity(
                currentTrackedEntity.ID,
                currentTrackedEntity.VelocityX,
                currentTrackedEntity.VelocityY,
                currentTrackedEntity.VelocityZ));
            currentTrackedEntity.VelocityModified = false;
        }
    }

    /// <summary>
    ///     Clamps to what a short can carry at 1/8000 of a block per tick, which is ±3.9.
    ///     <para>
    ///         The clamp lives with the sender rather than in the message. A message that truncated
    ///         instead would turn a fast knockback into a slow one in the opposite direction, and it
    ///         would do so silently — the receiver has no way to tell a wrapped value from a real one.
    ///     </para>
    /// </summary>
    private static EntityVelocityMessage Velocity(int entityId, double x, double y, double z)
    {
        const double limit = 3.9;

        return new EntityVelocityMessage
        {
            EntityId = entityId,
            MotionX = (short)(Math.Clamp(x, -limit, limit) * 8000.0),
            MotionY = (short)(Math.Clamp(y, -limit, limit) * 8000.0),
            MotionZ = (short)(Math.Clamp(z, -limit, limit) * 8000.0),
        };
    }

    /// <summary>An empty slot travels as item -1, which is what the renderer reads as "nothing".</summary>
    internal static EntityEquipmentMessage Equipment(int entityId, int slot, ItemStack? stack) => new()
    {
        EntityId = entityId,
        Slot = (short)slot,
        ItemRawId = (short)(stack?.ItemId ?? -1),
        ItemDamage = (short)(stack?.getDamage() ?? 0),
    };

    public void sendToListeners(Packet packet)
    {
        foreach (var player in listeners)
        {
            player.NetworkHandler.SendPacket(packet);
        }
    }

    public void sendToListeners(Message message)
    {
        foreach (var player in listeners)
        {
            player.NetworkHandler.SendMessage(message);
        }
    }

    /// <summary>
    ///     Sends a position packet to the listeners that have no other way to receive one.
    ///     <para>
    ///         A peer that speaks the protocol gets <see cref="EntitySnapshotMessage" /> instead, from
    ///         <c>EntityTracker</c>'s pass over the same tick. Sending both would not merely waste the
    ///         bytes: the two disagree about precision — the packets round rotation to an eleven
    ///         degree threshold and clamp position deltas to four blocks — so whichever arrived last
    ///         would win, and the snapshot's own delta chain would be measured against a position the
    ///         client no longer held.
    ///     </para>
    /// </summary>
    private void sendPositionToLegacyListeners(Message message)
    {
        foreach (var player in listeners)
        {
            if (player.NetworkHandler is { WantsCompactPayloads: true })
            {
                continue;
            }

            player.NetworkHandler.SendMessage(message);
        }
    }

    public void sendToAround(Packet packet)
    {
        foreach (var p in listeners)
        {
            p.NetworkHandler.SendPacket(packet);
        }
        if (currentTrackedEntity is ServerPlayerEntity entity)
        {
            entity.NetworkHandler.SendPacket(packet);
        }
    }

    public void sendToAround(Message message)
    {
        foreach (var p in listeners)
        {
            p.NetworkHandler.SendMessage(message);
        }
        if (currentTrackedEntity is ServerPlayerEntity entity)
        {
            entity.NetworkHandler.SendMessage(message);
        }
    }

    public void notifyEntityRemoved()
    {
        foreach (var player in listeners)
        {
            player.SnapshotStream.Forget(currentTrackedEntity.ID);
        }

        sendToListeners(new EntityDestroyMessage { EntityId = currentTrackedEntity.ID });
    }

    public void notifyEntityRemoved(ServerPlayerEntity player)
    {
        if (listeners.Remove(player))
        {
            player.SnapshotStream.Forget(currentTrackedEntity.ID);
        }
    }

    public void updateListener(ServerPlayerEntity player)
    {
        if (player != currentTrackedEntity)
        {
            double distX = player.X - lastX / 32.0;
            double distZ = player.Z - lastZ / 32.0;
            if (distX >= -trackedDistance && distX <= trackedDistance && distZ >= -trackedDistance && distZ <= trackedDistance)
            {
                if (!listeners.Contains(player))
                {
                    if (currentTrackedEntity.World is ServerWorld sw
                        && player.DimensionId == sw.Dimension.Id
                        && sw.ChunkMap != null)
                    {
                        int entityChunkX = MathHelper.Floor(currentTrackedEntity.X / 16.0);
                        int entityChunkZ = MathHelper.Floor(currentTrackedEntity.Z / 16.0);
                        if (!ChunkMap.HasPlayerReceivedChunkTerrain(player, entityChunkX, entityChunkZ))
                        {
                            return;
                        }
                    }

                    listeners.Add(player);
                    player.NetworkHandler.SendPacket(createAddEntityPacket());
                    if (alwaysUpdateVelocity)
                    {
                        player.NetworkHandler.SendMessage(Velocity(
                            currentTrackedEntity.ID,
                            currentTrackedEntity.VelocityX,
                            currentTrackedEntity.VelocityY,
                            currentTrackedEntity.VelocityZ));
                    }

                    ItemStack[] equipment = currentTrackedEntity.Equipment;
                    if (equipment != null)
                    {
                        for (int slot = 0; slot < equipment.Length; slot++)
                        {
                            player.NetworkHandler.SendMessage(Equipment(currentTrackedEntity.ID, slot, equipment[slot]));
                        }
                    }

                    if (currentTrackedEntity is EntityPlayer trackedPlayer)
                    {
                        if (trackedPlayer.IsSleeping)
                        {
                            player.NetworkHandler
                                .SendPacket(
                                    PlayerSleepUpdateS2CPacket.Get(
                                        currentTrackedEntity,
                                        0,
                                        MathHelper.Floor(currentTrackedEntity.X),
                                        MathHelper.Floor(currentTrackedEntity.Y),
                                        MathHelper.Floor(currentTrackedEntity.Z)
                                    )
                                );
                        }
                    }
                }
            }
            else if (listeners.Remove(player))
            {
                player.SnapshotStream.Forget(currentTrackedEntity.ID);
                player.NetworkHandler.SendMessage(new EntityDestroyMessage { EntityId = currentTrackedEntity.ID });
            }
        }
    }

    public void updateListeners(IEnumerable<ServerPlayerEntity> players)
    {
        foreach (var player in players)
        {
            updateListener(player);
        }
    }

    private Packet createAddEntityPacket()
    {
        if (currentTrackedEntity.Behaviors.Find<DroppedItemBehavior>() is not null)
        {
            var spawnPacket = ItemEntitySpawnS2CPacket.Get(currentTrackedEntity);
            currentTrackedEntity.X = spawnPacket.X / 32.0;
            currentTrackedEntity.Y = spawnPacket.Y / 32.0;
            currentTrackedEntity.Z = spawnPacket.Z / 32.0;
            return spawnPacket;
        }
        else if (currentTrackedEntity is ServerPlayerEntity p)
        {
            return PlayerSpawnS2CPacket.Get(p);
        }
        else
        {
            // Three cart kinds share one entity type, so which object-spawn id a minecart goes out
            // on comes from the behavior rather than the definition's single id.
            if (currentTrackedEntity.Behaviors.Find<MinecartBehavior>() is { } cart)
            {
                return EntitySpawnS2CPacket.Get(currentTrackedEntity, cart.SpawnObjectId(currentTrackedEntity));
            }

            if (currentTrackedEntity is EntityLiving living and not EntityPlayer)
            {
                return LivingEntitySpawnS2CPacket.Get(living);
            }
            // An arrow's spawn packet names whoever loosed it, so the client can credit the hit;
            // an unowned one (a dispenser's) names itself.
            else if (currentTrackedEntity.Behaviors.Find<ArrowBehavior>() is { } flight)
            {
                EntityLiving? shooter = flight.Owner(currentTrackedEntity);
                return EntitySpawnS2CPacket.Get(currentTrackedEntity, 60, shooter != null ? shooter.ID : currentTrackedEntity.ID);
            }
            // A fireball's spawn packet carries its shooter's id and rides its power vector in the
            // velocity fields, so it comes from the behavior rather than the generic declared branch.
            else if (currentTrackedEntity.Behaviors.Find<FireballBehavior>() is { } fireball)
            {
                var packet = EntitySpawnS2CPacket.Get(currentTrackedEntity, 63, fireball.Owner(currentTrackedEntity)!.ID);
                packet.VelocityX = (int)(fireball.PowerX(currentTrackedEntity) * 8000.0);
                packet.VelocityY = (int)(fireball.PowerY(currentTrackedEntity) * 8000.0);
                packet.VelocityZ = (int)(fireball.PowerZ(currentTrackedEntity) * 8000.0);

                return packet;
            }
            // A falling block's object-spawn id depends on which block it carries, so it comes from
            // the behavior rather than the definition's single SpawnObjectId.
            else if (currentTrackedEntity.Behaviors.Find<SettleAsBlockBehavior>() is { } settle)
            {
                return EntitySpawnS2CPacket.Get(currentTrackedEntity, settle.SpawnObjectId(currentTrackedEntity));
            }
            else if (currentTrackedEntity.Type?.Definition is { SpawnObjectId: > 0 } declared)
            {
                return EntitySpawnS2CPacket.Get(currentTrackedEntity, declared.SpawnObjectId);
            }
            // A painting spawns by name and anchor rather than by position, so it has its own packet.
            else if (currentTrackedEntity.Behaviors.Find<HangingArtBehavior>() is not null)
            {
                return PaintingEntitySpawnS2CPacket.Get(currentTrackedEntity);
            }
            else
            {
                throw new ArgumentException("Don't know how to add " + currentTrackedEntity.GetType() + "!");
            }
        }
    }

    public void removeListener(ServerPlayerEntity player)
    {
        if (listeners.Remove(player))
        {
            player.SnapshotStream.Forget(currentTrackedEntity.ID);
            player.NetworkHandler.SendMessage(new EntityDestroyMessage { EntityId = currentTrackedEntity.ID });
        }
    }
}
