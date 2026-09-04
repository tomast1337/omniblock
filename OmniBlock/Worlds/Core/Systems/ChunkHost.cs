using OmniBlock.Worlds.Chunks;

namespace OmniBlock.Worlds.Core.Systems;

public sealed class ChunkHost(IChunkSource chunkSource)
{
    public IChunkSource ChunkSource => chunkSource;

    public bool HasChunk(int x, int z) => chunkSource.IsChunkLoaded(x, z);

    public Chunk GetChunkFromPos(int x, int z) => GetChunk(x >> 4, z >> 4);

    public Chunk GetChunk(int chunkX, int chunkZ) => chunkSource.GetChunk(chunkX, chunkZ);

    public bool IsPosLoaded(int x, int y, int z) => y is >= 0 && y < ChuckFormat.WorldHeight && HasChunk(x >> 4, z >> 4);

    public bool IsRegionLoaded(int x, int y, int z, int range) => IsRegionLoaded(x - range, y - range, z - range, x + range, y + range, z + range);

    public bool IsRegionLoaded(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        if (maxY >= 0 && minY < ChuckFormat.WorldHeight)
        {
            minX >>= 4;
            minZ >>= 4;
            maxX >>= 4;
            maxZ >>= 4;

            for (var x = minX; x <= maxX; ++x)
            {
                for (var z = minZ; z <= maxZ; ++z)
                {
                    if (!HasChunk(x, z))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        return false;
    }

    public byte[] GetChunkData(int x, int y, int z, int sizeX, int sizeY, int sizeZ)
    {
        var chunkData = new byte[sizeX * sizeY * sizeZ * 5 / 2];

        var startChunkX = x >> 4;
        var startChunkZ = z >> 4;
        var endChunkX = (x + sizeX - 1) >> 4;
        var endChunkZ = (z + sizeZ - 1) >> 4;

        var currentBufferOffset = 0;
        var minY = Math.Max(0, y);
        var maxY = Math.Min(ChuckFormat.WorldHeight, y + sizeY);

        for (var chunkX = startChunkX; chunkX <= endChunkX; chunkX++)
        {
            var localStartX = Math.Max(0, x - chunkX * 16);
            var localEndX = Math.Min(16, x + sizeX - chunkX * 16);

            for (var chunkZ = startChunkZ; chunkZ <= endChunkZ; chunkZ++)
            {
                var localStartZ = Math.Max(0, z - chunkZ * 16);
                var localEndZ = Math.Min(16, z + sizeZ - chunkZ * 16);

                currentBufferOffset = GetChunk(chunkX, chunkZ).ToPacket(
                    chunkData,
                    localStartX, minY, localStartZ,
                    localEndX, maxY, localEndZ,
                    currentBufferOffset);
            }
        }

        return chunkData;
    }
}
