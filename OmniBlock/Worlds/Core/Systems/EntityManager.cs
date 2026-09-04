using OmniBlock.Blocks.Entities;
using OmniBlock.Entities;
using OmniBlock.Profiling;
using OmniBlock.Rules;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Worlds.Core.Systems;

public class EntityManager
{
    [ThreadStatic] private static List<Entity>? _tempCollisionEntities;
    [ThreadStatic] private static List<Box>? _tempCollisionBoxes;
    [ThreadStatic] private static List<Entity>? _tempCollisionEntitiesResult;

    private readonly List<BlockEntity> _blockEntityUpdateQueue = [];
    private readonly Dictionary<int, Entity> _entitiesById = new();
    private readonly List<Entity> _entitiesToUnload = [];
    private readonly IWorldContext _world;
    private bool _processingDeferred;

    public List<BlockEntity> BlockEntities = [];
    public List<Entity> Entities = [];
    public List<Entity> GlobalEntities = [];
    public List<EntityPlayer> Players = [];

    public EntityManager(IWorldContext world) => _world = world;

    public bool AllPlayersSleeping { get; private set; }

    public event Action<Entity>? OnEntityAdded;
    public event Action<Entity>? OnEntityRemoved;
    public event Action<Entity>? OnGlobalEntityAdded;
    public event Func<Entity, bool>? OnEntityUpdating;
    public event Action<int, int, int>? OnBlockUpdateRequired;

    public bool SpawnGlobalEntity(Entity entity)
    {
        GlobalEntities.Add(entity);
        OnGlobalEntityAdded?.Invoke(entity);
        return true;
    }

    private static int ClampChunkSlice(int slice)
    {
        if (slice < 0) return 0;
        if (slice >= 8) return 7;
        return slice;
    }

    private static bool ShouldSkipStandaloneUpdate(Entity entity)
    {
        if (entity.Vehicle == null)
        {
            return false;
        }

        if (!entity.Dead && !entity.Vehicle.Dead && ReferenceEquals(entity.Vehicle.Passenger, entity))
        {
            return true;
        }

        entity.Vehicle.Passenger = null;
        entity.Vehicle = null;
        return false;
    }

    private void RemoveEntityFromChunkList(Entity entity, int chunkX, int chunkZ, int chunkSlice)
    {
        if (_world.ChunkHost.ChunkSource.IsChunkLoaded(chunkX, chunkZ))
        {
            _world.ChunkHost.GetChunk(chunkX, chunkZ).RemoveEntity(entity, ClampChunkSlice(chunkSlice));
        }
    }

    private void RemoveEntityFromAllKnownChunkLists(Entity entity)
    {
        RemoveEntityFromChunkList(entity, entity.ChunkX, entity.ChunkZ, entity.ChunkSlice);

        var currentChunkX = MathHelper.Floor(entity.X / 16.0D);
        var currentChunkY = MathHelper.Floor(entity.Y / 16.0D);
        var currentChunkZ = MathHelper.Floor(entity.Z / 16.0D);

        if (currentChunkX != entity.ChunkX || currentChunkY != entity.ChunkSlice || currentChunkZ != entity.ChunkZ)
        {
            RemoveEntityFromChunkList(entity, currentChunkX, currentChunkZ, currentChunkY);
        }
    }

    private void RemoveEntityNow(Entity entity, bool forceNotify = false)
    {
        if (entity.Passenger != null)
        {
            entity.Passenger.SetVehicle(null);
        }

        if (entity.Vehicle != null)
        {
            entity.SetVehicle(null);
        }

        RemoveEntityFromAllKnownChunkLists(entity);

        var wasTracked = false;

        if (Entities.Remove(entity))
        {
            wasTracked = true;
        }

        if (_entitiesToUnload.Remove(entity))
        {
            wasTracked = true;
        }

        if (_entitiesById.TryGetValue(entity.ID, out var current) && ReferenceEquals(current, entity))
        {
            _entitiesById.Remove(entity.ID);
            wasTracked = true;
        }

        if (entity is EntityPlayer player)
        {
            if (Players.Remove(player))
            {
                wasTracked = true;
            }
        }

        if (wasTracked || forceNotify)
        {
            NotifyEntityRemoved(entity);
        }
    }

