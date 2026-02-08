using betareborn.Worlds;
using java.lang;
using java.util;

namespace betareborn.Chunks
{
    public class ChunkProviderClient : IChunkProvider
    {

        private Chunk blankChunk;
        private Map chunkMapping = new HashMap();
        private List field_889_c = new ArrayList();
        private World worldObj;

        public ChunkProviderClient(World var1)
        {
            blankChunk = new EmptyChunk(var1, new byte[-Short.MIN_VALUE], 0, 0);
            worldObj = var1;
        }

        public bool chunkExists(int var1, int var2)
        {
            if (this != null)
            {
                return true;
            }
            else
            {
                ChunkPos var3 = new ChunkPos(var1, var2);
                return chunkMapping.containsKey(var3);
            }
        }

        public void func_539_c(int var1, int var2)
        {
            Chunk var3 = provideChunk(var1, var2);
            if (!var3.isEmpty())
            {
                var3.unload();
            }

            chunkMapping.remove(new ChunkPos(var1, var2));
            field_889_c.remove(var3);
        }

        public Chunk prepareChunk(int var1, int var2)
        {
            ChunkPos var3 = new ChunkPos(var1, var2);
            byte[] var4 = new byte[-Short.MIN_VALUE];
            Chunk var5 = new Chunk(worldObj, var4, var1, var2);
            Arrays.fill(var5.skyLight.bytes, (byte)255);
            chunkMapping.put(var3, var5);
            var5.loaded = true;
            return var5;
        }

        public Chunk provideChunk(int var1, int var2)
        {
            ChunkPos var3 = new ChunkPos(var1, var2);
            Chunk var4 = (Chunk)chunkMapping.get(var3);
            return var4 == null ? blankChunk : var4;
        }

        public bool saveChunks(bool var1, IProgressUpdate var2)
        {
            return true;
        }

        public bool unload100OldestChunks()
        {
            return false;
        }

        public bool canSave()
        {
            return false;
        }

        public void populate(IChunkProvider var1, int var2, int var3)
        {
        }

        public void markChunksForUnload(int _)
        {
        }

        public string makeString()
        {
            return "MultiplayerChunkCache: " + chunkMapping.size();
        }
    }

}