using betareborn.Blocks;
using betareborn.Materials;
using betareborn.TileEntities;
using betareborn.Worlds;

namespace betareborn.Chunks
{
    public class ChunkCacheSnapshot : IBlockAccess, IDisposable
    {
        private readonly int chunkX;
        private readonly int chunkZ;
        private readonly ChunkSnapshot[][] chunkArray;
        private readonly float[] lightTable;
        private readonly int skylightSubtracted;
        private readonly WorldChunkManager worldChunkManager;
        private bool isLit = false;

        public ChunkCacheSnapshot(World var1, int var2, int var3, int var4, int var5, int var6, int var7)
        {
            worldChunkManager = var1.chunkManagerCache.get(var1);

            chunkX = var2 >> 4;
            chunkZ = var4 >> 4;
            int var8 = var5 >> 4;
            int var9 = var7 >> 4;
            chunkArray = new ChunkSnapshot[var8 - chunkX + 1][];
            for (int i = 0; i < chunkArray.Length; i++)
            {
                chunkArray[i] = new ChunkSnapshot[var9 - chunkZ + 1];
            }

            for (int var10 = chunkX; var10 <= var8; ++var10)
            {
                for (int var11 = chunkZ; var11 <= var9; ++var11)
                {
                    chunkArray[var10 - chunkX][var11 - chunkZ] = new(var1.getChunkFromChunkCoords(var10, var11));
                }
            }

            lightTable = new float[var1.worldProvider.lightBrightnessTable.Length];
            Buffer.BlockCopy(var1.worldProvider.lightBrightnessTable, 0, lightTable, 0, sizeof(float) * lightTable.Length);
            skylightSubtracted = var1.skylightSubtracted;
        }

        public int getBlockId(int var1, int var2, int var3)
        {
            if (var2 < 0)
            {
                return 0;
            }
            else if (var2 >= 128)
            {
                return 0;
            }
            else
            {
                int var4 = (var1 >> 4) - chunkX;
                int var5 = (var3 >> 4) - chunkZ;
                if (var4 >= 0 && var4 < chunkArray.Length && var5 >= 0 && var5 < chunkArray[var4].Length)
                {
                    ChunkSnapshot var6 = chunkArray[var4][var5];
                    return var6 == null ? 0 : var6.getBlockID(var1 & 15, var2, var3 & 15);
                }
                else
                {
                    return 0;
                }
            }
        }

        public Material getBlockMaterial(int var1, int var2, int var3)
        {
            int var4 = getBlockId(var1, var2, var3);
            return var4 == 0 ? Material.air : Block.blocksList[var4].blockMaterial;
        }

        public int getBlockMetadata(int var1, int var2, int var3)
        {
            if (var2 < 0)
            {
                return 0;
            }
            else if (var2 >= 128)
            {
                return 0;
            }
            else
            {
                int var4 = (var1 >> 4) - chunkX;
                int var5 = (var3 >> 4) - chunkZ;
                return chunkArray[var4][var5].getBlockMetadata(var1 & 15, var2, var3 & 15);
            }
        }

        public TileEntity getBlockTileEntity(int var1, int var2, int var3)
        {
            throw new NotImplementedException();
        }

        public float getBrightness(int var1, int var2, int var3, int var4)
        {
            int var5 = getLightValue(var1, var2, var3);
            if (var5 < var4)
            {
                var5 = var4;
            }

            return lightTable[var5];
        }

        public float getLightBrightness(int var1, int var2, int var3)
        {
            return lightTable[getLightValue(var1, var2, var3)];
        }

        public int getLightValue(int var1, int var2, int var3)
        {
            return getLightValueExt(var1, var2, var3, true);
        }

        public int getLightValueExt(int var1, int var2, int var3, bool var4)
        {
            if (var1 >= -32000000 && var3 >= -32000000 && var1 < 32000000 && var3 <= 32000000)
            {
                int var5;
                int var6;
                if (var4)
                {
                    var5 = getBlockId(var1, var2, var3);
                    if (var5 == Block.stairSingle.blockID || var5 == Block.tilledField.blockID || var5 == Block.stairCompactPlanks.blockID || var5 == Block.stairCompactCobblestone.blockID)
                    {
                        var6 = getLightValueExt(var1, var2 + 1, var3, false);
                        int var7 = getLightValueExt(var1 + 1, var2, var3, false);
                        int var8 = getLightValueExt(var1 - 1, var2, var3, false);
                        int var9 = getLightValueExt(var1, var2, var3 + 1, false);
                        int var10 = getLightValueExt(var1, var2, var3 - 1, false);
                        if (var7 > var6)
                        {
                            var6 = var7;
                        }

                        if (var8 > var6)
                        {
                            var6 = var8;
                        }

                        if (var9 > var6)
                        {
                            var6 = var9;
                        }

                        if (var10 > var6)
                        {
                            var6 = var10;
                        }

                        return var6;
                    }
                }

                if (var2 < 0)
                {
                    return 0;
                }
                else if (var2 >= 128)
                {
                    var5 = 15 - skylightSubtracted;
                    if (var5 < 0)
                    {
                        var5 = 0;
                    }

                    return var5;
                }
                else
                {
                    var5 = (var1 >> 4) - chunkX;
                    var6 = (var3 >> 4) - chunkZ;
                    return chunkArray[var5][var6].getBlockLightValue(var1 & 15, var2, var3 & 15, skylightSubtracted, out isLit);
                }
            }
            else
            {
                return 15;
            }
        }

        public WorldChunkManager getWorldChunkManager()
        {
            return worldChunkManager;
        }

        public bool isBlockNormalCube(int var1, int var2, int var3)
        {
            Block var4 = Block.blocksList[getBlockId(var1, var2, var3)];
            return var4 == null ? false : var4.blockMaterial.getIsSolid() && var4.renderAsNormalBlock();
        }

        public bool isBlockOpaqueCube(int var1, int var2, int var3)
        {
            Block var4 = Block.blocksList[getBlockId(var1, var2, var3)];
            return var4 == null ? false : var4.isOpaqueCube();
        }

        public bool getIsLit()
        {
            return isLit;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);

            foreach (var column in chunkArray)
            {
                if (column == null) continue;

                foreach (var snapshot in column)
                {
                    snapshot?.Dispose();
                }
            }
        }
    }
}
