using System.Collections.Concurrent;

namespace betareborn.Worlds
{
    public sealed class WorldChunkManagerCache
    {
        private readonly ConcurrentDictionary<int, WorldChunkManager> _perThread
            = new();

        public WorldChunkManager get(World world)
        {
            int tid = Environment.CurrentManagedThreadId;

            return _perThread.GetOrAdd(
                tid,
                _ => new WorldChunkManager(world)
            );
        }

        public void clear()
        {
            _perThread.Clear();
        }
    }
}