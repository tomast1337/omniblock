using betareborn.Entities;
using betareborn.Worlds;
using java.util;

namespace betareborn.Blocks
{
    public class BlockStairs : Block
    {

        private Block modelBlock;

        public BlockStairs(int var1, Block var2) : base(var1, var2.textureId, var2.blockMaterial)
        {
            modelBlock = var2;
            setHardness(var2.blockHardness);
            setResistance(var2.blockResistance / 3.0F);
            setStepSound(var2.stepSound);
            setLightOpacity(255);
        }

        public override void updateBoundingBox(BlockView var1, int var2, int var3, int var4)
        {
            setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
        }

        public override Box getCollisionBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            return base.getCollisionBoundingBoxFromPool(var1, var2, var3, var4);
        }

        public override bool isOpaque()
        {
            return false;
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override int getRenderType()
        {
            return 10;
        }

        public override bool shouldSideBeRendered(BlockView var1, int var2, int var3, int var4, int var5)
        {
            return base.shouldSideBeRendered(var1, var2, var3, var4, var5);
        }

        public override void getCollidingBoundingBoxes(World var1, int var2, int var3, int var4, Box var5, List<Box> var6)
        {
            int var7 = var1.getBlockMeta(var2, var3, var4);
            if (var7 == 0)
            {
                setBoundingBox(0.0F, 0.0F, 0.0F, 0.5F, 0.5F, 1.0F);
                base.getCollidingBoundingBoxes(var1, var2, var3, var4, var5, var6);
                setBoundingBox(0.5F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
                base.getCollidingBoundingBoxes(var1, var2, var3, var4, var5, var6);
            }
            else if (var7 == 1)
            {
                setBoundingBox(0.0F, 0.0F, 0.0F, 0.5F, 1.0F, 1.0F);
                base.getCollidingBoundingBoxes(var1, var2, var3, var4, var5, var6);
                setBoundingBox(0.5F, 0.0F, 0.0F, 1.0F, 0.5F, 1.0F);
                base.getCollidingBoundingBoxes(var1, var2, var3, var4, var5, var6);
            }
            else if (var7 == 2)
            {
                setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 0.5F, 0.5F);
                base.getCollidingBoundingBoxes(var1, var2, var3, var4, var5, var6);
                setBoundingBox(0.0F, 0.0F, 0.5F, 1.0F, 1.0F, 1.0F);
                base.getCollidingBoundingBoxes(var1, var2, var3, var4, var5, var6);
            }
            else if (var7 == 3)
            {
                setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 0.5F);
                base.getCollidingBoundingBoxes(var1, var2, var3, var4, var5, var6);
                setBoundingBox(0.0F, 0.0F, 0.5F, 1.0F, 0.5F, 1.0F);
                base.getCollidingBoundingBoxes(var1, var2, var3, var4, var5, var6);
            }

            setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
        }

        public override void randomDisplayTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
            modelBlock.randomDisplayTick(var1, var2, var3, var4, var5);
        }

        public override void onBlockClicked(World var1, int var2, int var3, int var4, EntityPlayer var5)
        {
            modelBlock.onBlockClicked(var1, var2, var3, var4, var5);
        }

        public override void onBlockDestroyedByPlayer(World var1, int var2, int var3, int var4, int var5)
        {
            modelBlock.onBlockDestroyedByPlayer(var1, var2, var3, var4, var5);
        }

        public override float getBlockBrightness(BlockView var1, int var2, int var3, int var4)
        {
            return modelBlock.getBlockBrightness(var1, var2, var3, var4);
        }

        public override float getExplosionResistance(Entity var1)
        {
            return modelBlock.getExplosionResistance(var1);
        }

        public override int getRenderBlockPass()
        {
            return modelBlock.getRenderBlockPass();
        }

        public override int getDroppedItemId(int var1, java.util.Random var2)
        {
            return modelBlock.getDroppedItemId(var1, var2);
        }

        public override int quantityDropped(java.util.Random var1)
        {
            return modelBlock.quantityDropped(var1);
        }

        public override int getTexture(int var1, int var2)
        {
            return modelBlock.getTexture(var1, var2);
        }

        public override int getBlockTextureFromSide(int var1)
        {
            return modelBlock.getBlockTextureFromSide(var1);
        }

        public override int getBlockTexture(BlockView var1, int var2, int var3, int var4, int var5)
        {
            return modelBlock.getBlockTexture(var1, var2, var3, var4, var5);
        }

        public override int tickRate()
        {
            return modelBlock.tickRate();
        }

        public override Box getSelectedBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            return modelBlock.getSelectedBoundingBoxFromPool(var1, var2, var3, var4);
        }

        public override void velocityToAddToEntity(World var1, int var2, int var3, int var4, Entity var5, Vec3D var6)
        {
            modelBlock.velocityToAddToEntity(var1, var2, var3, var4, var5, var6);
        }

        public override bool isCollidable()
        {
            return modelBlock.isCollidable();
        }

        public override bool canCollideCheck(int var1, bool var2)
        {
            return modelBlock.canCollideCheck(var1, var2);
        }

        public override bool canPlaceBlockAt(World var1, int var2, int var3, int var4)
        {
            return modelBlock.canPlaceBlockAt(var1, var2, var3, var4);
        }

        public override void onBlockAdded(World var1, int var2, int var3, int var4)
        {
            neighborUpdate(var1, var2, var3, var4, 0);
            modelBlock.onBlockAdded(var1, var2, var3, var4);
        }

        public override void onBlockRemoval(World var1, int var2, int var3, int var4)
        {
            modelBlock.onBlockRemoval(var1, var2, var3, var4);
        }

        public override void dropStacks(World var1, int var2, int var3, int var4, int var5, float var6)
        {
            modelBlock.dropStacks(var1, var2, var3, var4, var5, var6);
        }

        public override void onEntityWalking(World var1, int var2, int var3, int var4, Entity var5)
        {
            modelBlock.onEntityWalking(var1, var2, var3, var4, var5);
        }

        public override void updateTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
            modelBlock.updateTick(var1, var2, var3, var4, var5);
        }

        public override bool onUse(World var1, int var2, int var3, int var4, EntityPlayer var5)
        {
            return modelBlock.onUse(var1, var2, var3, var4, var5);
        }

        public override void onBlockDestroyedByExplosion(World var1, int var2, int var3, int var4)
        {
            modelBlock.onBlockDestroyedByExplosion(var1, var2, var3, var4);
        }

        public override void onBlockPlacedBy(World var1, int var2, int var3, int var4, EntityLiving var5)
        {
            int var6 = MathHelper.floor_double((double)(var5.rotationYaw * 4.0F / 360.0F) + 0.5D) & 3;
            if (var6 == 0)
            {
                var1.setBlockMeta(var2, var3, var4, 2);
            }

            if (var6 == 1)
            {
                var1.setBlockMeta(var2, var3, var4, 1);
            }

            if (var6 == 2)
            {
                var1.setBlockMeta(var2, var3, var4, 3);
            }

            if (var6 == 3)
            {
                var1.setBlockMeta(var2, var3, var4, 0);
            }

        }
    }

}