using betareborn.Entities;
using betareborn.Profiling;
using betareborn.Worlds;
using java.lang;

namespace betareborn.Chunks
{
    public class ChunkProvider : java.lang.Object, IChunkProvider
    {
        private readonly HashSet<int> droppedChunksSet = [];
        private readonly Chunk emptyChunk;
        private readonly IChunkProvider chunkProvider;
        private readonly McRegionChunkLoader chunkLoader;
        private readonly Dictionary<int, Chunk> chunkMap = [];
        private readonly List<Chunk> chunkList = [];
        private readonly World worldObj;
        private int lastRenderDistance = 0;

        public ChunkProvider(World var1, McRegionChunkLoader var2, IChunkProvider var3)
        {
            emptyChunk = new EmptyChunk(var1, new byte[-Short.MIN_VALUE], 0, 0);
            worldObj = var1;
            chunkLoader = var2;
            chunkProvider = var3;
        }

        public bool chunkExists(int var1, int var2)
        {
            return chunkMap.ContainsKey(ChunkPos.chunkXZ2Int(var1, var2));
        }

        public Chunk prepareChunk(int var1, int var2)
        {
            int var3 = ChunkPos.chunkXZ2Int(var1, var2);
            droppedChunksSet.Remove(var3);
            chunkMap.TryGetValue(var3, out Chunk? var4);
            if (var4 == null)
            {
                var4 = loadChunkFromFile(var1, var2);
                if (var4 == null)
                {
                    if (chunkProvider == null)
                    {
                        var4 = emptyChunk;
                    }
                    else
                    {
                        var4 = chunkProvider.provideChunk(var1, var2);
                    }
                }

                chunkMap[var3] = var4;
                chunkList.Add(var4);
                if (var4 != null)
                {
                    var4.populateBlockLight();
                    var4.load();
                }

                if (!var4.terrainPopulated && chunkExists(var1 + 1, var2 + 1) && chunkExists(var1, var2 + 1) && chunkExists(var1 + 1, var2))
                {
                    populate(this, var1, var2);
                }

                if (chunkExists(var1 - 1, var2) && !provideChunk(var1 - 1, var2).terrainPopulated && chunkExists(var1 - 1, var2 + 1) && chunkExists(var1, var2 + 1) && chunkExists(var1 - 1, var2))
                {
                    populate(this, var1 - 1, var2);
                }

                if (chunkExists(var1, var2 - 1) && !provideChunk(var1, var2 - 1).terrainPopulated && chunkExists(var1 + 1, var2 - 1) && chunkExists(var1, var2 - 1) && chunkExists(var1 + 1, var2))
                {
                    populate(this, var1, var2 - 1);
                }

                if (chunkExists(var1 - 1, var2 - 1) && !provideChunk(var1 - 1, var2 - 1).terrainPopulated && chunkExists(var1 - 1, var2 - 1) && chunkExists(var1, var2 - 1) && chunkExists(var1 - 1, var2))
                {
                    populate(this, var1 - 1, var2 - 1);
                }
            }

            return var4;
        }

        public Chunk provideChunk(int var1, int var2)
        {
            chunkMap.TryGetValue(ChunkPos.chunkXZ2Int(var1, var2), out Chunk? var3);
            return var3 == null ? prepareChunk(var1, var2) : var3;
        }

        private Chunk loadChunkFromFile(int var1, int var2)
        {
            if (chunkLoader == null)
            {
                return null;
            }
            else
            {
                try
                {
                    Chunk var3 = chunkLoader.loadChunk(worldObj, var1, var2);
                    if (var3 != null)
                    {
                        var3.lastSaveTime = worldObj.getWorldTime();
                    }

                    return var3;
                }
                catch (java.lang.Exception var4)
                {
                    var4.printStackTrace();
                    return null;
                }
            }
        }

        private void saveSingleExtraChunkData(Chunk var1)
        {
            if (chunkLoader != null)
            {
                try
                {
                    chunkLoader.saveExtraChunkData(worldObj, var1);
                }
                catch (java.lang.Exception var3)
                {
                    var3.printStackTrace();
                }

            }
        }

        private void saveSingleChunk(Chunk var1)
        {
            if (chunkLoader != null)
            {
                try
                {
                    var1.lastSaveTime = worldObj.getWorldTime();
                    chunkLoader.saveChunk(worldObj, var1, null, -1);
                }
                catch (java.io.IOException var3)
                {
                    var3.printStackTrace();
                }

            }
        }

