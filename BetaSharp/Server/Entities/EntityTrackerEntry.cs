using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Entities.Behaviors;
using BetaSharp.Items;
using BetaSharp.Network.Packets;
using BetaSharp.Network.Packets.S2CPlay;
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

    public void notifyNewLocation(IEnumerable<ServerPlayerEntity> players)
    {
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
                sendToListeners(EntityVelocityUpdateS2CPacket.Get(currentTrackedEntity.ID, velocityX, velocityY, velocityZ));
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
            bool hasMoved = Math.Abs(deltaX) >= 1 || Math.Abs(deltaY) >= 1 || Math.Abs(deltaZ) >= 1;
            bool hasRotated = Math.Abs(rotYaw - lastYaw) >= 8 || Math.Abs(rotPitch - lastPitch) >= 8;
            object? positionPacket = null;
            if (deltaX < -128 || deltaX >= 128 || deltaY < -128 || deltaY >= 128 || deltaZ < -128 || deltaZ >= 128 || ticksSinceLastDismount > 400)
            {
                ticksSinceLastDismount = 0;
                currentTrackedEntity.X = posX / 32.0;
                currentTrackedEntity.Y = posY / 32.0;
                currentTrackedEntity.Z = posZ / 32.0;
                positionPacket = EntityPositionS2CPacket.Get(currentTrackedEntity.ID, posX, posY, posZ, (byte)rotYaw, (byte)rotPitch);
            }
            else if (hasMoved || hasRotated)
            {
                // Some entities want their angle on every step rather than only when they visibly
                // turn — an arrow's flight is all arc and bounce, so it declares as much.
                if (currentTrackedEntity.Type?.Definition is { AlwaysSyncsRotation: true })
                {
                    positionPacket = EntityRotateAndMoveRelativeS2CPacket.Get(currentTrackedEntity.ID, (byte)deltaX, (byte)deltaY, (byte)deltaZ, (byte)rotYaw, (byte)rotPitch);
                }
                else if (hasMoved && hasRotated)
                {
                    positionPacket = EntityRotateAndMoveRelativeS2CPacket.Get(currentTrackedEntity.ID, (byte)deltaX, (byte)deltaY, (byte)deltaZ, (byte)rotYaw, (byte)rotPitch);
                }
                else if (hasMoved)
                {
                    positionPacket = EntityMoveRelativeS2CPacket.Get(currentTrackedEntity.ID, (byte)deltaX, (byte)deltaY, (byte)deltaZ);
                }
                else
                {
                    positionPacket = EntityRotateS2CPacket.Get(currentTrackedEntity.ID, (byte)rotYaw, (byte)rotPitch);
                }
            }

            if (positionPacket != null)
            {
                sendToListeners((Packet)positionPacket);
            }

            DataSynchronizer dataSync = currentTrackedEntity.DataSynchronizer;
            if (dataSync.Dirty)
            {
                var stream = new MemoryStream();
                dataSync.WriteChanges(stream);
                sendToAround(EntityTrackerUpdateS2CPacket.Get(currentTrackedEntity.ID, stream.ToArray()));
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
            sendToAround(EntityVelocityUpdateS2CPacket.Get(currentTrackedEntity));
            currentTrackedEntity.VelocityModified = false;
        }
    }

    public void sendToListeners(Packet packet)
    {
        foreach (var player in listeners)
        {
            player.NetworkHandler.SendPacket(packet);
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

    public void notifyEntityRemoved()
    {
        sendToListeners(EntityDestroyS2CPacket.Get(currentTrackedEntity.ID));
    }

    public void notifyEntityRemoved(ServerPlayerEntity player)
    {
        listeners.Remove(player);
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
                        player.NetworkHandler
                            .SendPacket(
                                EntityVelocityUpdateS2CPacket.Get(
                                    currentTrackedEntity.ID,
                                    currentTrackedEntity.VelocityX,
                                    currentTrackedEntity.VelocityY,
                                    currentTrackedEntity.VelocityZ
                                )
                            );
                    }

                    ItemStack[] equipment = currentTrackedEntity.Equipment;
                    if (equipment != null)
                    {
                        for (int slot = 0; slot < equipment.Length; slot++)
                        {
                            player.NetworkHandler.SendPacket(EntityEquipmentUpdateS2CPacket.Get(currentTrackedEntity.ID, slot, equipment[slot]));
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
                player.NetworkHandler.SendPacket(EntityDestroyS2CPacket.Get(currentTrackedEntity.ID));
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
            if (currentTrackedEntity is EntityMinecart minecartEntity)
            {
                if (minecartEntity.type == 0)
                {
                    return EntitySpawnS2CPacket.Get(currentTrackedEntity, 10);
                }

                if (minecartEntity.type == 1)
                {
                    return EntitySpawnS2CPacket.Get(currentTrackedEntity, 11);
                }

                if (minecartEntity.type == 2)
                {
                    return EntitySpawnS2CPacket.Get(currentTrackedEntity, 12);
                }
            }

            if (currentTrackedEntity is EntityBoat)
            {
                return EntitySpawnS2CPacket.Get(currentTrackedEntity, 1);
            }
            else if (currentTrackedEntity is EntityLiving living and not EntityPlayer)
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
            player.NetworkHandler.SendPacket(EntityDestroyS2CPacket.Get(currentTrackedEntity.ID));
        }
    }
}