    private void ProcessQueuedUnloads()
    {
        if (_entitiesToUnload.Count == 0)
        {
            return;
        }

        var pendingUnloads = _entitiesToUnload.ToArray();
        _entitiesToUnload.Clear();

        foreach (var entity in pendingUnloads)
        {
            RemoveEntityNow(entity, true);
        }
    }

    public void UpdateEntityLists()
    {
        ProcessQueuedUnloads();

        for (var i = Entities.Count - 1; i >= 0; --i)
        {
            var entity = Entities[i];

            if (ShouldSkipStandaloneUpdate(entity))
            {
                continue;
            }

            if (entity.Dead)
            {
                RemoveEntityNow(entity);
            }
        }
    }

    public bool SpawnEntity(Entity entity)
    {
        var chunkX = MathHelper.Floor(entity.X / 16.0D);
        var chunkZ = MathHelper.Floor(entity.Z / 16.0D);
        var isPlayer = entity is EntityPlayer;

        if (!isPlayer && !_world.ChunkHost.ChunkSource.IsChunkLoaded(chunkX, chunkZ))
        {
            return false;
        }

        if (entity is EntityPlayer player)
        {
            Players.Add(player);
        }

        _world.ChunkHost.GetChunk(chunkX, chunkZ).AddEntity(entity);
        Entities.Add(entity);
        _entitiesById[entity.ID] = entity;

        NotifyEntityAdded(entity);
        return true;
    }

    public void Remove(Entity entity)
    {
        if (entity.Passenger != null)
        {
            entity.Passenger.SetVehicle(null);
        }

        if (entity.Vehicle != null)
        {
            entity.SetVehicle(null);
        }

        entity.MarkDead();

        if (entity is EntityPlayer player)
        {
            Players.Remove(player);
        }
    }

    public void ServerRemove(Entity entity)
    {
        entity.MarkDead();
        RemoveEntityNow(entity);
    }

    public bool AreAllPlayersAsleep()
    {
        if (Players.Count == 0)
        {
            return false;
        }

        return Players.All(p => p.IsPlayerFullyAsleep());
    }

    public void WakeAllPlayers()
    {
        foreach (var player in Players.Where(p => p.IsSleeping))
        {
            player.WakeUp(false, false, true);
        }
    }

    private void NotifyEntityAdded(Entity entity) => OnEntityAdded?.Invoke(entity);

    private void NotifyEntityRemoved(Entity entity) => OnEntityRemoved?.Invoke(entity);

    public List<BlockEntity> GetBlockEntities(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        List<BlockEntity> blockEntInArea = [];

        for (var i = 0; i < BlockEntities.Count; i++)
        {
            var blockEnt = BlockEntities[i];
            if (blockEnt.X >= minX && blockEnt.Y >= minY && blockEnt.Z >= minZ &&
                blockEnt.X < maxX && blockEnt.Y < maxY && blockEnt.Z < maxZ)
            {
                blockEntInArea.Add(blockEnt);
            }
        }

        return blockEntInArea;
    }

    public void TickEntities()
    {
        using (Profiler.Begin("WeatherEffects"))
        {
            for (var i = 0; i < GlobalEntities.Count; ++i)
            {
                var globalEntity = GlobalEntities[i];
                globalEntity.Tick();
                if (globalEntity.Dead)
                {
                    GlobalEntities.RemoveAt(i--);
                }
            }
        }

        using (Profiler.Begin("ClearUnloadedEntities"))
        {
            ProcessQueuedUnloads();
        }

        using (Profiler.Begin("UpdateEntities"))
        {
            for (var i = Entities.Count - 1; i >= 0; --i)
            {
                var entity = Entities[i];

                if (ShouldSkipStandaloneUpdate(entity))
                {
                    continue;
                }

                if (!entity.Dead)
                {
                    UpdateEntity(entity, true);
                }

                if (entity.Dead)
                {
                    RemoveEntityNow(entity);
                }
            }
        }

        using (Profiler.Begin("PathingBatch"))
        {
            _world.PathingRequests.RunBatch();
        }

        _processingDeferred = true;
        using (Profiler.Begin("UpdateBlockEntities"))
        {
            for (var i = BlockEntities.Count - 1; i >= 0; i--)
            {
                var blockEntity = BlockEntities[i];
                if (!blockEntity.IsRemoved())
                {
                    blockEntity.Tick(this);
                }

                if (blockEntity.IsRemoved())
                {
                    BlockEntities.RemoveAt(i);
                    var chunk = _world.ChunkHost.GetChunk(blockEntity.X >> 4, blockEntity.Z >> 4);
                    chunk?.RemoveBlockEntityAt(blockEntity.X & 15, blockEntity.Y, blockEntity.Z & 15);
                }
            }

            _processingDeferred = false;

            if (_blockEntityUpdateQueue.Count > 0)
            {
                foreach (var queuedBlockEntity in _blockEntityUpdateQueue)
                {
                    if (!queuedBlockEntity.IsRemoved())
                    {
                        if (!BlockEntities.Contains(queuedBlockEntity))
                        {
                            BlockEntities.Add(queuedBlockEntity);
                        }

                        var chunk = _world.ChunkHost.GetChunk(queuedBlockEntity.X >> 4, queuedBlockEntity.Z >> 4);
                        chunk?.SetBlockEntity(queuedBlockEntity.X & 15, queuedBlockEntity.Y, queuedBlockEntity.Z & 15, queuedBlockEntity);
                        OnBlockUpdateRequired?.Invoke(queuedBlockEntity.X, queuedBlockEntity.Y, queuedBlockEntity.Z);
                    }
                }

                _blockEntityUpdateQueue.Clear();
            }
        }
    }

