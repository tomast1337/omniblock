using System.Collections.Concurrent;
using OmniBlock.Client.Chunks;
using OmniBlock.Client.Network;
using OmniBlock.Entities;
using OmniBlock.Network.Messages;
using OmniBlock.Profiling;
using OmniBlock.Registries;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Dimensions;
using OmniBlock.Worlds.Lod;
using OmniBlock.Worlds.Storage;

namespace OmniBlock.Client.Worlds;

public class ClientWorld : World
{
    private readonly List<BlockReset> _blockResets = [];
    private readonly List<ClientEntityDespawnVisual> _distanceDespawnVisuals = [];
    private readonly HashSet<Entity> forcedEntities = [];
    private readonly HashSet<Entity> pendingEntities = [];
    private readonly ConcurrentQueue<TerrainLodTileTransfer> _terrainLodTiles = new();
    private readonly ConcurrentQueue<TerrainLodTileStatusUpdate> _terrainLodStatuses = new();
    private long _terrainLodTilesEnqueued;
    private long _terrainLodTilesDequeued;
    private int _terrainLodTileQueuePeak;
    private MultiplayerChunkCache? _chunkCache;

    public ClientWorld(
        ClientNetworkHandler netHandler,
        long seed,
        int dimId,
        ContentRuntime content,
        TerrainLodCacheStore? terrainLodCache = null) : base(new EmptyWorldStorage(), "MpServer", new WorldSettings(seed, content.WorldTypes.Get("omniblock:default")), Dimension.FromId(dimId, content),
        content)
    {
        NetworkHandler = netHandler;
        TerrainLodCache = terrainLodCache;
        SetSpawnPos(new Vec3I(8, 64, 8));

        StateManager = netHandler.ClientPersistentStateManager;
        Entities.OnEntityAdded += HandleEntityAdded;
        Entities.OnEntityRemoved += HandleEntityRemoved;
        Writer.OnBlockChangedWithPrev += HandleBlockChanged;
    }

    /// <summary>
    ///     The connection feeding this world. Exposed for the renderer's per-frame
    ///     interpolation sample, which has no other route to it.
    /// </summary>
    public ClientNetworkHandler NetworkHandler { get; }
    internal TerrainLodCacheStore? TerrainLodCache { get; }
    internal IReadOnlyList<ClientEntityDespawnVisual> DistanceDespawnVisuals => _distanceDespawnVisuals;
    internal int DistanceDespawnPresentationCount { get; private set; }

    internal long TerrainLodTilesEnqueued => Interlocked.Read(ref _terrainLodTilesEnqueued);
    internal long TerrainLodTilesDequeued => Interlocked.Read(ref _terrainLodTilesDequeued);
    internal int TerrainLodTileQueueDepth => _terrainLodTiles.Count;
    internal int TerrainLodTileQueuePeak => Volatile.Read(ref _terrainLodTileQueuePeak);

    internal void EnqueueTerrainLodTile(TerrainLodColumnTile tile, int wireBytes)
    {
        _terrainLodTiles.Enqueue(new TerrainLodTileTransfer(
            tile ?? throw new ArgumentNullException(nameof(tile)), wireBytes));
        Interlocked.Increment(ref _terrainLodTilesEnqueued);
        var depth = _terrainLodTiles.Count;
        var peak = Volatile.Read(ref _terrainLodTileQueuePeak);
        while (depth > peak)
        {
            var observed = Interlocked.CompareExchange(
                ref _terrainLodTileQueuePeak, depth, peak);
            if (observed == peak) break;
            peak = observed;
        }
    }

    internal bool TryDequeueTerrainLodTile(out TerrainLodTileTransfer transfer)
    {
        if (!_terrainLodTiles.TryDequeue(out transfer)) return false;
        Interlocked.Increment(ref _terrainLodTilesDequeued);
        return true;
    }

    internal void EnqueueTerrainLodStatus(TerrainLodTileKey tile, TerrainLodTileStatus status) =>
        _terrainLodStatuses.Enqueue(new TerrainLodTileStatusUpdate(tile, status));

    internal bool TryDequeueTerrainLodStatus(out TerrainLodTileStatusUpdate status) =>
        _terrainLodStatuses.TryDequeue(out status);

