using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using OmniBlock.Blocks;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Lighting;

namespace OmniBlock.Worlds.Core.Systems;

public class LightingEngine : ILightProvider
{
    private readonly int _cobblestoneStairsId;
    private readonly int _farmlandId;

    private readonly List<LightUpdate> _lightingQueue = [];
    private readonly object _lightingQueueGate = new();
    private readonly ILogger<LightingEngine> _logger = Log.Instance.For<LightingEngine>();
    private readonly HashSet<PendingLightCell> _pendingLightCells = [];
    private readonly int _slabId;
    private readonly int _woodenStairsId;
    private readonly IWorldContext _world;
    private int _lightingUpdatesCounter;
    private int _lightingUpdatesScheduled;

    public LightingEngine(IWorldContext world)
    {
        _world = world;
        _slabId = ResolveOptionalBlockId("slab");
        _farmlandId = ResolveOptionalBlockId("farmland");
        _cobblestoneStairsId = ResolveOptionalBlockId("cobblestone_stairs");
        _woodenStairsId = ResolveOptionalBlockId("wooden_stairs");
    }

    internal int PendingUpdateCount
    {
        get
        {
            lock (_lightingQueueGate) return _lightingQueue.Count;
        }
    }

    public float GetNaturalBrightness(int x, int y, int z, int blockLight)
    {
        var lightLevel = GetLightLevel(x, y, z);
        if (lightLevel < blockLight)
        {
            lightLevel = blockLight;
        }

        return _world.Dimension.LightLevelToLuminance[lightLevel];
    }

    public float GetLuminance(int x, int y, int z) => _world.Dimension.LightLevelToLuminance[GetLightLevel(x, y, z)];

    public LightLevels GetLightLevels(int x, int y, int z, int minBlockLight) =>
        GetLightLevels(x, y, z, true).WithBlockFloor(minBlockLight);

    /// <inheritdoc cref="WorldRegionSnapshot.GetLightLevelsExt" />
    private LightLevels GetLightLevels(int x, int y, int z, bool checkNeighbors)
    {
        if (x < -32000000 || z < -32000000 || x >= 32000000 || z > 32000000)
        {
            return LightLevels.FullSky;
        }

        if (checkNeighbors)
        {
            var blockId = _world.Reader.GetBlockId(x, y, z);
            if (UsesNeighborLight(blockId))
            {
                return GetLightLevels(x, y + 1, z, false)
                    .Max(GetLightLevels(x + 1, y, z, false))
                    .Max(GetLightLevels(x - 1, y, z, false))
                    .Max(GetLightLevels(x, y, z + 1, false))
                    .Max(GetLightLevels(x, y, z - 1, false));
            }
        }

        if (y < 0)
        {
            return default;
        }

        if (y >= ChuckFormat.WorldHeight)
        {
            return _world.Dimension.HasCeiling ? default : LightLevels.FullSky;
        }

        var chunk = _world.ChunkHost.GetChunk(x >> 4, z >> 4);
        var packed = chunk.GetPackedLight(x & 15, y, z & 15);
        return LightLevels.Of((packed >> 4) & 0xF, packed & 0xF);
    }

    public event Action<int, int, int>? OnLightUpdated;

    public bool HasSkyLight(int x, int y, int z) => _world.ChunkHost.GetChunk(x >> 4, z >> 4).IsAboveMaxHeight(x & 15, y, z & 15);

    public int GetBrightness(int x, int y, int z)
    {
        if (y < 0)
        {
            return 0;
        }

        if (y >= ChuckFormat.WorldHeight)
        {
            return !_world.Dimension.HasCeiling ? 15 : 0;
        }

        return _world.ChunkHost.GetChunk(x >> 4, z >> 4).GetLight(x & 15, y, z & 15, 0);
    }

    public int GetLightLevel(int x, int y, int z) => GetLightLevel(x, y, z, true);

