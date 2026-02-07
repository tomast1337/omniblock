using betareborn.Blocks;
using betareborn.Entities;
using betareborn.TileEntities;
using betareborn.Worlds;

namespace betareborn.Chunks
{
    public class Chunk : java.lang.Object
    {
        public static bool isLit;
        public byte[] blocks;
        public bool isChunkLoaded;
        public World worldObj;
        public NibbleArray data;
        public NibbleArray skylightMap;
        public NibbleArray blocklightMap;
        public byte[] heightMap;
        public int lowestBlockHeight;
        public readonly int xPosition;
        public readonly int zPosition;
        public Dictionary<ChunkPosition, TileEntity> chunkTileEntityMap;
        public List<Entity>[] entities;
        public bool isTerrainPopulated;
        public bool isModified;
        public bool neverSave;
        public bool hasEntities;
        public long lastSaveTime;

        public Chunk(World var1, int var2, int var3)
        {
            chunkTileEntityMap = [];
            entities = new List<Entity>[8];
            isTerrainPopulated = false;
            isModified = false;
            hasEntities = false;
            lastSaveTime = 0L;
            worldObj = var1;
            xPosition = var2;
            zPosition = var3;
            heightMap = new byte[256];

            for (int var4 = 0; var4 < entities.Length; ++var4)
            {
                entities[var4] = [];
            }

        }

        public Chunk(World var1, byte[] var2, int var3, int var4) : this(var1, var3, var4)
        {
            blocks = var2;
            data = new NibbleArray(var2.Length);
            skylightMap = new NibbleArray(var2.Length);
            blocklightMap = new NibbleArray(var2.Length);
        }

        public virtual bool isAtLocation(int var1, int var2)
        {
            return var1 == xPosition && var2 == zPosition;
        }

        public virtual int getHeightValue(int var1, int var2)
        {
            return heightMap[var2 << 4 | var1] & 255;
        }

        public virtual void func_1014_a()
        {
        }

        public virtual void generateHeightMap()
        {
            int var1 = 127;

            for (int var2 = 0; var2 < 16; ++var2)
            {
                for (int var3 = 0; var3 < 16; ++var3)
                {
                    int var4 = 127;

                    for (int var5 = var2 << 11 | var3 << 7; var4 > 0 && Block.lightOpacity[blocks[var5 + var4 - 1] & 255] == 0; --var4)
                    {
                    }

                    heightMap[var3 << 4 | var2] = (byte)var4;
                    if (var4 < var1)
                    {
                        var1 = var4;
                    }
                }
            }

            lowestBlockHeight = var1;
            isModified = true;
        }

        public virtual void func_1024_c()
        {
            int var1 = 127;

            int var2;
            int var3;
            for (var2 = 0; var2 < 16; ++var2)
            {
                for (var3 = 0; var3 < 16; ++var3)
                {
                    int var4 = 127;

                    int var5;
                    for (var5 = var2 << 11 | var3 << 7; var4 > 0 && Block.lightOpacity[blocks[var5 + var4 - 1] & 255] == 0; --var4)
                    {
                    }

                    heightMap[var3 << 4 | var2] = (byte)var4;
                    if (var4 < var1)
                    {
                        var1 = var4;
                    }

                    if (!worldObj.worldProvider.hasNoSky)
                    {
                        int var6 = 15;
                        int var7 = 127;

                        do
                        {
                            var6 -= Block.lightOpacity[blocks[var5 + var7] & 255];
                            if (var6 > 0)
                            {
                                skylightMap.setNibble(var2, var7, var3, var6);
                            }

                            --var7;
                        } while (var7 > 0 && var6 > 0);
                    }
                }
            }

            lowestBlockHeight = var1;

            for (var2 = 0; var2 < 16; ++var2)
            {
                for (var3 = 0; var3 < 16; ++var3)
                {
                    func_996_c(var2, var3);
                }
            }

            isModified = true;
        }

        public virtual void func_4143_d()
        {
        }

        private void func_996_c(int var1, int var2)
        {
            int var3 = getHeightValue(var1, var2);
            int var4 = xPosition * 16 + var1;
            int var5 = zPosition * 16 + var2;
            func_1020_f(var4 - 1, var5, var3);
            func_1020_f(var4 + 1, var5, var3);
            func_1020_f(var4, var5 - 1, var3);
            func_1020_f(var4, var5 + 1, var3);
        }