    public override void Tick()
    {
        using (Profiler.Begin("WorldClock"))
        {
            SetTime(GetTime() + 1L);

            Environment.UpdateWeatherCycles();

            var ambient = Environment.GetAmbientDarkness(1.0F);
            if (ambient != Environment.AmbientDarkness)
            {
                Environment.AmbientDarkness = ambient;
                Broadcaster.NotifyAmbientDarknessChanged();
            }
        }

        using (Profiler.Begin("PendingEntities"))
        {
            for (var i = 0; i < 10 && pendingEntities.Count > 0; ++i)
            {
                var entity = pendingEntities.First();
                if (!Entities.Entities.Contains(entity))
                {
                    SpawnOrQueueEntity(entity);
                }
                else
                {
                    pendingEntities.Remove(entity);
                }
            }
        }

        using (Profiler.Begin("Network"))
        {
            NetworkHandler.Tick();
        }

        using (Profiler.Begin("DespawnVisuals"))
        {
            for (var i = _distanceDespawnVisuals.Count - 1; i >= 0; --i)
            {
                if (!_distanceDespawnVisuals[i].Tick())
                    _distanceDespawnVisuals.RemoveAt(i);
            }
        }

        using (Profiler.Begin("BlockResets"))
        {
            for (var i = 0; i < _blockResets.Count; ++i)
            {
                var blockReset = _blockResets[i];
                if (--blockReset.Delay == 0)
                {
                    Writer.OnBlockChangedWithPrev -= HandleBlockChanged;

                    Writer.SetBlockWithoutNotifyingNeighbors(blockReset.X, blockReset.Y, blockReset.Z, blockReset.BlockId, blockReset.Meta);
                    Broadcaster.BlockUpdateEvent(blockReset.X, blockReset.Y, blockReset.Z);

                    Writer.OnBlockChangedWithPrev += HandleBlockChanged;

                    _blockResets.RemoveAt(i--);
                }
            }
        }
    }

    public void ClearBlockResets(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        for (var i = 0; i < _blockResets.Count; ++i)
        {
            var br = _blockResets[i];
            if (br.X >= minX && br.Y >= minY && br.Z >= minZ &&
                br.X <= maxX && br.Y <= maxY && br.Z <= maxZ)
            {
                _blockResets.RemoveAt(i--);
            }
        }
    }

    protected override IChunkSource CreateChunkCache()
    {
        _chunkCache = new MultiplayerChunkCache(this);
        return _chunkCache;
    }

    public override void UpdateSpawnPosition() => SetSpawnPos(new Vec3I(8, 64, 8));

    protected override void ManageChunkUpdatesAndEvents()
    {
    }

    public void UpdateChunk(int chunkX, int chunkZ, bool load)
    {
        var chunkCache = _chunkCache ?? throw new InvalidOperationException("Client chunk cache is not initialized.");
        if (load)
        {
            chunkCache.LoadChunk(chunkX, chunkZ);
        }
        else
        {
            chunkCache.UnloadChunk(chunkX, chunkZ);
        }

        if (!load)
        {
            setBlocksDirty(chunkX * 16, 0, chunkZ * 16, chunkX * 16 + 15, ChuckFormat.WorldHeight, chunkZ * 16 + 15);
        }
    }

    /// <summary>
    ///     Spawns an entity the server told this client about, queueing it for a later attempt if
    ///     its chunk has not arrived yet.
    /// </summary>
    /// <remarks>
    ///     Deliberately not named <c>SpawnEntity</c>: <see cref="World.SpawnEntity" /> is not
    ///     virtual, so a same-named member here would be reached only through a
    ///     <see cref="ClientWorld" />-typed reference. Every caller holding a <see cref="World" />
    ///     would silently get the base version and skip the queueing this exists for.
    /// </remarks>
    private bool SpawnOrQueueEntity(Entity entity)
    {
        var spawned = Entities.SpawnEntity(entity);
        forcedEntities.Add(entity);
        if (!spawned)
        {
            pendingEntities.Add(entity);
        }

        return spawned;
    }

    private void Remove(Entity ent)
    {
        Entities.Remove(ent);
        forcedEntities.Remove(ent);
    }