    public int GetLightLevel(int x, int y, int z, bool checkNeighbors)
    {
        if (x < -32000000 || z < -32000000 || x >= 32000000 || z > 32000000)
        {
            return 15;
        }

        if (checkNeighbors)
        {
            var blockId = _world.Reader.GetBlockId(x, y, z);
            if (UsesNeighborLight(blockId))
            {
                var neighborMaxLight = GetLightLevel(x, y + 1, z, false);
                var lightPosX = GetLightLevel(x + 1, y, z, false);
                var lightNegX = GetLightLevel(x - 1, y, z, false);
                var lightPosZ = GetLightLevel(x, y, z + 1, false);
                var lightNegZ = GetLightLevel(x, y, z - 1, false);

                if (lightPosX > neighborMaxLight)
                {
                    neighborMaxLight = lightPosX;
                }

                if (lightNegX > neighborMaxLight)
                {
                    neighborMaxLight = lightNegX;
                }

                if (lightPosZ > neighborMaxLight)
                {
                    neighborMaxLight = lightPosZ;
                }

                if (lightNegZ > neighborMaxLight)
                {
                    neighborMaxLight = lightNegZ;
                }

                return neighborMaxLight;
            }
        }

        if (y < 0)
        {
            return 0;
        }

        if (y >= ChuckFormat.WorldHeight)
        {
            return !_world.Dimension.HasCeiling ? 15 - _world.Environment.AmbientDarkness : 0;
        }

        var chunk = _world.ChunkHost.GetChunk(x >> 4, z >> 4);
        return chunk.GetLight(x & 15, y, z & 15, _world.Environment.AmbientDarkness);
    }

    public void UpdateLight(LightType lightType, int x, int y, int z, int targetLuminance)
    {
        if (_world.Dimension.HasCeiling && lightType == LightType.Sky)
        {
            return;
        }

        if (_world.Reader.IsPosLoaded(x, y, z))
        {
            if (lightType == LightType.Sky)
            {
                if (_world.Reader.IsTopY(x, y, z))
                {
                    targetLuminance = 15;
                }
            }
            else if (lightType == LightType.Block)
            {
                var blockId = _world.Reader.GetBlockId(x, y, z);
                if (_world.Content.Blocks.GetLightEmission(blockId) > targetLuminance)
                {
                    targetLuminance = _world.Content.Blocks.GetLightEmission(blockId);
                }
            }

            if (GetBrightness(lightType, x, y, z) != targetLuminance)
            {
                QueueLightUpdate(lightType, x, y, z, x, y, z);
            }
        }
    }

    public int GetBrightness(LightType type, int x, int y, int z)
    {
        if (y < 0)
        {
            y = 0;
        }

        if (y >= ChuckFormat.WorldHeight)
        {
            return type.lightValue;
        }

        if (x >= -32000000 && z >= -32000000 && x < 32000000 && z <= 32000000)
        {
            var chunkX = x >> 4;
            var chunkZ = z >> 4;
            if (!_world.ChunkHost.HasChunk(chunkX, chunkZ))
            {
                return 0;
            }

            var chunk = _world.ChunkHost.GetChunk(chunkX, chunkZ);
            return chunk.GetLight(type, x & 15, y, z & 15);
        }

        return type.lightValue;
    }

    public void SetLight(LightType lightType, int x, int y, int z, int value)
    {
        if (x >= -32000000 && z >= -32000000 && x < 32000000 && z <= 32000000)
        {
            if (y >= 0 && y < ChuckFormat.WorldHeight)
            {
                if (_world.ChunkHost.HasChunk(x >> 4, z >> 4))
                {
                    var chunk = _world.ChunkHost.GetChunk(x >> 4, z >> 4);
                    chunk.SetLight(lightType, x & 15, y, z & 15, value);
                    OnLightUpdated?.Invoke(x, y, z);
                }
            }
        }
    }