        private void func_1020_f(int var1, int var2, int var3)
        {
            int var4 = worldObj.getHeightValue(var1, var2);
            if (var4 > var3)
            {
                worldObj.scheduleLightingUpdate(EnumSkyBlock.Sky, var1, var3, var2, var1, var4, var2);
                isModified = true;
            }
            else if (var4 < var3)
            {
                worldObj.scheduleLightingUpdate(EnumSkyBlock.Sky, var1, var4, var2, var1, var3, var2);
                isModified = true;
            }

        }

        private void func_1003_g(int var1, int var2, int var3)
        {
            int var4 = heightMap[var3 << 4 | var1] & 255;
            int var5 = var4;
            if (var2 > var4)
            {
                var5 = var2;
            }

            for (int var6 = var1 << 11 | var3 << 7; var5 > 0 && Block.lightOpacity[blocks[var6 + var5 - 1] & 255] == 0; --var5)
            {
            }

            if (var5 != var4)
            {
                worldObj.markBlocksDirtyVertical(var1, var3, var5, var4);
                heightMap[var3 << 4 | var1] = (byte)var5;
                int var7;
                int var8;
                int var9;
                if (var5 < lowestBlockHeight)
                {
                    lowestBlockHeight = var5;
                }
                else
                {
                    var7 = 127;

                    for (var8 = 0; var8 < 16; ++var8)
                    {
                        for (var9 = 0; var9 < 16; ++var9)
                        {
                            if ((heightMap[var9 << 4 | var8] & 255) < var7)
                            {
                                var7 = heightMap[var9 << 4 | var8] & 255;
                            }
                        }
                    }

                    lowestBlockHeight = var7;
                }

                var7 = xPosition * 16 + var1;
                var8 = zPosition * 16 + var3;
                if (var5 < var4)
                {
                    for (var9 = var5; var9 < var4; ++var9)
                    {
                        skylightMap.setNibble(var1, var9, var3, 15);
                    }
                }
                else
                {
                    worldObj.scheduleLightingUpdate(EnumSkyBlock.Sky, var7, var4, var8, var7, var5, var8);

                    for (var9 = var4; var9 < var5; ++var9)
                    {
                        skylightMap.setNibble(var1, var9, var3, 0);
                    }
                }

                var9 = 15;

                int var10;
                for (var10 = var5; var5 > 0 && var9 > 0; skylightMap.setNibble(var1, var5, var3, var9))
                {
                    --var5;
                    int var11 = Block.lightOpacity[getBlockID(var1, var5, var3)];
                    if (var11 == 0)
                    {
                        var11 = 1;
                    }

                    var9 -= var11;
                    if (var9 < 0)
                    {
                        var9 = 0;
                    }
                }

                while (var5 > 0 && Block.lightOpacity[getBlockID(var1, var5 - 1, var3)] == 0)
                {
                    --var5;
                }

                if (var5 != var10)
                {
                    worldObj.scheduleLightingUpdate(EnumSkyBlock.Sky, var7 - 1, var5, var8 - 1, var7 + 1, var10, var8 + 1);
                }

                isModified = true;
            }
        }

        public virtual int getBlockID(int var1, int var2, int var3)
        {
            return blocks[var1 << 11 | var3 << 7 | var2] & 255;
        }

        public virtual bool setBlockIDWithMetadata(int var1, int var2, int var3, int var4, int var5)
        {
            byte var6 = (byte)var4;
            int var7 = heightMap[var3 << 4 | var1] & 255;
            int var8 = blocks[var1 << 11 | var3 << 7 | var2] & 255;
            if (var8 == var4 && data.getNibble(var1, var2, var3) == var5)
            {
                return false;
            }
            else
            {
                int var9 = xPosition * 16 + var1;
                int var10 = zPosition * 16 + var3;
                blocks[var1 << 11 | var3 << 7 | var2] = (byte)(var6 & 255);
                if (var8 != 0 && !worldObj.multiplayerWorld)
                {
                    Block.blocksList[var8].onBlockRemoval(worldObj, var9, var2, var10);
                }

                data.setNibble(var1, var2, var3, var5);
                if (!worldObj.worldProvider.hasNoSky)
                {
                    if (Block.lightOpacity[var6 & 255] != 0)
                    {
                        if (var2 >= var7)
                        {
                            func_1003_g(var1, var2 + 1, var3);
                        }
                    }
                    else if (var2 == var7 - 1)
                    {
                        func_1003_g(var1, var2, var3);
                    }

                    worldObj.scheduleLightingUpdate(EnumSkyBlock.Sky, var9, var2, var10, var9, var2, var10);
                }

                worldObj.scheduleLightingUpdate(EnumSkyBlock.Block, var9, var2, var10, var9, var2, var10);
                func_996_c(var1, var3);
                data.setNibble(var1, var2, var3, var5);
                if (var4 != 0)
                {
                    Block.blocksList[var4].onBlockAdded(worldObj, var9, var2, var10);
                }

                isModified = true;
                return true;
            }
        }