    private void HandleEntityAdded(Entity ent)
    {
        if (pendingEntities.Contains(ent))
        {
            pendingEntities.Remove(ent);
        }
    }

    private void HandleEntityRemoved(Entity ent)
    {
        if (forcedEntities.Contains(ent))
        {
            pendingEntities.Add(ent);
        }
    }

    private void HandleBlockChanged(int x, int y, int z, int previousId, int previousMeta, int newId, int newMeta) => _blockResets.Add(new BlockReset(this, x, y, z, previousId, previousMeta));

    public void ForceEntity(int networkId, Entity ent)
    {
        var existingEnt = GetEntity(networkId);
        if (existingEnt != null)
        {
            forcedEntities.Remove(existingEnt);
            Remove(existingEnt);
        }

        forcedEntities.Add(ent);
        ent.ID = networkId;

        if (!SpawnOrQueueEntity(ent))
        {
            pendingEntities.Add(ent);
        }
    }

    public Entity? GetEntity(int networkId) => Entities.GetEntityByID(networkId);

    public Entity? RemoveEntityFromWorld(int networkId)
    {
        var ent = GetEntity(networkId);
        if (ent != null)
        {
            forcedEntities.Remove(ent);
            Remove(ent);
        }

        return ent;
    }

    /// <summary>
    ///     Retains no gameplay entity: only a bounded render snapshot that sinks and fades after a
    ///     genuine population despawn. Tracking-range removal must never enter this path.
    /// </summary>
    public void BeginDistanceDespawnPresentation(Entity entity)
    {
        const int maximumVisuals = 256;
        if (_distanceDespawnVisuals.Count == maximumVisuals)
            _distanceDespawnVisuals.RemoveAt(0);
        _distanceDespawnVisuals.Add(new ClientEntityDespawnVisual(entity));
        DistanceDespawnPresentationCount++;

        for (var i = 0; i < 7; i++)
        {
            var angle = entity.Random.NextFloat() * MathF.PI * 2;
            var radius = entity.Random.NextFloat() * entity.Width * 0.6;
            Broadcaster.AddParticle("smoke",
                entity.X + Math.Cos(angle) * radius,
                entity.Y + entity.Random.NextFloat() * entity.Height * 0.8,
                entity.Z + Math.Sin(angle) * radius,
                Math.Cos(angle) * 0.015,
                0.015 + entity.Random.NextFloat() * 0.02,
                Math.Sin(angle) * 0.015);
        }
    }

    /// <summary>
    ///     Applies one position's worth of server state: its block.
    /// </summary>
    /// <remarks>
    ///     No light. It used to take a byte from the same message and write it here unconditionally,
    ///     which made every construction of that message that forgot to fill the field overwrite
    ///     this cell with darkness — and two of them did. Light arrives on
    ///     <c>LightSectionsMessage</c>, as sections, where nothing is inferred from a field being
    ///     absent.
    /// </remarks>
    public bool SetBlockWithMetaFromPacket(int minX, int minY, int minZ, int blockId, int meta)
    {
        ClearBlockResets(minX, minY, minZ, minX, minY, minZ);

        if (Writer.SetBlockWithoutNotifyingNeighbors(minX, minY, minZ, blockId, meta))
        {
            BlockUpdate(minX, minY, minZ, blockId);
            return true;
        }

        return false;
    }

    public override void Disconnect()
    {
        NetworkHandler.SendMessage(new DisconnectMessage
        {
            Reason = "Quitting"
        });
        NetworkHandler.Disconnect();
    }
}

internal readonly record struct TerrainLodTileTransfer(TerrainLodColumnTile Tile, int WireBytes);
internal readonly record struct TerrainLodTileStatusUpdate(
    TerrainLodTileKey Tile,
    TerrainLodTileStatus Status);

internal sealed class ClientEntityDespawnVisual(Entity entity)
{
    internal const int DurationTicks = 12;
    public Entity Entity { get; } = entity;
    public int Age { get; private set; }

    public bool Tick() => ++Age < DurationTicks;

    public float Progress(float partialTicks) =>
        Math.Clamp((Age + partialTicks) / DurationTicks, 0.0f, 1.0f);
}
