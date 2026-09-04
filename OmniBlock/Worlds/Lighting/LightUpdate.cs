using OmniBlock.Blocks;
using OmniBlock.Worlds.Chunks;
using OmniBlock.Worlds.Core.Systems;

namespace OmniBlock.Worlds.Lighting;

internal struct LightUpdate
{
    public readonly LightType LightType;

    public int MinX;
    public int MinY;
    public int MinZ;
    public int MaxX;
    public int MaxY;
    public int MaxZ;

    public readonly bool IsSingleCell =>
        MinX == MaxX && MinY == MaxY && MinZ == MaxZ;

    public LightUpdate(LightType lightType, int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        LightType = lightType;
        MinX = minX;
        MinY = minY;
        MinZ = minZ;
        MaxX = maxX;
        MaxY = maxY;
        MaxZ = maxZ;
    }

    public void UpdateLight(IBlockReader reader, ChunkHost host, LightingEngine lighting, IBlockRuntimeView blocks)
    {
        var sizeX = MaxX - MinX + 1;
        var sizeY = MaxY - MinY + 1;
        var sizeZ = MaxZ - MinZ + 1;
        var updateVolume = sizeX * sizeY * sizeZ;

        if (updateVolume > -short.MinValue)
        {
            // _logger.LogInformation("Light too large, skipping!");
            return;
        }

        var startY = MinY < 0 ? 0 : MinY;
        var endY = MaxY >= ChuckFormat.WorldHeight ? ChuckFormat.WorldHeight - 1 : MaxY;

        var cachedChunkX = 0;
        var cachedChunkZ = 0;
        var isCacheValid = false;
        var isCachedChunkLoaded = false;

        for (var x = MinX; x <= MaxX; ++x)
        {
            for (var z = MinZ; z <= MaxZ; ++z)
            {
                var chunkX = x >> 4;
                var chunkZ = z >> 4;
                bool isChunkLoaded;

                if (isCacheValid && chunkX == cachedChunkX && chunkZ == cachedChunkZ)
                {
                    isChunkLoaded = isCachedChunkLoaded;
                }
                else
                {
                    isChunkLoaded = host.IsRegionLoaded(x, 0, z, 1);
                    if (isChunkLoaded)
                    {
                        var chunk = host.GetChunk(chunkX, chunkZ);
                        if (chunk.IsEmpty())
                        {
                            isChunkLoaded = false;
                        }
                    }

                    isCachedChunkLoaded = isChunkLoaded;
                    cachedChunkX = chunkX;
                    cachedChunkZ = chunkZ;
                    isCacheValid = true;
                }

                if (isChunkLoaded)
                {
                    for (var y = startY; y <= endY; ++y)
                    {
                        var currentLight = lighting.GetBrightness(LightType, x, y, z);
                        var blockId = reader.GetBlockId(x, y, z);

                        var opacity = blocks.GetOpacity(blockId);
                        if (opacity == 0)
                        {
                            opacity = 1;
                        }

                        var emittedLight = 0;
                        if (LightType == LightType.Sky)
                        {
                            if (reader.IsTopY(x, y, z))
                            {
                                emittedLight = 15;
                            }
                        }
                        else if (LightType == LightType.Block)
                        {
                            emittedLight = blocks.GetLightEmission(blockId);
                        }

                        int targetLight;
                        if (opacity >= 15 && emittedLight == 0)
                        {
                            targetLight = 0;
                        }
                        else
                        {
                            var westLight = lighting.GetBrightness(LightType, x - 1, y, z);
                            var eastLight = lighting.GetBrightness(LightType, x + 1, y, z);
                            var downLight = lighting.GetBrightness(LightType, x, y - 1, z);
                            var upLight = lighting.GetBrightness(LightType, x, y + 1, z);
                            var northLight = lighting.GetBrightness(LightType, x, y, z - 1);
                            var southLight = lighting.GetBrightness(LightType, x, y, z + 1);

                            targetLight = westLight;
                            if (eastLight > targetLight)
                            {
                                targetLight = eastLight;
                            }

                            if (downLight > targetLight)
                            {
                                targetLight = downLight;
                            }

                            if (upLight > targetLight)
                            {
                                targetLight = upLight;
                            }

                            if (northLight > targetLight)
                            {
                                targetLight = northLight;
                            }

                            if (southLight > targetLight)
                            {
                                targetLight = southLight;
                            }

                            targetLight -= opacity;
                            if (targetLight < 0)
                            {
                                targetLight = 0;
                            }

                            if (emittedLight > targetLight)
                            {
                                targetLight = emittedLight;
                            }
                        }

                        if (currentLight != targetLight)
                        {
                            lighting.SetLight(LightType, x, y, z, targetLight);

                            var prop = targetLight - 1;
                            if (prop < 0)
                            {
                                prop = 0;
                            }

                            lighting.UpdateLight(LightType, x - 1, y, z, prop);
                            lighting.UpdateLight(LightType, x, y - 1, z, prop);
                            lighting.UpdateLight(LightType, x, y, z - 1, prop);

                            if (x + 1 >= MaxX)
                            {
                                lighting.UpdateLight(LightType, x + 1, y, z, prop);
                            }

                            if (y + 1 >= MaxY)
                            {
                                lighting.UpdateLight(LightType, x, y + 1, z, prop);
                            }

                            if (z + 1 >= MaxZ)
                            {
                                lighting.UpdateLight(LightType, x, y, z + 1, prop);
                            }
                        }
                    }
                }
            }
        }
    }

    public bool Expand(int reqMinX, int reqMinY, int reqMinZ, int reqMaxX, int reqMaxY, int reqMaxZ)
    {
        if (reqMinX >= MinX && reqMinY >= MinY && reqMinZ >= MinZ &&
            reqMaxX <= MaxX && reqMaxY <= MaxY && reqMaxZ <= MaxZ)
        {
            return true;
        }

        byte expandTolerance = 1;

        if (reqMinX >= MinX - expandTolerance && reqMinY >= MinY - expandTolerance && reqMinZ >= MinZ - expandTolerance &&
            reqMaxX <= MaxX + expandTolerance && reqMaxY <= MaxY + expandTolerance && reqMaxZ <= MaxZ + expandTolerance)
        {
            var oldVolumeX = MaxX - MinX;
            var oldVolumeY = MaxY - MinY;
            var oldVolumeZ = MaxZ - MinZ;

            var newMinX = reqMinX > MinX ? MinX : reqMinX;
            var newMinY = reqMinY > MinY ? MinY : reqMinY;
            var newMinZ = reqMinZ > MinZ ? MinZ : reqMinZ;
            var newMaxX = reqMaxX < MaxX ? MaxX : reqMaxX;
            var newMaxY = reqMaxY < MaxY ? MaxY : reqMaxY;
            var newMaxZ = reqMaxZ < MaxZ ? MaxZ : reqMaxZ;

            var newVolumeX = newMaxX - newMinX;
            var newVolumeY = newMaxY - newMinY;
            var newVolumeZ = newMaxZ - newMinZ;

            var oldVolume = oldVolumeX * oldVolumeY * oldVolumeZ;
            var newVolume = newVolumeX * newVolumeY * newVolumeZ;

            if (newVolume - oldVolume <= 2)
            {
                MinX = newMinX;
                MinY = newMinY;
                MinZ = newMinZ;
                MaxX = newMaxX;
                MaxY = newMaxY;
                MaxZ = newMaxZ;
                return true;
            }
        }

        return false;
    }
}
