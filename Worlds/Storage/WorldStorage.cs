using betareborn.Worlds;
using betareborn.Worlds.Chunks.Storage;
using betareborn.Worlds.Dimensions;
using java.util;

namespace betareborn.Worlds.Storage
{
    public interface WorldStorage
    {
        WorldProperties loadProperties();

        void checkSessionLock();

        ChunkStorage getChunkStorage(Dimension dim);

        void save(WorldProperties var1, List var2);

        void save(WorldProperties var1);
        void forceSave();
        //PlayerSaveHandler getPlayerSaveHandler();

        java.io.File getWorldPropertiesFile(string name);
    }

}