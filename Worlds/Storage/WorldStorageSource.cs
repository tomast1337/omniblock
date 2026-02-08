using betareborn.Worlds;
using java.util;

namespace betareborn.Worlds.Storage
{
    public interface WorldStorageSource
    {
        string getName();

        WorldStorage get(string saveName, bool createPlayerDataDir);

        List getAll();

        void flush();

        WorldProperties getProperties(string var1);

        void delete(string var1);

        void rename(string var1, string var2);

        bool needsConversion(string var1);

        bool convert(string var1, LoadingDisplay var2);
    }

}