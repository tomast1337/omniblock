using betareborn.Worlds;

namespace betareborn.Chunks
{
    public class ChunkProviderLoadOrGenerate : java.lang.Object, ChunkSource
    {
        private readonly Chunk blankChunk;
        private readonly ChunkSource chunkProvider;
        private readonly IChunkLoader chunkLoader;
        private readonly Chunk[] chunks;
        private readonly World worldObj;
        int lastQueriedChunkXPos;
        int lastQueriedChunkZPos;
        private Chunk lastQueriedChunk;
        private int curChunkX;
        private int curChunkY;

        public void setCurrentChunkOver(int var1, int var2)
        {
            curChunkX = var1;
            curChunkY = var2;
        }

        public bool canChunkExist(int var1, int var2)
        {
            byte var3 = 15;
            return var1 >= curChunkX - var3 && var2 >= curChunkY - var3 && var1 <= curChunkX + var3 && var2 <= curChunkY + var3;
        }

        public bool chunkExists(int var1, int var2)
        {
            if (!canChunkExist(var1, var2))
            {
                return false;
            }
            else if (var1 == lastQueriedChunkXPos && var2 == lastQueriedChunkZPos && lastQueriedChunk != null)
            {
                return true;
            }
            else
            {
                int var3 = var1 & 31;
                int var4 = var2 & 31;
                int var5 = var3 + var4 * 32;
                return chunks[var5] != null && (chunks[var5] == blankChunk || chunks[var5].chunkPosEquals(var1, var2));
            }
        }

        public Chunk prepareChunk(int var1, int var2)
        {
            return getChunk(var1, var2);
        }

        public Chunk getChunk(int var1, int var2)
        {
            if (var1 == lastQueriedChunkXPos && var2 == lastQueriedChunkZPos && lastQueriedChunk != null)
            {
                return lastQueriedChunk;
            }
            else if (!worldObj.findingSpawnPoint && !canChunkExist(var1, var2))
            {
                return blankChunk;
            }
            else
            {
                int var3 = var1 & 31;
                int var4 = var2 & 31;
                int var5 = var3 + var4 * 32;
                if (!chunkExists(var1, var2))
                {
                    if (chunks[var5] != null)
                    {
                        chunks[var5].unload();
                        saveChunk(chunks[var5]);
                        saveExtraChunkData(chunks[var5]);
                    }

                    Chunk var6 = func_542_c(var1, var2);
                    if (var6 == null)
                    {
                        if (chunkProvider == null)
                        {
                            var6 = blankChunk;
                        }
                        else
                        {
                            var6 = chunkProvider.getChunk(var1, var2);
                            var6.fill();
                        }
                    }

                    chunks[var5] = var6;
                    var6.populateBlockLight();
                    if (chunks[var5] != null)
                    {
                        chunks[var5].load();
                    }

                    if (!chunks[var5].terrainPopulated && chunkExists(var1 + 1, var2 + 1) && chunkExists(var1, var2 + 1) && chunkExists(var1 + 1, var2))
                    {
                        decorate(this, var1, var2);
                    }

                    if (chunkExists(var1 - 1, var2) && !getChunk(var1 - 1, var2).terrainPopulated && chunkExists(var1 - 1, var2 + 1) && chunkExists(var1, var2 + 1) && chunkExists(var1 - 1, var2))
                    {
                        decorate(this, var1 - 1, var2);
                    }

                    if (chunkExists(var1, var2 - 1) && !getChunk(var1, var2 - 1).terrainPopulated && chunkExists(var1 + 1, var2 - 1) && chunkExists(var1, var2 - 1) && chunkExists(var1 + 1, var2))
                    {
                        decorate(this, var1, var2 - 1);
                    }

                    if (chunkExists(var1 - 1, var2 - 1) && !getChunk(var1 - 1, var2 - 1).terrainPopulated && chunkExists(var1 - 1, var2 - 1) && chunkExists(var1, var2 - 1) && chunkExists(var1 - 1, var2))
                    {
                        decorate(this, var1 - 1, var2 - 1);
                    }
                }

                lastQueriedChunkXPos = var1;
                lastQueriedChunkZPos = var2;
                lastQueriedChunk = chunks[var5];
                return chunks[var5];
            }
        }

        private Chunk func_542_c(int var1, int var2)
        {
            if (chunkLoader == null)
            {
                return blankChunk;
            }
            else
            {
                try
                {
                    Chunk var3 = chunkLoader.loadChunk(worldObj, var1, var2);
                    if (var3 != null)
                    {
                        var3.lastSaveTime = worldObj.getTime();
                    }

                    return var3;
                }
                catch (java.lang.Exception var4)
                {
                    var4.printStackTrace();
                    return blankChunk;
                }
            }
        }

        private void saveExtraChunkData(Chunk var1)
        {
            if (chunkLoader != null)
            {
                try
                {
                    chunkLoader.saveEntities(worldObj, var1);
                }
                catch (java.lang.Exception var3)
                {
                    var3.printStackTrace();
                }

            }
        }

        private void saveChunk(Chunk var1)
        {
            if (chunkLoader != null)
            {
                try
                {
                    var1.lastSaveTime = worldObj.getTime();
                    chunkLoader.saveChunk(worldObj, var1, null, -1);
                }
                catch (java.io.IOException var3)
                {
                    var3.printStackTrace();
                }

            }
        }

        public void decorate(ChunkSource var1, int var2, int var3)
        {
            Chunk var4 = getChunk(var2, var3);
            if (!var4.terrainPopulated)
            {
                var4.terrainPopulated = true;
                if (chunkProvider != null)
                {
                    chunkProvider.decorate(var1, var2, var3);
                    var4.markDirty();
                }
            }

        }

        public bool save(bool var1, LoadingDisplay var2)
        {
            int var3 = 0;
            int var4 = 0;
            int var5;
            if (var2 != null)
            {
                for (var5 = 0; var5 < chunks.Length; ++var5)
                {
                    if (chunks[var5] != null && chunks[var5].shouldSave(var1))
                    {
                        ++var4;
                    }
                }
            }

            var5 = 0;

            for (int var6 = 0; var6 < chunks.Length; ++var6)
            {
                if (chunks[var6] != null)
                {
                    if (var1 && !chunks[var6].empty)
                    {
                        saveExtraChunkData(chunks[var6]);
                    }

                    if (chunks[var6].shouldSave(var1))
                    {
                        saveChunk(chunks[var6]);
                        chunks[var6].dirty = false;
                        ++var3;
                        if (var3 == 2 && !var1)
                        {
                            return false;
                        }

                        if (var2 != null)
                        {
                            ++var5;
                            if (var5 % 10 == 0)
                            {
                                var2.setLoadingProgress(var5 * 100 / var4);
                            }
                        }
                    }
                }
            }

            if (var1)
            {
                if (chunkLoader == null)
                {
                    return true;
                }

                chunkLoader.flush();
            }

            return true;
        }

        public bool tick()
        {
            if (chunkLoader != null)
            {
                chunkLoader.tick();
            }

            return chunkProvider.tick();
        }

        public bool canSave()
        {
            return true;
        }

        public void markChunksForUnload(int _)
        {
        }

        public string getDebugInfo()
        {
            return "ChunkCache: " + chunks.Length;
        }
    }
}