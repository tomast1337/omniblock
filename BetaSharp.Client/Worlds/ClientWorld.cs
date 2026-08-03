using BetaSharp.Client.Chunks;
using BetaSharp.Client.Network;
using BetaSharp.Entities;
using BetaSharp.Network.Messages;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds;
using BetaSharp.Worlds.Chunks;
using BetaSharp.Worlds.Core;
using BetaSharp.Worlds.Core.Systems;
using BetaSharp.Worlds.Dimensions;
using BetaSharp.Worlds.Storage;

namespace BetaSharp.Client.Worlds;

public class ClientWorld : World
{
    private readonly List<BlockReset> _blockResets = [];
    private readonly ClientNetworkHandler _networkHandler;

    /// <summary>The connection feeding this world. Exposed for the renderer's per-frame
    ///     interpolation sample, which has no other route to it.</summary>
    public ClientNetworkHandler NetworkHandler => _networkHandler;
    private MultiplayerChunkCache _chunkCache;
    private readonly HashSet<Entity> forcedEntities = [];
    private readonly HashSet<Entity> pendingEntities = [];

    public ClientWorld(ClientNetworkHandler netHandler, long seed, int dimId) : base(new EmptyWorldStorage(), "MpServer", new WorldSettings(seed, WorldType.Default, ""), Dimension.FromId(dimId))
    {
        _networkHandler = netHandler;
        SetSpawnPos(new Vec3I(8, 64, 8));

        StateManager = netHandler.ClientPersistentStateManager;
        Entities.OnEntityAdded += HandleEntityAdded;
        Entities.OnEntityRemoved += HandleEntityRemoved;
        Writer.OnBlockChangedWithPrev += HandleBlockChanged;
    }

    public override void Tick()
    {
        SetTime(GetTime() + 1L);

        Environment.UpdateWeatherCycles();

        int ambient = Environment.GetAmbientDarkness(1.0F);
        if (ambient != Environment.AmbientDarkness)
        {
            Environment.AmbientDarkness = ambient;
            Broadcaster.NotifyAmbientDarknessChanged();
        }

        for (int i = 0; i < 10 && pendingEntities.Count > 0; ++i)
        {
            Entity entity = pendingEntities.First();
            if (!Entities.Entities.Contains(entity))
            {
                SpawnOrQueueEntity(entity);
            }
            else
            {
                pendingEntities.Remove(entity);
            }
        }

        _networkHandler.Tick();

        for (int i = 0; i < _blockResets.Count; ++i)
        {
            BlockReset blockReset = _blockResets[i];
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

    public void ClearBlockResets(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        for (int i = 0; i < _blockResets.Count; ++i)
        {
            BlockReset br = _blockResets[i];
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
        if (load)
        {
            _chunkCache.LoadChunk(chunkX, chunkZ);
        }
        else
        {
            _chunkCache.UnloadChunk(chunkX, chunkZ);
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
        bool spawned = Entities.SpawnEntity(entity);
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

    private void HandleBlockChanged(int x, int y, int z, int previousId, int previousMeta, int newId, int newMeta)
    {
        _blockResets.Add(new BlockReset(this, x, y, z, previousId, previousMeta));
    }

    public void ForceEntity(int networkId, Entity ent)
    {
        Entity? existingEnt = GetEntity(networkId);
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

    public Entity? GetEntity(int networkId)
    {
        return Entities.GetEntityByID(networkId);
    }

    public Entity? RemoveEntityFromWorld(int networkId)
    {
        Entity? ent = GetEntity(networkId);
        if (ent != null)
        {
            forcedEntities.Remove(ent);
            Remove(ent);
        }

        return ent;
    }

    /// <summary>
    ///     Applies one position's worth of server state: its block and its light.
    /// </summary>
    /// <remarks>
    ///     The light is applied whether or not the block changed. The server announces a position
    ///     whenever its light changes, and a light-only change leaves the block byte-identical to
    ///     what this client already holds, which is precisely the case
    ///     <see cref="Chunk.SetBlock" /> refuses. Gating the light on the block having changed
    ///     discards every such update, and nothing resends it.
    /// </remarks>
    public bool SetBlockWithMetaFromPacket(int minX, int minY, int minZ, int blockId, int meta, byte light)
    {
        ClearBlockResets(minX, minY, minZ, minX, minY, minZ);
        bool blockChanged = Writer.SetBlockWithoutNotifyingNeighbors(minX, minY, minZ, blockId, meta);

        bool lightChanged = BlockHost.HasChunk(minX >> 4, minZ >> 4)
            && BlockHost.GetChunk(minX >> 4, minZ >> 4)
                .SetPackedLight(minX & 15, minY, minZ & 15, light);

        if (blockChanged || lightChanged)
        {
            BlockUpdate(minX, minY, minZ, blockId);
        }

        return blockChanged;
    }

    public override void Disconnect()
    {
        _networkHandler.SendMessage(new DisconnectMessage { Reason = "Quitting" });
        _networkHandler.Disconnect();
    }
}