    public void UpdateEntity(Entity entity, bool requireLoaded)
    {
        if (OnEntityUpdating != null && !OnEntityUpdating.Invoke(entity))
        {
            return;
        }

        if (entity.Dead)
        {
            return;
        }

        var blockX = MathHelper.Floor(entity.X);
        var blockZ = MathHelper.Floor(entity.Z);
        const byte loadRadius = 32;

        if (!requireLoaded || _world.ChunkHost.IsRegionLoaded(blockX - loadRadius, 0, blockZ - loadRadius, blockX + loadRadius, 128, blockZ + loadRadius))
        {
            entity.LastTickX = entity.X;
            entity.LastTickY = entity.Y;
            entity.LastTickZ = entity.Z;
            entity.PrevYaw = entity.Yaw;
            entity.PrevPitch = entity.Pitch;

            if (requireLoaded && entity.IsPersistent)
            {
                if (entity.Vehicle != null)
                {
                    entity.TickRiding();
                }
                else
                {
                    entity.Tick();
                }
            }

            if (double.IsNaN(entity.X) || double.IsInfinity(entity.X))
            {
                entity.X = entity.LastTickX;
            }

            if (double.IsNaN(entity.Y) || double.IsInfinity(entity.Y))
            {
                entity.Y = entity.LastTickY;
            }

            if (double.IsNaN(entity.Z) || double.IsInfinity(entity.Z))
            {
                entity.Z = entity.LastTickZ;
            }

            if (double.IsNaN(entity.Pitch) || double.IsInfinity(entity.Pitch))
            {
                entity.Pitch = entity.PrevPitch;
            }

            if (double.IsNaN(entity.Yaw) || double.IsInfinity(entity.Yaw))
            {
                entity.Yaw = entity.PrevYaw;
            }

            var newChunkX = MathHelper.Floor(entity.X / 16.0D);
            var newChunkY = MathHelper.Floor(entity.Y / 16.0D);
            var newChunkZ = MathHelper.Floor(entity.Z / 16.0D);

            if (!entity.IsPersistent || entity.ChunkX != newChunkX || entity.ChunkSlice != newChunkY || entity.ChunkZ != newChunkZ)
            {
                if (entity.IsPersistent && _world.ChunkHost.ChunkSource.IsChunkLoaded(entity.ChunkX, entity.ChunkZ))
                {
                    _world.ChunkHost.GetChunk(entity.ChunkX, entity.ChunkZ).RemoveEntity(entity, entity.ChunkSlice);
                }

                if (_world.ChunkHost.ChunkSource.IsChunkLoaded(newChunkX, newChunkZ))
                {
                    entity.IsPersistent = true;
                    _world.ChunkHost.GetChunk(newChunkX, newChunkZ).AddEntity(entity);
                }
                else
                {
                    entity.IsPersistent = false;
                }
            }

            if (requireLoaded && entity.IsPersistent && entity.Passenger != null)
            {
                if (!entity.Passenger.Dead && entity.Passenger.Vehicle == entity)
                {
                    UpdateEntity(entity.Passenger, true);
                }
                else
                {
                    entity.Passenger.Vehicle = null;
                    entity.Passenger = null;
                }
            }
        }
    }

