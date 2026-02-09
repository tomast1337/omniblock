using betareborn.Entities;
using betareborn.Items;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockCrops : BlockPlant
    {

        public BlockCrops(int i, int j) : base(i, j)
        {
            textureId = j;
            setTickRandomly(true);
            float var3 = 0.5F;
            setBoundingBox(0.5F - var3, 0.0F, 0.5F - var3, 0.5F + var3, 0.25F, 0.5F + var3);
        }

        protected override bool canPlantOnTop(int id)
        {
            return id == Block.FARMLAND.id;
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            base.onTick(world, x, y, z, random);
            if (world.getLightLevel(x, y + 1, z) >= 9)
            {
                int var6 = world.getBlockMeta(x, y, z);
                if (var6 < 7)
                {
                    float var7 = getAvailableMoisture(world, x, y, z);
                    if (random.nextInt((int)(100.0F / var7)) == 0)
                    {
                        ++var6;
                        world.setBlockMeta(x, y, z, var6);
                    }
                }
            }

        }

        public void applyFullGrowth(World world, int x, int y, int z)
        {
            world.setBlockMeta(x, y, z, 7);
        }

        private float getAvailableMoisture(World world, int x, int y, int z)
        {
            float var5 = 1.0F;
            int var6 = world.getBlockId(x, y, z - 1);
            int var7 = world.getBlockId(x, y, z + 1);
            int var8 = world.getBlockId(x - 1, y, z);
            int var9 = world.getBlockId(x + 1, y, z);
            int var10 = world.getBlockId(x - 1, y, z - 1);
            int var11 = world.getBlockId(x + 1, y, z - 1);
            int var12 = world.getBlockId(x + 1, y, z + 1);
            int var13 = world.getBlockId(x - 1, y, z + 1);
            bool var14 = var8 == id || var9 == id;
            bool var15 = var6 == id || var7 == id;
            bool var16 = var10 == id || var11 == id || var12 == id || var13 == id;

            for (int var17 = x - 1; var17 <= x + 1; ++var17)
            {
                for (int var18 = z - 1; var18 <= z + 1; ++var18)
                {
                    int var19 = world.getBlockId(var17, y - 1, var18);
                    float var20 = 0.0F;
                    if (var19 == Block.FARMLAND.id)
                    {
                        var20 = 1.0F;
                        if (world.getBlockMeta(var17, y - 1, var18) > 0)
                        {
                            var20 = 3.0F;
                        }
                    }

                    if (var17 != x || var18 != z)
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

        public override int getTexture(int side, int meta)
        {
            if (meta < 0)
            {
                meta = 7;
            }

            return textureId + meta;
        }

        public override int getRenderType()
        {
            return 6;
        }

        public override void dropStacks(World world, int x, int y, int z, int meta, float luck)
        {
            base.dropStacks(world, x, y, z, meta, luck);
            if (!world.isRemote)
            {
                for (int var7 = 0; var7 < 3; ++var7)
                {
                    if (world.random.nextInt(15) <= meta)
                    {
                        float var8 = 0.7F;
                        float var9 = world.random.nextFloat() * var8 + (1.0F - var8) * 0.5F;
                        float var10 = world.random.nextFloat() * var8 + (1.0F - var8) * 0.5F;
                        float var11 = world.random.nextFloat() * var8 + (1.0F - var8) * 0.5F;
                        EntityItem var12 = new EntityItem(world, (double)((float)x + var9), (double)((float)y + var10), (double)((float)z + var11), new ItemStack(Item.SEEDS));
                        var12.delayBeforeCanPickup = 10;
                        world.spawnEntity(var12);
                    }
                }

            }
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return blockMeta == 7 ? Item.WHEAT.id : -1;
        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return 1;
        }
    }

}