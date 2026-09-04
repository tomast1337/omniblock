using Microsoft.Extensions.Logging;
using OmniBlock.Blocks.Entities;
using OmniBlock.Entities;
using OmniBlock.Network.Messages;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core;

namespace OmniBlock.Server;

internal class ChunkMap
{
    private readonly Dictionary<long, TrackedChunk> _chunkMapping = new();
    private readonly List<TrackedChunk> _chunksToUpdate = [];
    private readonly int _dimensionId;
    private readonly ILogger<ChunkMap> _logger = Log.Instance.For<ChunkMap>();
    private readonly OmniBlockServer _server;
    public readonly ChunkLoadingQueue loadQueue;
    private int _viewDistance;
    public List<ServerPlayerEntity> players = [];

    public ChunkMap(OmniBlockServer server, int dimensionId, int viewRadius)
    {
        if (viewRadius > 32)
        {
            throw new ArgumentException("Too big view Radius! Max is 32.", nameof(viewRadius));
        }

        if (viewRadius < 4)
        {
            throw new ArgumentException("Too small view Radius! Min is 4.", nameof(viewRadius));
        }

        _viewDistance = viewRadius;
        _server = server;
        _dimensionId = dimensionId;
        loadQueue = new ChunkLoadingQueue(this);
    }

    public ServerWorld getWorld() => _server.getWorld(_dimensionId);

    public void SetViewDistance(int newDistance)
    {
        var oldDistance = _viewDistance;
        _viewDistance = newDistance;

        if (newDistance < oldDistance)
        {
            // Unload chunks that are now out of view distance
            foreach (var player in players)
            {
                var px = (int)player.LastX >> 4;
                var pz = (int)player.LastZ >> 4;

                foreach (var item in GetChunks(player, oldDistance))
                {
                    if (isWithinViewDistance(item.X, item.Z, px, pz))
                    {
                        continue;
                    }

                    if (GetOrCreateChunk(item.X, item.Z, false) is TrackedChunk chunk)
                    {
                        chunk.removePlayer(player);
                    }
                    else
                    {
                        loadQueue.Remove(item.X, item.Z, player);
                        player.CancelChunkSend(item);
                    }
                }
            }
        }
        else if (newDistance > oldDistance)
        {
            // Load chunks that are now within view distance
            foreach (var player in players)
            {
                var px = (int)player.LastX >> 4;
                var pz = (int)player.LastZ >> 4;

                foreach (var item in GetChunks(player))
                {
                    if (isWithinOldViewDistance(item.X, item.Z, px, pz, oldDistance))
                    {
                        continue;
                    }

                    if (GetOrCreateChunk(item.X, item.Z, false) is TrackedChunk chunk)
                    {
                        if (!chunk.HasPlayer(player))
                        {
                            chunk.addPlayer(player);
                        }
                    }
                    else
                    {
                        loadQueue.Add(item.X, item.Z, player);
                    }
                }
            }
        }
    }

    private static bool isWithinOldViewDistance(int chunkX, int chunkZ, int centerX, int centerZ, int oldDist)
    {
        var dx = chunkX - centerX;
        var dz = chunkZ - centerZ;
        return dx >= -oldDist && dx <= oldDist && dz >= -oldDist && dz <= oldDist;
    }

    public void updateChunks()
    {
        foreach (var chunk in _chunksToUpdate)
        {
            chunk.updateChunk();
        }

        _chunksToUpdate.Clear();

        // Every watched chunk, not only the ones a block change put on the list above. Light
        // changes without any block changing, and the pass that first lights a chunk writes the
        // arrays directly and so puts nothing on any list at all. Testing a mask is a field read,
        // which is what makes sweeping the whole set affordable.
        foreach (var chunk in _chunkMapping.Values)
        {
            chunk.sendDirtyLight();
        }

        loadQueue.Tick();
    }

    public static long GetChunkHash(int chunkX, int chunkZ) => (chunkX + 2147483647L) | ((chunkZ + 2147483647L) << 32);

    internal TrackedChunk? GetOrCreateChunk(int chunkX, int chunkZ, bool createIfAbsent)
    {
        var chunkHash = GetChunkHash(chunkX, chunkZ);
        var chunk = _chunkMapping.GetValueOrDefault(chunkHash);
        if (chunk == null && createIfAbsent)
        {
            chunk = new TrackedChunk(this, chunkX, chunkZ);
            _chunkMapping[chunkHash] = chunk;
        }

        return chunk;
    }

