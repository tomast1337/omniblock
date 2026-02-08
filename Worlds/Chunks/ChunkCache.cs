using betareborn.Chunks;
using betareborn.Entities;
using betareborn.Profiling;
using betareborn.Worlds;
using betareborn.Worlds.Chunks.Storage;
using java.lang;

namespace betareborn.Worlds.Chunks
{
    public class ChunkCache : java.lang.Object, ChunkSource
    {
        private readonly HashSet<int> chunksToUnload = [];
        private readonly Chunk empty;
        private readonly ChunkSource generator;
        private readonly RegionChunkStorage storage;
        private readonly Dictionary<int, Chunk> chunkByPos = [];
        private readonly List<Chunk> chunks = [];
        private readonly World world;
        private int lastRenderDistance = 0;

        public ChunkCache(World world, RegionChunkStorage storage, ChunkSource generator)
        {
            empty = new EmptyChunk(world, new byte[-Short.MIN_VALUE], 0, 0);
            this.world = world;
            this.storage = storage;
            this.generator = generator;
        }

        public bool isChunkLoaded(int x, int z)
        {
            return chunkByPos.ContainsKey(ChunkPos.chunkXZ2Int(x, z));
        }

        public Chunk loadChunk(int chunkX, int chunkZ)
        {
            int var3 = ChunkPos.chunkXZ2Int(chunkX, chunkZ);
            chunksToUnload.Remove(var3);
            chunkByPos.TryGetValue(var3, out Chunk? var4);
            if (var4 == null)
            {
                var4 = loadChunkFromStorage(chunkX, chunkZ);
                if (var4 == null)
                {
                    if (generator == null)
                    {
                        var4 = empty;
                    }
                    else
                    {
                        var4 = generator.getChunk(chunkX, chunkZ);
                    }
                }

                chunkByPos[var3] = var4;
                chunks.Add(var4);
                if (var4 != null)
                {
                    var4.populateBlockLight();
                    var4.load();
                }

                if (!var4.terrainPopulated && isChunkLoaded(chunkX + 1, chunkZ + 1) && isChunkLoaded(chunkX, chunkZ + 1) && isChunkLoaded(chunkX + 1, chunkZ))
                {
                    decorate(this, chunkX, chunkZ);
                }

                if (isChunkLoaded(chunkX - 1, chunkZ) && !getChunk(chunkX - 1, chunkZ).terrainPopulated && isChunkLoaded(chunkX - 1, chunkZ + 1) && isChunkLoaded(chunkX, chunkZ + 1) && isChunkLoaded(chunkX - 1, chunkZ))
                {
                    decorate(this, chunkX - 1, chunkZ);
                }

                if (isChunkLoaded(chunkX, chunkZ - 1) && !getChunk(chunkX, chunkZ - 1).terrainPopulated && isChunkLoaded(chunkX + 1, chunkZ - 1) && isChunkLoaded(chunkX, chunkZ - 1) && isChunkLoaded(chunkX + 1, chunkZ))
                {
                    decorate(this, chunkX, chunkZ - 1);
                }

                if (isChunkLoaded(chunkX - 1, chunkZ - 1) && !getChunk(chunkX - 1, chunkZ - 1).terrainPopulated && isChunkLoaded(chunkX - 1, chunkZ - 1) && isChunkLoaded(chunkX, chunkZ - 1) && isChunkLoaded(chunkX - 1, chunkZ))
                {
                    decorate(this, chunkX - 1, chunkZ - 1);
                }
            }

            return var4;
        }

        public Chunk getChunk(int chunkX, int chunkZ)
        {
            chunkByPos.TryGetValue(ChunkPos.chunkXZ2Int(chunkX, chunkZ), out Chunk? var3);
            return var3 == null ? loadChunk(chunkX, chunkZ) : var3;
        }

