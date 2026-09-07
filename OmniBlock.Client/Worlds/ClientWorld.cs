using OmniBlock.Client.Chunks;
using OmniBlock.Client.Network;
using OmniBlock.Entities;
using OmniBlock.Network.Messages;
using OmniBlock.Registries;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core;
using OmniBlock.Worlds.Core.Systems;
using OmniBlock.Worlds.Dimensions;
using OmniBlock.Worlds.Storage;

namespace OmniBlock.Client.Worlds;

public class ClientWorld : World
{
    private readonly List<BlockReset> _blockResets = [];
    private readonly HashSet<Entity> forcedEntities = [];
    private readonly HashSet<Entity> pendingEntities = [];
    private MultiplayerChunkCache _chunkCache;

    public ClientWorld(ClientNetworkHandler netHandler, long seed, int dimId, ContentRuntime content) : base(new EmptyWorldStorage(), "MpServer", new WorldSettings(seed, WorldType.Default), Dimension.FromId(dimId), content)
    {
        NetworkHandler = netHandler;
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

    public override void Tick()
    {
        SetTime(GetTime() + 1L);

        Environment.UpdateWeatherCycles();

        var ambient = Environment.GetAmbientDarkness(1.0F);
        if (ambient != Environment.AmbientDarkness)
        {
            Environment.AmbientDarkness = ambient;
            Broadcaster.NotifyAmbientDarknessChanged();
        }

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

        NetworkHandler.Tick();

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
