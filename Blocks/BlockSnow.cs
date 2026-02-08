using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Items;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockSnow : Block
    {

        public BlockSnow(int id, int textureId) : base(id, textureId, Material.SNOW_LAYER)
        {
            setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F / 16.0F, 1.0F);
            setTickRandomly(true);
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            int var5 = world.getBlockMeta(x, y, z) & 7;
            return var5 >= 3 ? Box.createCached((double)x + minX, (double)y + minY, (double)z + minZ, (double)x + maxX, (double)((float)y + 0.5F), (double)z + maxZ) : null;
        }

        public override bool isOpaque()
        {
            return false;
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override void updateBoundingBox(BlockView blockView, int x, int y, int z)
        {
            int var5 = blockView.getBlockMeta(x, y, z) & 7;
            float var6 = (float)(2 * (1 + var5)) / 16.0F;
            setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, var6, 1.0F);
        }

        public override bool canPlaceAt(World world, int x, int y, int z)
        {
            int var5 = world.getBlockId(x, y - 1, z);
            return var5 != 0 && Block.BLOCKS[var5].isOpaque() ? world.getMaterial(x, y - 1, z).blocksMovement() : false;
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            breakIfCannotPlace(world, x, y, z);
        }

        private bool breakIfCannotPlace(World world, int x, int y, int z)
        {
            if (!canPlaceAt(world, x, y, z))
            {
                dropStacks(world, x, y, z, world.getBlockMeta(x, y, z));
                world.setBlockWithNotify(x, y, z, 0);
                return false;
            }
            else
            {
                return true;
            }
        }

        public override void afterBreak(World world, EntityPlayer player, int x, int y, int z, int meta)
        {
            int var7 = Item.snowball.id;
            float var8 = 0.7F;
            double var9 = (double)(world.random.nextFloat() * var8) + (double)(1.0F - var8) * 0.5D;
            double var11 = (double)(world.random.nextFloat() * var8) + (double)(1.0F - var8) * 0.5D;
            double var13 = (double)(world.random.nextFloat() * var8) + (double)(1.0F - var8) * 0.5D;
            EntityItem var15 = new EntityItem(world, (double)x + var9, (double)y + var11, (double)z + var13, new ItemStack(var7, 1, 0));
            var15.delayBeforeCanPickup = 10;
            world.spawnEntity(var15);
            world.setBlockWithNotify(x, y, z, 0);
            player.increaseStat(Stats.Stats.mineBlockStatArray[id], 1);
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Item.snowball.id;
        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return 0;
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (world.getBrightness(LightType.Block, x, y, z) > 11)
            {
                dropStacks(world, x, y, z, world.getBlockMeta(x, y, z));
                world.setBlockWithNotify(x, y, z, 0);
            }

        }

        public override bool isSideVisible(BlockView blockView, int x, int y, int z, int side)
        {
            return side == 1 ? true : base.isSideVisible(blockView, x, y, z, side);
        }
    }

}