    public void markBlockForUpdate(int x, int y, int z)
    {
        var chunkX = x >> 4;
        var chunkZ = z >> 4;
        var trackedChunk = GetOrCreateChunk(chunkX, chunkZ, false);
        trackedChunk?.updatePlayerChunks(x & 15, y, z & 15);
    }

    internal bool IsChunkTrackedAndSent(int chunkX, int chunkZ)
    {
        var key = GetChunkHash(chunkX, chunkZ);
        return _chunkMapping.TryGetValue(key, out var trackedChunk) && trackedChunk != null && trackedChunk.HasPlayersAndHasBeenSent();
    }

    internal static bool HasPlayerReceivedChunkTerrain(ServerPlayerEntity player, int chunkX, int chunkZ)
    {
        var pos = new ChunkPos(chunkX, chunkZ);
        return player.ChunksTerrainSentToClient.ContainsKey(pos);
    }

    public void OnChunkDecorated(int chunkX, int chunkZ)
    {
        int[] dxs = [0, 1, 0, 1];
        int[] dzs = [0, 0, 1, 1];

        for (var c = 0; c < 4; c++)
        {
            var cx = chunkX + dxs[c];
            var cz = chunkZ + dzs[c];

            var key = GetChunkHash(cx, cz);
            if (!_chunkMapping.TryGetValue(key, out var trackedChunk) || trackedChunk == null) continue;

            trackedChunk.updatePlayerChunks(0, 50, 0);
            trackedChunk.updatePlayerChunks(15, 124, 15);

            for (var i = 0; i < 8; i++)
            {
                trackedChunk.updatePlayerChunks(i, 64, i);
            }
        }
    }

    public void addPlayer(ServerPlayerEntity player)
    {
        player.ResetChunkStreamingState();
        player.LastX = player.X;
        player.LastZ = player.Z;

        var isHomeChunk = true;
        foreach (var item in GetChunks(player))
        {
            // The player's own chunk (always first out of GetChunks) is force-loaded
            // synchronously — the async worker pool gives no per-tick guarantee it'll be ready
            // before gameplay starts, and without this the player can fall through unloaded
            // terrain under their own feet.
            if (GetOrCreateChunk(item.X, item.Z, isHomeChunk) is { } centerChunk)
            {
                centerChunk.addPlayer(player);
            }
            else
            {
                loadQueue.Add(item.X, item.Z, player);
            }

            isHomeChunk = false;
        }

        players.Add(player);
    }

    public void removePlayer(ServerPlayerEntity player)
    {
        foreach (var item in GetChunks(player))
        {
            var chunk = GetOrCreateChunk(item.X, item.Z, false);
            chunk?.removePlayer(player);
        }

        players.Remove(player);
        loadQueue.RemovePlayer(player);
        player.ResetChunkStreamingState();
    }

    private bool isWithinViewDistance(int chunkX, int chunkZ, int centerX, int centerZ)
    {
        var deltaX = chunkX - centerX;
        var deltaZ = chunkZ - centerZ;
        return deltaX >= -_viewDistance && deltaX <= _viewDistance && deltaZ >= -_viewDistance && deltaZ <= _viewDistance;
    }

