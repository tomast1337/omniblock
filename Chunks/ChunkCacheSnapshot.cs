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
            //TODO: OPTIMIZE THIS
            worldChunkManager = new(var1);

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

        public LightValue getBrightness2(int var1, int var2, int var3, int min)
        {
            LightValue var5 = getLightValueExt2(var1, var2, var3, true);

            if (var5.BlockLight < min)
            {
                var5.BlockLight = (byte)min;
            }

            return var5;
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

                    ChunkSnapshot chunk = chunkArray[var5][var6];
                    int lightValue = chunk.getBlockLightValue(var1 & 15, var2, var3 & 15, skylightSubtracted);

                    if (chunk.getIsLit())
                    {
                        isLit = true;
                    }

                    return lightValue;
                }
            }
            else
            {
                return 15;
            }
        }

        public LightValue getLightValueExt2(int x, int y, int z, bool checkSpecialBlocks)
        {
            if (x >= -32000000 && z >= -32000000 && x < 32000000 && z <= 32000000)
            {
                if (checkSpecialBlocks)
                {
                    int blockId = getBlockId(x, y, z);
                    if (blockId == Block.stairSingle.blockID ||
                        blockId == Block.tilledField.blockID ||
                        blockId == Block.stairCompactPlanks.blockID ||
                        blockId == Block.stairCompactCobblestone.blockID)
                    {
                        var light = getLightValueExt2(x, y + 1, z, false);
                        var lightXP = getLightValueExt2(x + 1, y, z, false);
                        var lightXN = getLightValueExt2(x - 1, y, z, false);
                        var lightZP = getLightValueExt2(x, y, z + 1, false);
                        var lightZN = getLightValueExt2(x, y, z - 1, false);

                        light.SkyLight = Math.Max(light.SkyLight, lightXP.SkyLight);
                        light.SkyLight = Math.Max(light.SkyLight, lightXN.SkyLight);
                        light.SkyLight = Math.Max(light.SkyLight, lightZP.SkyLight);
                        light.SkyLight = Math.Max(light.SkyLight, lightZN.SkyLight);

                        light.BlockLight = Math.Max(light.BlockLight, lightXP.BlockLight);
                        light.BlockLight = Math.Max(light.BlockLight, lightXN.BlockLight);
                        light.BlockLight = Math.Max(light.BlockLight, lightZP.BlockLight);
                        light.BlockLight = Math.Max(light.BlockLight, lightZN.BlockLight);

                        return light;
                    }
                }

                if (y < 0)
                {
                    return new LightValue { SkyLight = 0, BlockLight = 0 };
                }

                else if (y >= 128)
                {
                    byte skyLight = (byte)Math.Max(0, 15 - skylightSubtracted);
                    return new LightValue { SkyLight = skyLight, BlockLight = 0 };
                }

                else
                {
                    int chunkXIndex = (x >> 4) - chunkX;
                    int chunkZIndex = (z >> 4) - chunkZ;

                    ChunkSnapshot chunk = chunkArray[chunkXIndex][chunkZIndex];

                    byte skyLight = (byte)chunk.skylightMap.getNibble(x & 15, y, z & 15);
                    byte blockLight = (byte)chunk.blocklightMap.getNibble(x & 15, y, z & 15);

                    if (skyLight > 0)
                    {
                        isLit = true;
                    }

                    return new LightValue { SkyLight = skyLight, BlockLight = blockLight };
                }
            }
            else
            {
                return new LightValue { SkyLight = 15, BlockLight = 15 };
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
