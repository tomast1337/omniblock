using betareborn.Entities;
using betareborn.Items;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockCrops : BlockFlower
    {

        public BlockCrops(int var1, int var2) : base(var1, var2)
        {
            blockIndexInTexture = var2;
            setTickOnLoad(true);
            float var3 = 0.5F;
            setBlockBounds(0.5F - var3, 0.0F, 0.5F - var3, 0.5F + var3, 0.25F, 0.5F + var3);
        }

        protected override bool canThisPlantGrowOnThisBlockID(int var1)
        {
            return var1 == Block.tilledField.blockID;
        }

        public override void updateTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
            base.updateTick(var1, var2, var3, var4, var5);
            if (var1.getBlockLightValue(var2, var3 + 1, var4) >= 9)
            {
                int var6 = var1.getBlockMetadata(var2, var3, var4);
                if (var6 < 7)
                {
                    float var7 = getGrowthRate(var1, var2, var3, var4);
                    if (var5.nextInt((int)(100.0F / var7)) == 0)
                    {
                        ++var6;
                        var1.setBlockMetadataWithNotify(var2, var3, var4, var6);
                    }
                }
            }

        }

        public void fertilize(World var1, int var2, int var3, int var4)
        {
            var1.setBlockMetadataWithNotify(var2, var3, var4, 7);
        }

        private float getGrowthRate(World var1, int var2, int var3, int var4)
        {
            float var5 = 1.0F;
            int var6 = var1.getBlockId(var2, var3, var4 - 1);
            int var7 = var1.getBlockId(var2, var3, var4 + 1);
            int var8 = var1.getBlockId(var2 - 1, var3, var4);
            int var9 = var1.getBlockId(var2 + 1, var3, var4);
            int var10 = var1.getBlockId(var2 - 1, var3, var4 - 1);
            int var11 = var1.getBlockId(var2 + 1, var3, var4 - 1);
            int var12 = var1.getBlockId(var2 + 1, var3, var4 + 1);
            int var13 = var1.getBlockId(var2 - 1, var3, var4 + 1);
            bool var14 = var8 == blockID || var9 == blockID;
            bool var15 = var6 == blockID || var7 == blockID;
            bool var16 = var10 == blockID || var11 == blockID || var12 == blockID || var13 == blockID;

            for (int var17 = var2 - 1; var17 <= var2 + 1; ++var17)
            {
                for (int var18 = var4 - 1; var18 <= var4 + 1; ++var18)
                {
                    int var19 = var1.getBlockId(var17, var3 - 1, var18);
                    float var20 = 0.0F;
                    if (var19 == Block.tilledField.blockID)
                    {
                        var20 = 1.0F;
                        if (var1.getBlockMetadata(var17, var3 - 1, var18) > 0)
                        {
                            var20 = 3.0F;
                        }
                    }

                    if (var17 != var2 || var18 != var4)
                    {
                        var20 /= 4.0F;
                    }

                    var5 += var20;
                }
            }

            if (var16 || var14 && var15)
            {
                var5 /= 2.0F;
            }

            return var5;
        }

        public override int getBlockTextureFromSideAndMetadata(int var1, int var2)
        {
            if (var2 < 0)
            {
                var2 = 7;
            }

            return blockIndexInTexture + var2;
        }

        public override int getRenderType()
        {
            return 6;
        }

        public override void dropBlockAsItemWithChance(World var1, int var2, int var3, int var4, int var5, float var6)
        {
            base.dropBlockAsItemWithChance(var1, var2, var3, var4, var5, var6);
            if (!var1.multiplayerWorld)
            {
                for (int var7 = 0; var7 < 3; ++var7)
                {
                    if (var1.random.nextInt(15) <= var5)
                    {
                        float var8 = 0.7F;
                        float var9 = var1.random.nextFloat() * var8 + (1.0F - var8) * 0.5F;
                        float var10 = var1.random.nextFloat() * var8 + (1.0F - var8) * 0.5F;
                        float var11 = var1.random.nextFloat() * var8 + (1.0F - var8) * 0.5F;
                        EntityItem var12 = new EntityItem(var1, (double)((float)var2 + var9), (double)((float)var3 + var10), (double)((float)var4 + var11), new ItemStack(Item.seeds));
                        var12.delayBeforeCanPickup = 10;
                        var1.spawnEntity(var12);
                    }
                }

            }
        }

        public override int idDropped(int var1, java.util.Random var2)
        {
            return var1 == 7 ? Item.wheat.id : -1;
        }

        public override int quantityDropped(java.util.Random var1)
        {
            return 1;
        }
    }

}