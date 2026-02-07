using betareborn.Chunks;
using betareborn.Entities;
using betareborn.TileEntities;
using betareborn.Worlds;
using java.lang;

namespace betareborn
{
    public class EmptyChunk : Chunk
    {
        public EmptyChunk(World var1, int var2, int var3) : base(var1, var2, var3)
        {
            neverSave = true;
        }

        public EmptyChunk(World var1, byte[] var2, int var3, int var4) : base(var1, var2, var3, var4)
        {
            neverSave = true;
        }

        public override bool isAtLocation(int var1, int var2)
        {
            return var1 == xPosition && var2 == zPosition;
        }

        public override int getHeightValue(int var1, int var2)
        {
            return 0;
        }

        public override void func_1014_a()
        {
        }

        public override void generateHeightMap()
        {
        }

        public override void func_1024_c()
        {
        }

        public override void func_4143_d()
        {
        }

        public override int getBlockID(int var1, int var2, int var3)
        {
            return 0;
        }

        public override bool setBlockIDWithMetadata(int var1, int var2, int var3, int var4, int var5)
        {
            return true;
        }

        public override bool setBlockID(int var1, int var2, int var3, int var4)
        {
            return true;
        }

        public override int getBlockMetadata(int var1, int var2, int var3)
        {
            return 0;
        }

        public override void setBlockMetadata(int var1, int var2, int var3, int var4)
        {
        }

        public override int getSavedLightValue(EnumSkyBlock var1, int var2, int var3, int var4)
        {
            return 0;
        }

        public override void setLightValue(EnumSkyBlock var1, int var2, int var3, int var4, int var5)
        {
        }

        public override int getBlockLightValue(int var1, int var2, int var3, int var4)
        {
            return 0;
        }

        public override void addEntity(Entity var1)
        {
        }

        public override void removeEntity(Entity var1)
        {
        }

        public override void removeEntityAtIndex(Entity var1, int var2)
        {
        }

        public override bool canBlockSeeTheSky(int var1, int var2, int var3)
        {
            return false;
        }

        public override TileEntity getChunkBlockTileEntity(int var1, int var2, int var3)
        {
            return null;
        }

        public override void addTileEntity(TileEntity var1)
        {
        }

        public override void setChunkBlockTileEntity(int var1, int var2, int var3, TileEntity var4)
        {
        }

        public override void removeChunkBlockTileEntity(int var1, int var2, int var3)
        {
        }

        public override void onChunkLoad()
        {
        }

        public override void onChunkUnload()
        {
        }

        public override void setChunkModified()
        {
        }

        public override void getEntitiesWithinAABBForEntity(Entity var1, Box var2, List<Entity> var3)
        {
        }

        public override void getEntitiesOfTypeWithinAAAB(Class var1, Box var2, List<Entity> var3)
        {
        }

        public override bool needsSaving(bool var1)
        {
            return false;
        }

        public override int setChunkData(byte[] var1, int var2, int var3, int var4, int var5, int var6, int var7, int var8)
        {
            int var9 = var5 - var2;
            int var10 = var6 - var3;
            int var11 = var7 - var4;
            int var12 = var9 * var10 * var11;
            return var12 + var12 / 2 * 3;
        }

        public override java.util.Random func_997_a(long var1)
        {
            return new(worldObj.getRandomSeed() + (long)(xPosition * xPosition * 4987142) + (long)(xPosition * 5947611) + (long)(zPosition * zPosition) * 4392871L + (long)(zPosition * 389711) ^ var1);
        }

        public override bool func_21167_h()
        {
            return true;
        }
    }
}