    public void updatePlayerChunks(ServerPlayerEntity player)
    {
        var playerChunkCenterX = (int)player.X >> 4;
        var playerChunkCenterZ = (int)player.Z >> 4;
        var playerLastChunkCenterX = (int)player.LastX >> 4;
        var playerLastChunkCenterZ = (int)player.LastZ >> 4;
        var playerChunkCenterDeltaX = playerChunkCenterX - playerLastChunkCenterX;
        var playerChunkCenterDeltaZ = playerChunkCenterZ - playerLastChunkCenterZ;
        if (playerChunkCenterDeltaX == 0 && playerChunkCenterDeltaZ == 0)
        {
            return;
        }

        player.UpdateChunkStreamingMotion(playerChunkCenterDeltaX, playerChunkCenterDeltaZ);

        for (var x = playerChunkCenterX - _viewDistance; x <= playerChunkCenterX + _viewDistance; x++)
        {
            for (var z = playerChunkCenterZ - _viewDistance; z <= playerChunkCenterZ + _viewDistance; z++)
            {
                if (!isWithinViewDistance(x, z, playerLastChunkCenterX, playerLastChunkCenterZ))
                {
                    if (GetOrCreateChunk(x, z, false) is { } chunk)
                    {
                        if (!chunk.HasPlayer(player))
                        {
                            chunk.addPlayer(player);
                        }
                    }
                    else
                    {
                        loadQueue.Add(x, z, player);
                    }
                }

                if (!isWithinViewDistance(x - playerChunkCenterDeltaX, z - playerChunkCenterDeltaZ, playerChunkCenterX, playerChunkCenterZ))
                {
                    var oldChunkX = x - playerChunkCenterDeltaX;
                    var oldChunkZ = z - playerChunkCenterDeltaZ;
                    if (GetOrCreateChunk(oldChunkX, oldChunkZ, false) is TrackedChunk chunk)
                    {
                        chunk.removePlayer(player);
                    }
                    else
                    {
                        ChunkPos oldChunkPos = new(oldChunkX, oldChunkZ);
                        loadQueue.Remove(oldChunkX, oldChunkZ, player);
                        player.CancelChunkSend(oldChunkPos);
                    }
                }
            }
        }

        player.LastX = player.X;
        player.LastZ = player.Z;
    }

    public int getBlockViewDistance() => _viewDistance * 16 - 16;

    private ReadOnlySpan<ChunkPos> GetChunks(ServerPlayerEntity player) => GetChunks(player, _viewDistance);

    private static ReadOnlySpan<ChunkPos> GetChunks(ServerPlayerEntity player, int radius)
    {
        var playerChunkX = (int)player.X >> 4;
        var playerChunkZ = (int)player.Z >> 4;
        var diameter = radius * 2 + 1;
        var chunks = new ChunkPos[diameter * diameter];
        var index = 0;

        chunks[index++] = new ChunkPos(playerChunkX, playerChunkZ);

        for (var currentRadius = 1; currentRadius <= radius; currentRadius++)
        {
            for (var dx = -currentRadius; dx <= currentRadius; dx++)
                chunks[index++] = new ChunkPos(playerChunkX + dx, playerChunkZ - currentRadius);

            for (var dz = -currentRadius + 1; dz <= currentRadius; dz++)
                chunks[index++] = new ChunkPos(playerChunkX + currentRadius, playerChunkZ + dz);

            for (var dx = currentRadius - 1; dx >= -currentRadius; dx--)
                chunks[index++] = new ChunkPos(playerChunkX + dx, playerChunkZ + currentRadius);

            for (var dz = currentRadius - 1; dz >= -currentRadius + 1; dz--)
                chunks[index++] = new ChunkPos(playerChunkX - currentRadius, playerChunkZ + dz);
        }

        return chunks;
    }

    internal class TrackedChunk
    {
        private const int MaxDirtyBlocks = 10;
        private readonly ChunkMap _chunkMap;
        private readonly ChunkPos _chunkPos;
        private readonly short[] _dirtyBlocks;
        private readonly ILogger<TrackedChunk> _logger = Log.Instance.For<TrackedChunk>();
        private readonly HashSet<ServerPlayerEntity> _players;
        private int _dirtyBlockCount;
        private int _dirtyBlockMaxX;
        private int _dirtyBlockMaxY;
        private int _dirtyBlockMaxZ;
        private int _dirtyBlockMinX;
        private int _dirtyBlockMinY;
        private int _dirtyBlockMinZ;
        private bool _hasBeenSent;

        public TrackedChunk(ChunkMap chunkMap, int chunkX, int chunkZ)
        {
            _chunkMap = chunkMap;
            _players = [];
            _dirtyBlocks = new short[MaxDirtyBlocks];
            _dirtyBlockCount = 0;
            _chunkPos = new ChunkPos(chunkX, chunkZ);
            chunkMap.getWorld().ChunkCache.LoadChunk(chunkX, chunkZ);
        }

        public bool HasPlayer(ServerPlayerEntity player) => _players.Contains(player);

        public void addPlayer(ServerPlayerEntity player)
        {
            if (!_players.Add(player))
            {
                return;
            }

            if (player.ActiveChunks.Add(_chunkPos))
            {
                player.NetworkHandler.SendMessage(new ChunkStatusUpdateMessage
                {
                    X = _chunkPos.X,
                    Z = _chunkPos.Z,
                    Loaded = true
                });
            }

            player.ScheduleChunkSend(_chunkPos);
        }

