using betareborn.Worlds;
using java.util;

namespace betareborn
{
    public interface ISaveHandler
    {
        WorldInfo loadWorldInfo();

        void func_22150_b();

        ChunkStorage getChunkLoader(WorldProvider var1);

        void saveWorldInfoAndPlayer(WorldInfo var1, List var2);

        void saveWorldInfo(WorldInfo var1);

        java.io.File func_28113_a(string var1);
    }

}