        public virtual bool setBlockID(int var1, int var2, int var3, int var4)
        {
            byte var5 = (byte)var4;
            int var6 = heightMap[var3 << 4 | var1] & 255;
            int var7 = blocks[var1 << 11 | var3 << 7 | var2] & 255;
            if (var7 == var4)
            {
                return false;
            }
            else
            {
                int var8 = xPosition * 16 + var1;
                int var9 = zPosition * 16 + var3;
                blocks[var1 << 11 | var3 << 7 | var2] = (byte)(var5 & 255);
                if (var7 != 0)
                {
                    Block.blocksList[var7].onBlockRemoval(worldObj, var8, var2, var9);
                }

                data.setNibble(var1, var2, var3, 0);
                if (Block.lightOpacity[var5 & 255] != 0)
                {
                    if (var2 >= var6)
                    {
                        func_1003_g(var1, var2 + 1, var3);
                    }
                }
                else if (var2 == var6 - 1)
                {
                    func_1003_g(var1, var2, var3);
                }

                worldObj.scheduleLightingUpdate(EnumSkyBlock.Sky, var8, var2, var9, var8, var2, var9);
                worldObj.scheduleLightingUpdate(EnumSkyBlock.Block, var8, var2, var9, var8, var2, var9);
                func_996_c(var1, var3);
                if (var4 != 0 && !worldObj.multiplayerWorld)
                {
                    Block.blocksList[var4].onBlockAdded(worldObj, var8, var2, var9);
                }

                isModified = true;
                return true;
            }
        }

        public virtual int getBlockMetadata(int var1, int var2, int var3)
        {
            return data.getNibble(var1, var2, var3);
        }

        public virtual void setBlockMetadata(int var1, int var2, int var3, int var4)
        {
            isModified = true;
            data.setNibble(var1, var2, var3, var4);
        }

        public virtual int getSavedLightValue(EnumSkyBlock var1, int var2, int var3, int var4)
        {
            return var1 == EnumSkyBlock.Sky ? skylightMap.getNibble(var2, var3, var4) : (var1 == EnumSkyBlock.Block ? blocklightMap.getNibble(var2, var3, var4) : 0);
        }

        public virtual void setLightValue(EnumSkyBlock var1, int var2, int var3, int var4, int var5)
        {
            isModified = true;
            if (var1 == EnumSkyBlock.Sky)
            {
                skylightMap.setNibble(var2, var3, var4, var5);
            }
            else
            {
                if (var1 != EnumSkyBlock.Block)
                {
                    return;
                }

                blocklightMap.setNibble(var2, var3, var4, var5);
            }

        }

        public virtual int getBlockLightValue(int var1, int var2, int var3, int var4)
        {
            int var5 = skylightMap.getNibble(var1, var2, var3);
            if (var5 > 0)
            {
                isLit = true;
            }

            var5 -= var4;
            int var6 = blocklightMap.getNibble(var1, var2, var3);
            if (var6 > var5)
            {
                var5 = var6;
            }

            return var5;
        }

        public virtual void addEntity(Entity var1)
        {
            hasEntities = true;
            int var2 = MathHelper.floor_double(var1.posX / 16.0D);
            int var3 = MathHelper.floor_double(var1.posZ / 16.0D);
            if (var2 != xPosition || var3 != zPosition)
            {
                java.lang.System.@out.println("Wrong location! " + var1);
                java.lang.Thread.dumpStack();
            }

            int var4 = MathHelper.floor_double(var1.posY / 16.0D);
            if (var4 < 0)
            {
                var4 = 0;
            }

            if (var4 >= entities.Length)
            {
                var4 = entities.Length - 1;
            }

            var1.addedToChunk = true;
            var1.chunkCoordX = xPosition;
            var1.chunkCoordY = var4;
            var1.chunkCoordZ = zPosition;
            entities[var4].Add(var1);
        }