        public void EnqueueForTrackingPlayers()
        {
            if (_hasBeenSent)
            {
                return;
            }

            _hasBeenSent = true;

            foreach (var player in _players)
            {
                player.PendingChunkUpdates.Enqueue(_chunkPos);
            }
        }

        internal bool HasPlayersAndHasBeenSent() => _hasBeenSent && _players.Count > 0;

        public void removePlayer(ServerPlayerEntity player)
        {
            if (_players.Remove(player))
            {
                if (_players.Count == 0)
                {
                    var chunkHash = GetChunkHash(_chunkPos.X, _chunkPos.Z);
                    _chunkMap._chunkMapping.Remove(chunkHash);
                    if (_dirtyBlockCount > 0)
                    {
                        _chunkMap._chunksToUpdate.Remove(this);
                    }

                    _chunkMap.getWorld().ChunkCache.isLoaded(_chunkPos.X, _chunkPos.Z);
                }

                if (player.ActiveChunks.Remove(_chunkPos))
                {
                    player.NetworkHandler.SendMessage(new ChunkStatusUpdateMessage
                    {
                        X = _chunkPos.X,
                        Z = _chunkPos.Z,
                        Loaded = false
                    });
                }

                player.CancelChunkSend(_chunkPos);
            }
        }

        public void updatePlayerChunks(int x, int y, int z)
        {
            if (_dirtyBlockCount == 0)
            {
                _chunkMap._chunksToUpdate.Add(this);
                _dirtyBlockMinX = _dirtyBlockMaxX = x;
                _dirtyBlockMinY = _dirtyBlockMaxY = y;
                _dirtyBlockMinZ = _dirtyBlockMaxZ = z;
            }

            if (_dirtyBlockMinX > x)
            {
                _dirtyBlockMinX = x;
            }

            if (_dirtyBlockMinY > y)
            {
                _dirtyBlockMinY = y;
            }

            if (_dirtyBlockMinZ > z)
            {
                _dirtyBlockMinZ = z;
            }

            if (_dirtyBlockMaxX < x)
            {
                _dirtyBlockMaxX = x;
            }

            if (_dirtyBlockMaxY < y)
            {
                _dirtyBlockMaxY = y;
            }

            if (_dirtyBlockMaxZ < z)
            {
                _dirtyBlockMaxZ = z;
            }

            if (_dirtyBlockCount < MaxDirtyBlocks)
            {
                // for some reason, this uses a 255 value for y.
                var blockArrayIndex = (short)((x << 12) | (z << 8) | y);

                for (var i = 0; i < _dirtyBlockCount; i++)
                {
                    if (_dirtyBlocks[i] == blockArrayIndex)
                    {
                        return;
                    }
                }

                _dirtyBlocks[_dirtyBlockCount++] = blockArrayIndex;
            }
        }

        public void sendMessageToPlayers(Message message)
        {
            foreach (var serverPlayer in _players)
            {
                if (serverPlayer.ActiveChunks.Contains(_chunkPos))
                {
                    serverPlayer.NetworkHandler.SendMessage(message);
                }
            }
        }

