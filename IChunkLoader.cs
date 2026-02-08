using betareborn.Chunks;
using betareborn.Worlds;

namespace betareborn
{
    public interface IChunkLoader
    {
        Chunk loadChunk(World world, int chunkX, int chunkZ);

        void saveChunk(World world, Chunk chunk, Action onSave, long sequence);

        void saveEntities(World world, Chunk chunk);

        void tick();

        void flush();

        void flushToDisk();
    }

}