        public virtual void removeEntity(Entity var1)
        {
            removeEntityAtIndex(var1, var1.chunkCoordY);
        }

        public virtual void removeEntityAtIndex(Entity var1, int var2)
        {
            if (var2 < 0)
            {
                var2 = 0;
            }

            if (var2 >= entities.Length)
            {
                var2 = entities.Length - 1;
            }

            entities[var2].Remove(var1);
        }

        public virtual bool canBlockSeeTheSky(int var1, int var2, int var3)
        {
            return var2 >= (heightMap[var3 << 4 | var1] & 255);
        }

        public virtual TileEntity getChunkBlockTileEntity(int var1, int var2, int var3)
        {
            ChunkPosition var4 = new(var1, var2, var3);
            chunkTileEntityMap.TryGetValue(var4, out TileEntity? var5);
            //TileEntity? var5 = (TileEntity)chunkTileEntityMap.get(var4);
            if (var5 == null)
            {
                int var6 = getBlockID(var1, var2, var3);
                if (!Block.isBlockContainer[var6])
                {
                    return null;
                }

                BlockContainer var7 = (BlockContainer)Block.blocksList[var6];
                var7.onBlockAdded(worldObj, xPosition * 16 + var1, var2, zPosition * 16 + var3);
                //var5 = (TileEntity)chunkTileEntityMap.get(var4);
                chunkTileEntityMap.TryGetValue(var4, out var5);
            }

            if (var5 != null && var5.isRemoved())
            {
                chunkTileEntityMap.Remove(var4);
                return null;
            }
            else
            {
                return var5;
            }
        }

        public virtual void addTileEntity(TileEntity var1)
        {
            int var2 = var1.x - xPosition * 16;
            int var3 = var1.y;
            int var4 = var1.z - zPosition * 16;
            setChunkBlockTileEntity(var2, var3, var4, var1);
            if (isChunkLoaded)
            {
                worldObj.loadedTileEntityList.Add(var1);
            }

        }

        public virtual void setChunkBlockTileEntity(int var1, int var2, int var3, TileEntity var4)
        {
            ChunkPosition var5 = new(var1, var2, var3);
            var4.world = worldObj;
            var4.x = xPosition * 16 + var1;
            var4.y = var2;
            var4.z = zPosition * 16 + var3;
            if (getBlockID(var1, var2, var3) != 0 && Block.blocksList[getBlockID(var1, var2, var3)] is BlockContainer)
            {
                var4.cancelRemoval();
                chunkTileEntityMap[var5] = var4;
            }
            else
            {
                java.lang.System.@out.println("Attempted to place a tile entity where there was no entity tile!");
            }
        }

        public virtual void removeChunkBlockTileEntity(int var1, int var2, int var3)
        {
            ChunkPosition var4 = new(var1, var2, var3);
            if (isChunkLoaded)
            {
                chunkTileEntityMap.TryGetValue(var4, out TileEntity? var5);
                //TileEntity var5 = chunkTileEntityMap.Remove(var4);
                if (var5 != null)
                {
                    chunkTileEntityMap.Remove(var4);
                    var5.markRemoved();
                }
            }

        }

        public virtual void onChunkLoad()
        {
            isChunkLoaded = true;
            worldObj.func_31054_a(chunkTileEntityMap.Values);

            for (int var1 = 0; var1 < entities.Length; ++var1)
            {
                worldObj.func_636_a(entities[var1]);
            }

        }

        public virtual void onChunkUnload()
        {
            isChunkLoaded = false;
            //Iterator var1 = chunkTileEntityMap.values().iterator();

            //while (var1.hasNext())
            //{
            //    TileEntity var2 = (TileEntity)var1.next();
            //    var2.func_31005_i();
            //}

            foreach (var var2 in chunkTileEntityMap.Values)
            {
                var2.markRemoved();
            }

            for (int var3 = 0; var3 < entities.Length; ++var3)
            {
                worldObj.func_632_b(entities[var3]);
            }

        }

        public virtual void setChunkModified()
        {
            isModified = true;
        }