        public void updateChunk()
        {
            var sWorld = _chunkMap.getWorld();
            if (_dirtyBlockCount != 0)
            {
                if (_dirtyBlockCount == 1)
                {
                    var worldX = _chunkPos.X * 16 + _dirtyBlockMinX;
                    var worldY = _dirtyBlockMinY;
                    var worldZ = _chunkPos.Z * 16 + _dirtyBlockMinZ;
                    sendMessageToPlayers(new BlockUpdateMessage
                    {
                        X = worldX,
                        Y = (sbyte)worldY,
                        Z = worldZ,
                        BlockRawId = (byte)sWorld.Reader.GetBlockId(worldX, worldY, worldZ),
                        BlockMetadata = (byte)sWorld.Reader.GetBlockMeta(worldX, worldY, worldZ)
                    });
                    if (sWorld.Content.Blocks.TryGetByProtocolId(sWorld.Reader.GetBlockId(worldX, worldY, worldZ), out var block)
                        && block.HasBlockEntity)
                    {
                        sendBlockEntityUpdate(sWorld.Entities.GetBlockEntity<BlockEntity>(worldX, worldY, worldZ));
                    }
                }
                else if (_dirtyBlockCount == MaxDirtyBlocks)
                {
                    _dirtyBlockMinY = _dirtyBlockMinY / 2 * 2;
                    _dirtyBlockMaxY = (_dirtyBlockMaxY / 2 + 1) * 2;
                    var worldX = _dirtyBlockMinX + _chunkPos.X * 16;
                    var worldY = _dirtyBlockMinY;
                    var worldZ = _dirtyBlockMinZ + _chunkPos.Z * 16;
                    var sizeX = _dirtyBlockMaxX - _dirtyBlockMinX + 1;
                    var sizeY = _dirtyBlockMaxY - _dirtyBlockMinY + 2;
                    var sizeZ = _dirtyBlockMaxZ - _dirtyBlockMinZ + 1;
                    sendMessageToPlayers(RegionDataMessage.Of(worldX, worldY, worldZ, sizeX, sizeY, sizeZ, sWorld));
                    var blockEntities = sWorld.getBlockEntities(worldX, worldY, worldZ, worldX + sizeX, worldY + sizeY, worldZ + sizeZ);

                    for (var i = 0; i < blockEntities.Count; i++)
                    {
                        sendBlockEntityUpdate(blockEntities[i]);
                    }
                }
                else
                {
                    var delta = new ChunkDeltaUpdateMessage
                    {
                        X = _chunkPos.X,
                        Z = _chunkPos.Z
                    };
                    delta.Positions = new short[_dirtyBlockCount];
                    delta.BlockRawIds = new byte[_dirtyBlockCount];
                    delta.BlockMetadata = new byte[_dirtyBlockCount];
                    var chunk = sWorld.BlockHost.GetChunk(_chunkPos.X, _chunkPos.Z);
                    for (var i = 0; i < _dirtyBlockCount; i++)
                    {
                        var bx = (_dirtyBlocks[i] >> 12) & 15;
                        var bz = (_dirtyBlocks[i] >> 8) & 15;
                        var by = _dirtyBlocks[i] & 255;
                        delta.Positions[i] = _dirtyBlocks[i];
                        delta.BlockRawIds[i] = (byte)chunk.GetBlockId(bx, by, bz);
                        delta.BlockMetadata[i] = (byte)chunk.GetBlockMeta(bx, by, bz);
                    }

                    sendMessageToPlayers(delta);

                    for (var i = 0; i < _dirtyBlockCount; i++)
                    {
                        var worldX = _chunkPos.X * 16 + ((_dirtyBlocks[i] >> 12) & 15);
                        var worldY = _dirtyBlocks[i] & 0xFF;
                        var worldZ = _chunkPos.Z * 16 + ((_dirtyBlocks[i] >> 8) & 15);
                        if (sWorld.Content.Blocks.TryGetByProtocolId(sWorld.Reader.GetBlockId(worldX, worldY, worldZ), out var block)
                            && block.HasBlockEntity)
                        {
                            sendBlockEntityUpdate(sWorld.Entities.GetBlockEntity<BlockEntity>(worldX, worldY, worldZ));
                        }
                    }
                }

                _dirtyBlockCount = 0;
            }
        }

        /// <summary>
        ///     Sends whatever sections of this chunk have had light written since the last sweep.
        /// </summary>
        /// <remarks>
        ///     The mask is taken — read and cleared — whether or not anyone is watching. Leaving it
        ///     set for an unwatched chunk would mean the first player to arrive is sent every
        ///     section ever touched, and they are about to be sent the whole chunk anyway.
        /// </remarks>
        public void sendDirtyLight()
        {
            var sWorld = _chunkMap.getWorld();
            if (!sWorld.BlockHost.HasChunk(_chunkPos.X, _chunkPos.Z))
            {
                return;
            }

            var chunk = sWorld.BlockHost.GetChunk(_chunkPos.X, _chunkPos.Z);
            var sections = chunk.TakeLightDirtySections();

            if (sections == 0 || _players.Count == 0)
            {
                return;
            }

            sendMessageToPlayers(LightSectionsMessage.Of(chunk, sections));
        }

        private void sendBlockEntityUpdate(BlockEntity? blockentity)
        {
            if (blockentity != null)
            {
                if (blockentity.CreateUpdateMessage() is { } message)
                {
                    sendMessageToPlayers(message);
                }
            }
        }
    }
}
