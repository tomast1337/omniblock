using betareborn.Worlds;
using betareborn.Worlds.Storage;

namespace betareborn.Server.Worlds
{
    public class ReadOnlyServerWorld : ServerWorld
    {
        public ReadOnlyServerWorld(MinecraftServer server, WorldStorage storage, String saveName, int dimension, long seed, ServerWorld del) : base(server, storage, saveName, dimension, seed)
        {
            persistentStateManager = del.persistentStateManager;
        }
    }
}