        public virtual void getEntitiesWithinAABBForEntity(Entity var1, Box var2, List<Entity> var3)
        {
            int var4 = MathHelper.floor_double((var2.minY - 2.0D) / 16.0D);
            int var5 = MathHelper.floor_double((var2.maxY + 2.0D) / 16.0D);
            if (var4 < 0)
            {
                var4 = 0;
            }

            if (var5 >= entities.Length)
            {
                var5 = entities.Length - 1;
            }

            for (int var6 = var4; var6 <= var5; ++var6)
            {
                List<Entity> var7 = entities[var6];

                for (int var8 = 0; var8 < var7.Count; ++var8)
                {
                    Entity var9 = var7[var8];
                    if (var9 != var1 && var9.boundingBox.intersects(var2))
                    {
                        var3.Add(var9);
                    }
                }
            }

        }

        public virtual void getEntitiesOfTypeWithinAAAB(java.lang.Class var1, Box var2, List<Entity> var3)
        {
            int var4 = MathHelper.floor_double((var2.minY - 2.0D) / 16.0D);
            int var5 = MathHelper.floor_double((var2.maxY + 2.0D) / 16.0D);
            if (var4 < 0)
            {
                var4 = 0;
            }

            if (var5 >= entities.Length)
            {
                var5 = entities.Length - 1;
            }

            for (int var6 = var4; var6 <= var5; ++var6)
            {
                List<Entity> var7 = entities[var6];

                for (int var8 = 0; var8 < var7.Count; ++var8)
                {
                    Entity var9 = var7[var8];
                    if (var1.isAssignableFrom(var9.getClass()) && var9.boundingBox.intersects(var2))
                    {
                        var3.Add(var9);
                    }
                }
            }

        }

        public virtual bool needsSaving(bool var1)
        {
            if (neverSave)
            {
                return false;
            }
            else
            {
                if (var1)
                {
                    if (hasEntities && worldObj.getWorldTime() != lastSaveTime)
                    {
                        return true;
                    }
                }
                else if (hasEntities && worldObj.getWorldTime() >= lastSaveTime + 600L)
                {
                    return true;
                }

                return isModified;
            }
        }

        public virtual int setChunkData(byte[] var1, int var2, int var3, int var4, int var5, int var6, int var7, int var8)
        {
            int var9;
            int var10;
            int var11;
            int var12;
            for (var9 = var2; var9 < var5; ++var9)
            {
                for (var10 = var4; var10 < var7; ++var10)
                {
                    var11 = var9 << 11 | var10 << 7 | var3;
                    var12 = var6 - var3;
                    java.lang.System.arraycopy(var1, var8, blocks, var11, var12);
                    var8 += var12;
                }
            }

            generateHeightMap();

            for (var9 = var2; var9 < var5; ++var9)
            {
                for (var10 = var4; var10 < var7; ++var10)
                {
                    var11 = (var9 << 11 | var10 << 7 | var3) >> 1;
                    var12 = (var6 - var3) / 2;
                    java.lang.System.arraycopy(var1, var8, data.data, var11, var12);
                    var8 += var12;
                }
            }

            for (var9 = var2; var9 < var5; ++var9)
            {
                for (var10 = var4; var10 < var7; ++var10)
                {
                    var11 = (var9 << 11 | var10 << 7 | var3) >> 1;
                    var12 = (var6 - var3) / 2;
                    java.lang.System.arraycopy(var1, var8, blocklightMap.data, var11, var12);
                    var8 += var12;
                }
            }

            for (var9 = var2; var9 < var5; ++var9)
            {
                for (var10 = var4; var10 < var7; ++var10)
                {
                    var11 = (var9 << 11 | var10 << 7 | var3) >> 1;
                    var12 = (var6 - var3) / 2;
                    java.lang.System.arraycopy(var1, var8, skylightMap.data, var11, var12);
                    var8 += var12;
                }
            }

            return var8;
        }

        public virtual java.util.Random func_997_a(long var1)
        {
            return new java.util.Random(worldObj.getRandomSeed() + (long)(xPosition * xPosition * 4987142) + (long)(xPosition * 5947611) + (long)(zPosition * zPosition) * 4392871L + (long)(zPosition * 389711) ^ var1);
        }

        public virtual bool func_21167_h()
        {
            return false;
        }

        public void func_25124_i()
        {
            ChunkBlockMap.func_26002_a(blocks);
        }
    }
}