    /// <summary>
    ///     Ticks a vehicle without consulting OnEntityUpdating, so the server can manually tick
    ///     the ridden vehicle when processing a movement packet (avoids filter that skips entities with EntityPlayer passenger).
    /// </summary>
    public void TickVehicleBypassingFilter(Entity vehicle, bool requireLoaded)
    {
        if (vehicle.Dead)
        {
            return;
        }

        var blockX = MathHelper.Floor(vehicle.X);
        var blockZ = MathHelper.Floor(vehicle.Z);
        const byte loadRadius = 32;

        if (!requireLoaded || _world.ChunkHost.IsRegionLoaded(blockX - loadRadius, 0, blockZ - loadRadius, blockX + loadRadius, ChuckFormat.WorldHeight, blockZ + loadRadius))
        {
            vehicle.LastTickX = vehicle.X;
            vehicle.LastTickY = vehicle.Y;
            vehicle.LastTickZ = vehicle.Z;
            vehicle.PrevYaw = vehicle.Yaw;
            vehicle.PrevPitch = vehicle.Pitch;

            if (requireLoaded && vehicle.IsPersistent)
            {
                if (vehicle.Vehicle != null)
                {
                    vehicle.TickRiding();
                }
                else
                {
                    vehicle.Tick();
                }
            }

            if (double.IsNaN(vehicle.X) || double.IsInfinity(vehicle.X))
            {
                vehicle.X = vehicle.LastTickX;
            }

            if (double.IsNaN(vehicle.Y) || double.IsInfinity(vehicle.Y))
            {
                vehicle.Y = vehicle.LastTickY;
            }

            if (double.IsNaN(vehicle.Z) || double.IsInfinity(vehicle.Z))
            {
                vehicle.Z = vehicle.LastTickZ;
            }

            if (double.IsNaN(vehicle.Pitch) || double.IsInfinity(vehicle.Pitch))
            {
                vehicle.Pitch = vehicle.PrevPitch;
            }

            if (double.IsNaN(vehicle.Yaw) || double.IsInfinity(vehicle.Yaw))
            {
                vehicle.Yaw = vehicle.PrevYaw;
            }

            var newChunkX = MathHelper.Floor(vehicle.X / 16.0D);
            var newChunkY = MathHelper.Floor(vehicle.Y / 16.0D);
            var newChunkZ = MathHelper.Floor(vehicle.Z / 16.0D);

            if (!vehicle.IsPersistent || vehicle.ChunkX != newChunkX || vehicle.ChunkSlice != newChunkY || vehicle.ChunkZ != newChunkZ)
            {
                if (vehicle.IsPersistent && _world.ChunkHost.ChunkSource.IsChunkLoaded(vehicle.ChunkX, vehicle.ChunkZ))
                {
                    _world.ChunkHost.GetChunk(vehicle.ChunkX, vehicle.ChunkZ).RemoveEntity(vehicle, vehicle.ChunkSlice);
                }

                if (_world.ChunkHost.ChunkSource.IsChunkLoaded(newChunkX, newChunkZ))
                {
                    vehicle.IsPersistent = true;
                    _world.ChunkHost.GetChunk(newChunkX, newChunkZ).AddEntity(vehicle);
                }
                else
                {
                    vehicle.IsPersistent = false;
                }
            }

            if (requireLoaded && vehicle.IsPersistent && vehicle.Passenger != null)
            {
                if (!vehicle.Passenger.Dead && vehicle.Passenger.Vehicle == vehicle)
                {
                    UpdateEntity(vehicle.Passenger, true);
                }
                else
                {
                    vehicle.Passenger.Vehicle = null;
                    vehicle.Passenger = null;
                }
            }
        }
    }

    public List<Box> GetEntityCollisions(Entity entity, Box area) => GetEntityCollisions(entity, area, new List<Box>());

    internal List<Box> GetEntityCollisionsScratch(Entity entity, Box area)
    {
        _tempCollisionBoxes ??= new List<Box>();
        _tempCollisionBoxes.Clear();
        return GetEntityCollisions(entity, area, _tempCollisionBoxes);
    }

