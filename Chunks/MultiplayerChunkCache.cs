using betareborn.Worlds;
using betareborn.Worlds.Chunks;
using java.lang;
using java.util;

namespace betareborn.Chunks
{
    public class MultiplayerChunkCache : ChunkSource
    {

        private readonly Chunk empty;
        private readonly Dictionary<ChunkPos, Chunk> chunkByPos = [];
        private readonly World world;

        public MultiplayerChunkCache(World var1)
        {
            empty = new EmptyChunk(var1, new byte[-Short.MIN_VALUE], 0, 0);
            world = var1;
        }

        public bool isChunkLoaded(int var1, int var2)
        {
            if (this != null)
            {
                return true;
            }
            else
            {
                ChunkPos var3 = new ChunkPos(var1, var2);
                return chunkByPos.ContainsKey(var3);
            }
        }

        public void unloadChunk(int x, int z)
        {
            Chunk var3 = getChunk(x, z);
            if (!var3.isEmpty())
            {
                var3.unload();
            }

            chunkByPos.Remove(new ChunkPos(x, z));
        }

        public Chunk loadChunk(int x, int z)
        {
            ChunkPos var3 = new ChunkPos(x, z);
            byte[] var4 = new byte[-Short.MIN_VALUE];
            Chunk var5 = new Chunk(world, var4, x, z);
            Arrays.fill(var5.skyLight.bytes, (byte)255);

            if (chunkByPos.ContainsKey(var3))
            {
                chunkByPos[var3] = var5;
            }
            else
            {
                chunkByPos.Add(var3, var5);
            }

            var5.loaded = true;
            return var5;
        }

        public Chunk getChunk(int x, int z)
        {
            ChunkPos var3 = new ChunkPos(x, z);
            chunkByPos.TryGetValue(var3, out Chunk? var4);
            return var4 == null ? empty : var4;
        }

        public bool save(bool bl, LoadingDisplay display)
        {
            return true;
        }

        public bool tick()
        {
            return false;
        }

        public bool canSave()
        {
            return false;
        }

        public void decorate(ChunkSource var1, int var2, int var3)
        {
        }

        public void markChunksForUnload(int _)
        {
        }

        public string getDebugInfo()
        {
            return "MultiplayerChunkCache: " + chunkByPos.Count;
        }
    }

}