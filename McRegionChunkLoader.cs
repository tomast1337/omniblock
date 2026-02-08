using betareborn.Chunks;
using betareborn.NBT;
using betareborn.Worlds;

namespace betareborn
{
    public class McRegionChunkLoader : IChunkLoader
    {

        public readonly java.io.File worldDir;

        public McRegionChunkLoader(java.io.File var1)
        {
            worldDir = var1;
        }

        public Chunk loadChunk(World var1, int var2, int var3)
        {
            NBTTagCompound? var4 = Region.RegionCache.readChunkNBT(worldDir, var2, var3);
            if (var4 != null)
            {
                if (!var4.hasKey("Level"))
                {
                    java.lang.System.@out.println("Chunk file at " + var2 + "," + var3 + " is missing level data, skipping");
                    return null;
                }
                else if (!var4.getCompoundTag("Level").hasKey("Blocks"))
                {
                    java.lang.System.@out.println("Chunk file at " + var2 + "," + var3 + " is missing block data, skipping");
                    return null;
                }
                else
                {
                    Chunk var6 = ChunkLoader.loadChunkIntoWorldFromCompound(var1, var4.getCompoundTag("Level"));
                    if (!var6.chunkPosEquals(var2, var3))
                    {
                        java.lang.System.@out.println("Chunk file at " + var2 + "," + var3 + " is in the wrong location; relocating. (Expected " + var2 + ", " + var3 + ", got " + var6.x + ", " + var6.z + ")");
                        var4.setInteger("xPos", var2);
                        var4.setInteger("zPos", var3);
                        var6 = ChunkLoader.loadChunkIntoWorldFromCompound(var1, var4.getCompoundTag("Level"));
                    }

                    var6.fill();
                    return var6;
                }
            }
            else
            {
                return null;
            }
        }

        public void saveChunk(World var1, Chunk var2, Action onSave, long _)
        {
            NBTTagCompound var4 = new();
            NBTTagCompound var5 = new();
            var4.setTag("Level", var5);
            ChunkLoader.storeChunkInCompound(var2, var1, var5);
            Region.RegionCache.writeChunkNBT(worldDir, var2.x, var2.z, var4);
        }

        public void saveEntities(World var1, Chunk var2)
        {
        }

        public void tick()
        {
        }

        public void flush()
        {
        }

        public void flushToDisk()
        {
            Region.RegionCache.unloadAllRegions(worldDir);
            Region.RegionCache.resetLoadedCounters();
        }
    }
}