        public void populate(IChunkProvider var1, int var2, int var3)
        {
            Chunk var4 = provideChunk(var2, var3);
            if (!var4.terrainPopulated)
            {
                var4.terrainPopulated = true;
                if (chunkProvider != null)
                {
                    chunkProvider.populate(var1, var2, var3);
                    var4.markDirty();
                }
            }

        }

        public bool saveChunks(bool force, IProgressUpdate var2)
        {
            Profiler.PushGroup("saveChunks");
            Profiler.Start("collectDirty");

            int numSaved = 0;
            int totalChecked = 0;
            int totalNeedsSaving = 0;
            const int MAX_CHUNKS_PER_SAVE = 24;

            for (int var4 = 0; var4 < chunkList.Count; ++var4)
            {
                totalChecked++;
                Chunk chunk = chunkList[var4];

                if (force && !chunk.empty)
                {
                    saveSingleExtraChunkData(chunk);
                }

                if (chunk.shouldSave(force))
                {
                    totalNeedsSaving++;
                    Profiler.Stop("collectDirty");
                    Profiler.Start("saveChunk");

                    saveSingleChunk(chunk);
                    ++numSaved;

                    Profiler.Stop("saveChunk");
                    Profiler.Start("collectDirty");

                    if (numSaved == MAX_CHUNKS_PER_SAVE && !force)
                    {
                        Profiler.Stop("collectDirty");
                        Profiler.PopGroup();

                        Region.RegionCache.autosaveChunks(chunkLoader.worldDir, MAX_CHUNKS_PER_SAVE);

                        return false;
                    }
                }
            }

            Profiler.Stop("collectDirty");

            if (force)
            {
                if (chunkLoader == null)
                {
                    Profiler.PopGroup();
                    return true;
                }

                chunkLoader.saveExtraData();
                chunkLoader.flushToDisk();
            }

            Region.RegionCache.autosaveChunks(chunkLoader.worldDir, MAX_CHUNKS_PER_SAVE);

            Profiler.PopGroup();
            return true;
        }

        public bool unload100OldestChunks()
        {
            for (int var1 = 0; var1 < 100; ++var1)
            {
                if (droppedChunksSet.Count != 0)
                {
                    int var2 = droppedChunksSet.First();
                    Chunk var3 = chunkMap[var2];
                    var3.unload();
                    saveSingleChunk(var3);
                    saveSingleExtraChunkData(var3);
                    droppedChunksSet.Remove(var2);
                    chunkMap.Remove(var2);
                    chunkList.Remove(var3);
                }
            }

            chunkLoader?.func_814_a();

            return chunkProvider.unload100OldestChunks();
        }

        public void markChunksForUnload(int renderDistanceChunks)
        {
            foreach (Chunk chunk in chunkList)
            {
                var players = worldObj.playerEntities;
                bool nearAnyPlayer = false;

                int chunkCenterX = chunk.x * 16 + 8;
                int chunkCenterZ = chunk.z * 16 + 8;

                const int chunkBuffer = 4;
                int unloadDistance = (renderDistanceChunks + chunkBuffer) * 16;

                for (int i = 0; i < players.size(); i++)
                {
                    EntityPlayer player = (EntityPlayer)players.get(i);
                    int dx = (int)player.posX - chunkCenterX;
                    int dz = (int)player.posZ - chunkCenterZ;

                    if (dx * dx + dz * dz < unloadDistance * unloadDistance)
                    {
                        nearAnyPlayer = true;
                        break;
                    }
                }

                if (!nearAnyPlayer)
                {
                    int chunkKey = ChunkPos.chunkXZ2Int(chunk.x, chunk.z);
                    droppedChunksSet.Add(chunkKey);
                }
            }

            if (renderDistanceChunks != lastRenderDistance)
            {
                //Might want to do a dynamic calculation at some point
                Region.RegionCache.setMaxLoadedRegions(chunkLoader.worldDir, 32);
            }

            for (int i = 0; i < worldObj.playerEntities.size(); i++)
            {
                var player = (EntityPlayer)worldObj.playerEntities.get(i);
                Region.RegionCache.loadNearbyRegions(chunkLoader.worldDir, (int)player.posX, (int)player.posZ, renderDistanceChunks);
            }
        }

        public bool canSave()
        {
            return true;
        }

        public string makeString()
        {
            return "ServerChunkCache: " + chunkMap.Count + " Drop: " + droppedChunksSet.Count;
        }
    }
}