        private Chunk loadChunkFromStorage(int chunkX, int chunkZ)
        {
            if (storage == null)
            {
                return null;
            }
            else
            {
                try
                {
                    Chunk var3 = storage.loadChunk(world, chunkX, chunkZ);
                    if (var3 != null)
                    {
                        var3.lastSaveTime = world.getTime();
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

        private void saveEntities(Chunk chunk)
        {
            if (storage != null)
            {
                try
                {
                    storage.saveEntities(world, chunk);
                }
                catch (java.lang.Exception var3)
                {
                    var3.printStackTrace();
                }

            }
        }

        private void saveChunk(Chunk chunk)
        {
            if (storage != null)
            {
                try
                {
                    chunk.lastSaveTime = world.getTime();
                    storage.saveChunk(world, chunk, null, -1);
                }
                catch (java.io.IOException var3)
                {
                    var3.printStackTrace();
                }

            }
        }

        public void decorate(ChunkSource source, int x, int z)
        {
            Chunk var4 = getChunk(x, z);
            if (!var4.terrainPopulated)
            {
                var4.terrainPopulated = true;
                if (generator != null)
                {
                    generator.decorate(source, x, z);
                    var4.markDirty();
                }
            }

        }

        public bool save(bool saveEntities, LoadingDisplay display)
        {
            Profiler.PushGroup("saveChunks");
            Profiler.Start("collectDirty");

            int numSaved = 0;
            int totalChecked = 0;
            int totalNeedsSaving = 0;
            const int MAX_CHUNKS_PER_SAVE = 24;

            for (int var4 = 0; var4 < chunks.Count; ++var4)
            {
                totalChecked++;
                Chunk chunk = chunks[var4];

                if (saveEntities && !chunk.empty)
                {
                    this.saveEntities(chunk);
                }

                if (chunk.shouldSave(saveEntities))
                {
                    totalNeedsSaving++;
                    Profiler.Stop("collectDirty");
                    Profiler.Start("saveChunk");

                    saveChunk(chunk);
                    chunk.dirty = false;
                    ++numSaved;

                    Profiler.Stop("saveChunk");
                    Profiler.Start("collectDirty");

                    if (numSaved == MAX_CHUNKS_PER_SAVE && !saveEntities)
                    {
                        Profiler.Stop("collectDirty");
                        Profiler.PopGroup();

                        Region.RegionCache.autosaveChunks(storage.worldDir, MAX_CHUNKS_PER_SAVE);

                        return false;
                    }
                }
            }

            Profiler.Stop("collectDirty");

            if (saveEntities)
            {
                if (storage == null)
                {
                    Profiler.PopGroup();
                    return true;
                }

                storage.flush();
                storage.flushToDisk();
            }

            Region.RegionCache.autosaveChunks(storage.worldDir, MAX_CHUNKS_PER_SAVE);

            Profiler.PopGroup();
            return true;
        }

        public bool tick()
        {
            for (int var1 = 0; var1 < 100; ++var1)
            {
                if (chunksToUnload.Count != 0)
                {
                    int var2 = chunksToUnload.First();
                    Chunk var3 = chunkByPos[var2];
                    var3.unload();
                    saveChunk(var3);
                    saveEntities(var3);
                    chunksToUnload.Remove(var2);
                    chunkByPos.Remove(var2);
                    chunks.Remove(var3);
                }
            }

            storage?.tick();

            return generator.tick();
        }

        public void markChunksForUnload(int renderDistanceChunks)
        {
            foreach (Chunk chunk in chunks)
            {
                var players = world.playerEntities;
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
                    chunksToUnload.Add(chunkKey);
                }
            }

            if (renderDistanceChunks != lastRenderDistance)
            {
                //Might want to do a dynamic calculation at some point
                Region.RegionCache.setMaxLoadedRegions(storage.worldDir, 32);
            }

            for (int i = 0; i < world.playerEntities.size(); i++)
            {
                var player = (EntityPlayer)world.playerEntities.get(i);
                Region.RegionCache.loadNearbyRegions(storage.worldDir, (int)player.posX, (int)player.posZ, renderDistanceChunks);
            }
        }

        public bool canSave()
        {
            return true;
        }

        public string getDebugInfo()
        {
            return "ServerChunkCache: " + chunkByPos.Count + " Drop: " + chunksToUnload.Count;
        }
    }
}