    public bool DoLightingUpdates()
    {
        lock (_lightingQueueGate)
        {
        if (_lightingUpdatesCounter >= 50)
        {
            return false;
        }

        ++_lightingUpdatesCounter;
        try
        {
            var updatesBudget = 500;

            while (_lightingQueue.Count > 0)
            {
                if (updatesBudget <= 0)
                {
                    return true;
                }

                updatesBudget--;

                var lastIndex = _lightingQueue.Count - 1;
                var updateTask = _lightingQueue[lastIndex];

                _lightingQueue.RemoveAt(lastIndex);
                if (updateTask.IsSingleCell)
                {
                    _pendingLightCells.Remove(new PendingLightCell(updateTask.LightType,
                        updateTask.MinX, updateTask.MinY, updateTask.MinZ));
                }

                updateTask.UpdateLight(_world.Reader, _world.ChunkHost, this, _world.Content.Blocks);
            }

            return false;
        }
        finally
        {
            --_lightingUpdatesCounter;
        }
        }
    }

    public void QueueLightUpdate(LightType type, int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
        => QueueLightUpdate(type, minX, minY, minZ, maxX, maxY, maxZ, true);

    public void QueueLightUpdate(LightType type, int minX, int minY, int minZ, int maxX, int maxY, int maxZ, bool attemptMerge)
    {
        lock (_lightingQueueGate)
        {
        // A remote world holds only the light the wire writes — the chunk blob on load, the
        // section snapshot on change. Propagating locally here would race that snapshot with a
        // locally derived answer that can diverge while a neighbour is still loading, and nothing
        // arbitrates the two once they disagree.
        if (_world.IsRemote)
        {
            return;
        }

        if (_world.Dimension.HasCeiling && type == LightType.Sky)
        {
            return;
        }

        ++_lightingUpdatesScheduled;
        try
        {
            if (_lightingUpdatesScheduled == 50)
            {
                return;
            }

            var centerX = (maxX + minX) / 2;
            var centerZ = (maxZ + minZ) / 2;

            if (_world.Reader.IsPosLoaded(centerX, 64, centerZ))
            {
                if (_world.ChunkHost.GetChunkFromPos(centerX, centerZ).IsEmpty())
                {
                    return;
                }

                var isSingleCell = minX == maxX && minY == maxY && minZ == maxZ;
                PendingLightCell pendingCell = new(type, minX, minY, minZ);
                if (isSingleCell && !_pendingLightCells.Add(pendingCell)) return;

                var queueSize = _lightingQueue.Count;
                var span = CollectionsMarshal.AsSpan(_lightingQueue);

                if (attemptMerge)
                {
                    var lookbackCount = Math.Min(5, queueSize);
                    for (var i = 0; i < lookbackCount; ++i)
                    {
                        ref var existingUpdate = ref span[queueSize - i - 1];
                        var existingWasSingleCell = existingUpdate.IsSingleCell;
                        PendingLightCell existingCell = new(existingUpdate.LightType,
                            existingUpdate.MinX, existingUpdate.MinY, existingUpdate.MinZ);
                        if (existingUpdate.LightType == type &&
                            existingUpdate.Expand(minX, minY, minZ, maxX, maxY, maxZ))
                        {
                            if (isSingleCell) _pendingLightCells.Remove(pendingCell);
                            if (existingWasSingleCell && !existingUpdate.IsSingleCell)
                                _pendingLightCells.Remove(existingCell);
                            return;
                        }
                    }
                }

                _lightingQueue.Add(new LightUpdate(type, minX, minY, minZ, maxX, maxY, maxZ));

                const int maxQueueCapacity = 1000000;
                if (_lightingQueue.Count > maxQueueCapacity)
                {
                    _logger.LogInformation($"More than {maxQueueCapacity} updates, aborting lighting updates");
                    _lightingQueue.Clear();
                    _pendingLightCells.Clear();
                }
            }
        }
        finally
        {
            --_lightingUpdatesScheduled;
        }
        }
    }

    private bool UsesNeighborLight(int blockId) =>
        blockId == _slabId || blockId == _farmlandId ||
        blockId == _cobblestoneStairsId || blockId == _woodenStairsId;

    private int ResolveOptionalBlockId(ResourceLocation key) =>
        _world.Content.Blocks.TryGet(key, out var block) ? block.Id : -1;

    private readonly record struct PendingLightCell(LightType Type, int X, int Y, int Z);
}