    private List<Box> GetEntityCollisions(Entity entity, Box area, List<Box> collidingBoundingBoxes)
    {
        var minX = MathHelper.Floor(area.MinX);
        var maxX = MathHelper.Floor(area.MaxX + 1.0D);
        var minY = MathHelper.Floor(area.MinY);
        var maxY = MathHelper.Floor(area.MaxY + 1.0D);
        var minZ = MathHelper.Floor(area.MinZ);
        var maxZ = MathHelper.Floor(area.MaxZ + 1.0D);

        if (minX > maxX)
        {
            (minX, maxX) = (maxX, minX);
        }

        if (minY > maxY)
        {
            (minY, maxY) = (maxY, minY);
        }

        if (minZ > maxZ)
        {
            (minZ, maxZ) = (maxZ, minZ);
        }

        for (var x = minX; x < maxX; ++x)
        {
            for (var z = minZ; z < maxZ; ++z)
            {
                if (_world.ChunkHost.IsPosLoaded(x, 64, z))
                {
                    for (var y = minY - 1; y < maxY; ++y)
                    {
                        if (_world.Content.Blocks.TryGetByProtocolId(
                                _world.Reader.GetBlockId(x, y, z), out var block))
                        {
                            block.AddIntersectingBoundingBox(_world.Reader, this, x, y, z, area, collidingBoundingBoxes);
                        }
                    }
                }
            }
        }

        const double expansion = 0.25D;
        _tempCollisionEntities ??= new List<Entity>();
        _tempCollisionEntities.Clear();

        GetEntities(entity, area.Expand(expansion, expansion, expansion), _tempCollisionEntities);

        var collisionCount = 0;
        var maxCollisions = _world.Rules.GetInt(DefaultRules.MaxCollisions);

        foreach (var other in _tempCollisionEntities)
        {
            if (other.Dead)
            {
                continue;
            }

            var entityBox = other.GetBoundingBox();
            if (entityBox != null && entityBox.Value.Intersects(area))
            {
                collidingBoundingBoxes.Add(entityBox.Value);
                collisionCount++;
            }

            entityBox = entity.GetCollisionAgainstShape(other);
            if (entityBox != null && entityBox.Value.Intersects(area))
            {
                collidingBoundingBoxes.Add(entityBox.Value);
                collisionCount++;
            }

            if (collisionCount >= maxCollisions)
            {
                break;
            }
        }

        return collidingBoundingBoxes;
    }

    internal double GetMaxYEntityCollision(Entity entity, Box area)
    {
        const double expansion = 0.25D;
        _tempCollisionEntities ??= new List<Entity>();
        _tempCollisionEntities.Clear();

        GetEntities(entity, area.Expand(expansion, expansion, expansion), _tempCollisionEntities);

        var collisionCount = 0;
        var maxCollisions = _world.Rules.GetInt(DefaultRules.MaxCollisions);

        var maxY = 0.0D;

        foreach (var other in _tempCollisionEntities)
        {
            if (other.Dead)
            {
                continue;
            }

            var entityBox = other.GetBoundingBox();
            if (entityBox != null && entityBox.Value.Intersects(area))
            {
                if (entityBox.Value.MaxY > maxY)
                {
                    maxY = entityBox.Value.MaxY;
                }

                collisionCount++;
            }

            entityBox = entity.GetCollisionAgainstShape(other);
            if (entityBox != null && entityBox.Value.Intersects(area))
            {
                if (entityBox.Value.MaxY > maxY)
                {
                    maxY = entityBox.Value.MaxY;
                }

                collisionCount++;
            }

            if (collisionCount >= maxCollisions)
            {
                break;
            }
        }

        return maxY;
    }

    public List<Entity> GetEntities(Entity? excludeEntity, Box area) => GetEntities(excludeEntity, area, new List<Entity>());

    internal List<Entity> GetEntitiesScratch(Entity? excludeEntity, Box area)
    {
        _tempCollisionEntitiesResult ??= new List<Entity>();
        _tempCollisionEntitiesResult.Clear();
        return GetEntities(excludeEntity, area, _tempCollisionEntitiesResult);
    }

    private List<Entity> GetEntities(Entity? excludeEntity, Box area, List<Entity> results)
    {
        var minChunkX = MathHelper.Floor((area.MinX - 2.0D) / 16.0D);
        var maxChunkX = MathHelper.Floor((area.MaxX + 2.0D) / 16.0D);
        var minChunkZ = MathHelper.Floor((area.MinZ - 2.0D) / 16.0D);
        var maxChunkZ = MathHelper.Floor((area.MaxZ + 2.0D) / 16.0D);

        for (var chunkX = minChunkX; chunkX <= maxChunkX; ++chunkX)
        {
            for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; ++chunkZ)
            {
                if (_world.ChunkHost.ChunkSource.IsChunkLoaded(chunkX, chunkZ))
                {
                    _world.ChunkHost.GetChunk(chunkX, chunkZ).CollectOtherEntities(excludeEntity, area, results);
                }
            }
        }

        return results;
    }

    public List<T> CollectEntitiesOfType<T>(Box area) where T : Entity
    {
        List<T> results = new();

        var minChunkX = MathHelper.Floor((area.MinX - 2.0D) / 16.0D);
        var maxChunkX = MathHelper.Floor((area.MaxX + 2.0D) / 16.0D);
        var minChunkZ = MathHelper.Floor((area.MinZ - 2.0D) / 16.0D);
        var maxChunkZ = MathHelper.Floor((area.MaxZ + 2.0D) / 16.0D);

        for (var chunkX = minChunkX; chunkX <= maxChunkX; ++chunkX)
        {
            for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; ++chunkZ)
            {
                if (_world.ChunkHost.HasChunk(chunkX, chunkZ))
                {
                    _world.ChunkHost.GetChunk(chunkX, chunkZ).CollectEntitiesOfType(area, results);
                }
            }
        }

        return results;
    }

    /// <summary>
    ///     Counts living entities declaring the given <see cref="EntityDefinition.SpawnCategory" />,
    ///     the data-driven replacement for counting by class hierarchy against a mob cap.
    /// </summary>
    public int CountEntitiesInCategory(string category)
    {
        var res = 0;
        foreach (var entity in Entities)
        {
            if (entity is EntityLiving living && living.Definition.SpawnCategory == category)
            {
                res++;
            }
        }

        return res;
    }

    public int CountEntitiesOfType(Type type)
    {
        var res = 0;
        foreach (var entity in Entities)
        {
            if (type.IsInstanceOfType(entity))
            {
                res++;
            }
        }

        return res;
    }

    public void AddEntities(List<Entity> entitiesToAdd)
    {
        Entities.AddRange(entitiesToAdd);
        for (var i = 0; i < entitiesToAdd.Count; ++i)
        {
            _entitiesById[entitiesToAdd[i].ID] = entitiesToAdd[i];
            NotifyEntityAdded(entitiesToAdd[i]);
        }
    }

    public void UnloadEntities(List<Entity> entitiesToUnload) => _entitiesToUnload.AddRange(entitiesToUnload);

    public EntityPlayer? GetClosestPlayer(double x, double y, double z, double range)
    {
        var minDistanceSquared = -1.0D;
        EntityPlayer? closestPlayer = null;
        var rangeSquared = range * range;

        foreach (var player in Players)
        {
            if (!player.GameMode.VisibleToWorld) continue;
            var distanceSquared = player.GetSquaredDistance(x, y, z);

            var withinRange = range < 0.0D || distanceSquared < rangeSquared;
            var isClosestSoFar = minDistanceSquared == -1.0D || distanceSquared < minDistanceSquared;

            if (withinRange && isClosestSoFar)
            {
                minDistanceSquared = distanceSquared;
                closestPlayer = player;
            }
        }

        return closestPlayer;
    }

    public EntityPlayer? GetClosestPlayerTarget(double x, double y, double z, double range)
    {
        var minDistanceSquared = -1.0D;
        EntityPlayer? closestPlayer = null;
        var rangeSquared = range * range;

        foreach (var player in Players)
        {
            if (!player.GameMode.CanBeTargeted || !player.IsAlive) continue;
            var distanceSquared = player.GetSquaredDistance(x, y, z);

            var withinRange = range < 0.0D || distanceSquared < rangeSquared;
            var isClosestSoFar = minDistanceSquared == -1.0D || distanceSquared < minDistanceSquared;

            if (withinRange && isClosestSoFar)
            {
                minDistanceSquared = distanceSquared;
                closestPlayer = player;
            }
        }

        return closestPlayer;
    }

    public EntityPlayer? GetPlayer(string name) => Players.FirstOrDefault(p => p.Name == name);

    public Entity? GetEntityByID(int id) => _entitiesById.TryGetValue(id, out var entity) ? entity : null;

    public void UpdateSleepingPlayers()
    {
        AllPlayersSleeping = Players.Count > 0;
        foreach (var player in Players)
        {
            if (!player.IsSleeping)
            {
                AllPlayersSleeping = false;
                break;
            }
        }
    }

    public bool CanSpawnEntity(Box spawnArea)
    {
        var nearbyEntities = GetEntitiesScratch(null, spawnArea);
        return nearbyEntities.All(entity => entity.Dead || !entity.PreventEntitySpawning);
    }

    public T? GetOrCreateBlockEntity<T>(int x, int y, int z) where T : BlockEntity
    {
        var entity = _blockEntityUpdateQueue.FirstOrDefault(e => e.X == x && e.Y == y && e.Z == z);

        if (entity == null || entity.IsRemoved())
        {
            entity = BlockEntities.FirstOrDefault(e => e.X == x && e.Y == y && e.Z == z);
        }

        if (entity != null && !entity.IsRemoved())
        {
            return entity as T;
        }

        var blockId = _world.Reader.GetBlockId(x, y, z);
        if (blockId == 0 || !_world.Content.Blocks.GetByProtocolId(blockId).HasBlockEntity)
        {
            return null;
        }

        entity = _world.Content.Blocks.GetByProtocolId(blockId).GetBlockEntity();

        if (entity == null)
        {
            return null;
        }

        entity.World = _world;
        entity.X = x;
        entity.Y = y;
        entity.Z = z;

        SetBlockEntity(x, y, z, entity);

        return entity as T;
    }

    public void LoadChunksNearEntity(Entity entity)
    {
        var chunkX = MathHelper.Floor(entity.X / 16.0D);
        var chunkZ = MathHelper.Floor(entity.Z / 16.0D);
        const byte loadRadius = 2;

        for (var x = chunkX - loadRadius; x <= chunkX + loadRadius; ++x)
        {
            for (var z = chunkZ - loadRadius; z <= chunkZ + loadRadius; ++z)
            {
                _world.ChunkHost.GetChunk(x, z);
            }
        }

        if (!Entities.Contains(entity))
        {
            Entities.Add(entity);
        }
    }

    public T? GetBlockEntity<T>(int x, int y, int z) where T : BlockEntity
    {
        var chunk = _world.ChunkHost.GetChunk(x >> 4, z >> 4);
        var entity = chunk.GetBlockEntity(x & 15, y, z & 15)
                     ?? BlockEntities.FirstOrDefault(e => e.X == x && e.Y == y && e.Z == z);
        return entity as T;
    }

    public T? PeekBlockEntity<T>(int x, int y, int z) where T : BlockEntity
    {
        var chunk = _world.ChunkHost.GetChunk(x >> 4, z >> 4);
        var entity = chunk.PeekBlockEntity(x & 15, y, z & 15)
                     ?? BlockEntities.FirstOrDefault(e => e.X == x && e.Y == y && e.Z == z);
        return entity as T;
    }

    public void SetBlockEntity(int x, int y, int z, BlockEntity? blockEntity)
    {
        if (blockEntity == null || blockEntity.IsRemoved())
        {
            return;
        }

        if (_processingDeferred)
        {
            blockEntity.X = x;
            blockEntity.Y = y;
            blockEntity.Z = z;
            _blockEntityUpdateQueue.Add(blockEntity);
        }
        else
        {
            BlockEntities.Add(blockEntity);
            var chunk = _world.ChunkHost.GetChunk(x >> 4, z >> 4);
            if (chunk != null)
            {
                chunk.SetBlockEntity(x & 15, y, z & 15, blockEntity);
            }
        }
    }

    public void RemoveBlockEntity(int x, int y, int z)
    {
        var entity = GetBlockEntity<BlockEntity>(x, y, z);
        if (entity != null && _processingDeferred)
        {
            entity.MarkRemoved();
        }
        else
        {
            _world.ChunkHost.GetChunk(x >> 4, z >> 4)?.RemoveBlockEntityAt(x & 15, y, z & 15);
            if (entity != null)
            {
                BlockEntities.Remove(entity);
            }
        }
    }

    public void ProcessBlockUpdates(IEnumerable<BlockEntity> updates)
    {
        if (_processingDeferred)
        {
            _blockEntityUpdateQueue.AddRange(updates);
        }
        else
        {
            BlockEntities.AddRange(updates);
        }
    }
}
