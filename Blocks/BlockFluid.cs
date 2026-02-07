using betareborn.Entities;
using betareborn.Materials;
using betareborn.Worlds;
using Silk.NET.Maths;

namespace betareborn.Blocks
{
    public abstract class BlockFluid : Block
    {
        protected BlockFluid(int var1, Material var2) : base(var1, (var2 == Material.lava ? 14 : 12) * 16 + 13, var2)
        {
            float var3 = 0.0F;
            float var4 = 0.0F;
            setBlockBounds(0.0F + var4, 0.0F + var3, 0.0F + var4, 1.0F + var4, 1.0F + var3, 1.0F + var4);
            setTickOnLoad(true);
        }

        public override int colorMultiplier(IBlockAccess var1, int var2, int var3, int var4)
        {
            return 16777215;
        }

        public static float getPercentAir(int var0)
        {
            if (var0 >= 8)
            {
                var0 = 0;
            }

            float var1 = (float)(var0 + 1) / 9.0F;
            return var1;
        }

        public override int getBlockTextureFromSide(int var1)
        {
            return var1 != 0 && var1 != 1 ? blockIndexInTexture + 1 : blockIndexInTexture;
        }

        protected int getFlowDecay(World var1, int var2, int var3, int var4)
        {
            return var1.getBlockMaterial(var2, var3, var4) != blockMaterial ? -1 : var1.getBlockMetadata(var2, var3, var4);
        }

        protected int getEffectiveFlowDecay(IBlockAccess var1, int var2, int var3, int var4)
        {
            if (var1.getBlockMaterial(var2, var3, var4) != blockMaterial)
            {
                return -1;
            }
            else
            {
                int var5 = var1.getBlockMetadata(var2, var3, var4);
                if (var5 >= 8)
                {
                    var5 = 0;
                }

                return var5;
            }
        }

        public override bool renderAsNormalBlock()
        {
            return false;
        }

        public override bool isOpaqueCube()
        {
            return false;
        }

        public override bool canCollideCheck(int var1, bool var2)
        {
            return var2 && var1 == 0;
        }

        public override bool getIsBlockSolid(IBlockAccess var1, int var2, int var3, int var4, int var5)
        {
            Material var6 = var1.getBlockMaterial(var2, var3, var4);
            return var6 == blockMaterial ? false : (var6 == Material.ice ? false : (var5 == 1 ? true : base.getIsBlockSolid(var1, var2, var3, var4, var5)));
        }

        public override bool shouldSideBeRendered(IBlockAccess var1, int var2, int var3, int var4, int var5)
        {
            Material var6 = var1.getBlockMaterial(var2, var3, var4);
            return var6 == blockMaterial ? false : (var6 == Material.ice ? false : (var5 == 1 ? true : base.shouldSideBeRendered(var1, var2, var3, var4, var5)));
        }

        public override Box getCollisionBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            return null;
        }

        public override int getRenderType()
        {
            return 4;
        }

        public int idDropped(int var1, Random var2)
        {
            return 0;
        }

        public int quantityDropped(Random var1)
        {
            return 0;
        }

        private Vector3D<double> getFlowVector(IBlockAccess var1, int var2, int var3, int var4)
        {
            Vector3D<double> var5 = new(0.0);
            int var6 = getEffectiveFlowDecay(var1, var2, var3, var4);

            for (int var7 = 0; var7 < 4; ++var7)
            {
                int var8 = var2;
                int var10 = var4;
                if (var7 == 0)
                {
                    var8 = var2 - 1;
                }

                if (var7 == 1)
                {
                    var10 = var4 - 1;
                }

                if (var7 == 2)
                {
                    ++var8;
                }

                if (var7 == 3)
                {
                    ++var10;
                }

                int var11 = getEffectiveFlowDecay(var1, var8, var3, var10);
                int var12;
                if (var11 < 0)
                {
                    if (!var1.getBlockMaterial(var8, var3, var10).getIsSolid())
                    {
                        var11 = getEffectiveFlowDecay(var1, var8, var3 - 1, var10);
                        if (var11 >= 0)
                        {
                            var12 = var11 - (var6 - 8);
                            var5 += new Vector3D<double>((double)((var8 - var2) * var12), (double)((var3 - var3) * var12), (double)((var10 - var4) * var12));
                        }
                    }
                }
                else if (var11 >= 0)
                {
                    var12 = var11 - var6;
                    var5 += new Vector3D<double>((double)((var8 - var2) * var12), (double)((var3 - var3) * var12), (double)((var10 - var4) * var12));
                }
            }

            if (var1.getBlockMetadata(var2, var3, var4) >= 8)
            {
                bool var13 = false;
                if (var13 || getIsBlockSolid(var1, var2, var3, var4 - 1, 2))
                {
                    var13 = true;
                }

                if (var13 || getIsBlockSolid(var1, var2, var3, var4 + 1, 3))
                {
                    var13 = true;
                }

                if (var13 || getIsBlockSolid(var1, var2 - 1, var3, var4, 4))
                {
                    var13 = true;
                }

                if (var13 || getIsBlockSolid(var1, var2 + 1, var3, var4, 5))
                {
                    var13 = true;
                }

                if (var13 || getIsBlockSolid(var1, var2, var3 + 1, var4 - 1, 2))
                {
                    var13 = true;
                }

                if (var13 || getIsBlockSolid(var1, var2, var3 + 1, var4 + 1, 3))
                {
                    var13 = true;
                }

                if (var13 || getIsBlockSolid(var1, var2 - 1, var3 + 1, var4, 4))
                {
                    var13 = true;
                }

                if (var13 || getIsBlockSolid(var1, var2 + 1, var3 + 1, var4, 5))
                {
                    var13 = true;
                }

                if (var13)
                {
                    var5 = Normalize(var5) + new Vector3D<double>(0.0, -0.6, 0.0);
                    //var5 = var5.normalize().addVector(0.0D, -6.0D, 0.0D);
                }
            }

            //var5 = var5.normalize();
            var5 = Normalize(var5);
            return var5;
        }

        public override void velocityToAddToEntity(World var1, int var2, int var3, int var4, Entity var5, Vec3D var6)
        {
            Vector3D<double> var7 = getFlowVector(var1, var2, var3, var4);
            var6.xCoord += var7.X;
            var6.yCoord += var7.Y;
            var6.zCoord += var7.Z;
        }

        public override int tickRate()
        {
            return blockMaterial == Material.water ? 5 : (blockMaterial == Material.lava ? 30 : 0);
        }

        public override float getBlockBrightness(IBlockAccess var1, int var2, int var3, int var4)
        {
            float var5 = var1.getLightBrightness(var2, var3, var4);
            float var6 = var1.getLightBrightness(var2, var3 + 1, var4);
            return var5 > var6 ? var5 : var6;
        }

        public override void updateTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
            base.updateTick(var1, var2, var3, var4, var5);
        }

        public override int getRenderBlockPass()
        {
            return blockMaterial == Material.water ? 1 : 0;
        }

        public override void randomDisplayTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
            if (blockMaterial == Material.water && var5.nextInt(64) == 0)
            {
                int var6 = var1.getBlockMetadata(var2, var3, var4);
                if (var6 > 0 && var6 < 8)
                {
                    var1.playSoundEffect((double)((float)var2 + 0.5F), (double)((float)var3 + 0.5F), (double)((float)var4 + 0.5F), "liquid.water", var5.nextFloat() * 0.25F + 12.0F / 16.0F, var5.nextFloat() * 1.0F + 0.5F);
                }
            }

            if (blockMaterial == Material.lava && var1.getBlockMaterial(var2, var3 + 1, var4) == Material.air && !var1.isBlockOpaqueCube(var2, var3 + 1, var4) && var5.nextInt(100) == 0)
            {
                double var12 = (double)((float)var2 + var5.nextFloat());
                double var8 = (double)var3 + maxY;
                double var10 = (double)((float)var4 + var5.nextFloat());
                var1.addParticle("lava", var12, var8, var10, 0.0D, 0.0D, 0.0D);
            }

        }

        public static double func_293_a(IBlockAccess var0, int var1, int var2, int var3, Material var4)
        {
            Vector3D<double> var5 = new(0.0);
            if (var4 == Material.water)
            {
                var5 = ((BlockFluid)waterMoving).getFlowVector(var0, var1, var2, var3);
            }

            if (var4 == Material.lava)
            {
                var5 = ((BlockFluid)lavaMoving).getFlowVector(var0, var1, var2, var3);
            }

            return var5.X == 0.0D && var5.Z == 0.0D ? -1000.0D : java.lang.Math.atan2(var5.Z, var5.X) - Math.PI * 0.5D;
        }

        public override void onBlockAdded(World var1, int var2, int var3, int var4)
        {
            checkForHarden(var1, var2, var3, var4);
        }

        public override void onNeighborBlockChange(World var1, int var2, int var3, int var4, int var5)
        {
            checkForHarden(var1, var2, var3, var4);
        }

        private void checkForHarden(World var1, int var2, int var3, int var4)
        {
            if (var1.getBlockId(var2, var3, var4) == blockID)
            {
                if (blockMaterial == Material.lava)
                {
                    bool var5 = false;
                    if (var5 || var1.getBlockMaterial(var2, var3, var4 - 1) == Material.water)
                    {
                        var5 = true;
                    }

                    if (var5 || var1.getBlockMaterial(var2, var3, var4 + 1) == Material.water)
                    {
                        var5 = true;
                    }

                    if (var5 || var1.getBlockMaterial(var2 - 1, var3, var4) == Material.water)
                    {
                        var5 = true;
                    }

                    if (var5 || var1.getBlockMaterial(var2 + 1, var3, var4) == Material.water)
                    {
                        var5 = true;
                    }

                    if (var5 || var1.getBlockMaterial(var2, var3 + 1, var4) == Material.water)
                    {
                        var5 = true;
                    }

                    if (var5)
                    {
                        int var6 = var1.getBlockMetadata(var2, var3, var4);
                        if (var6 == 0)
                        {
                            var1.setBlockWithNotify(var2, var3, var4, Block.obsidian.blockID);
                        }
                        else if (var6 <= 4)
                        {
                            var1.setBlockWithNotify(var2, var3, var4, Block.cobblestone.blockID);
                        }

                        triggerLavaMixEffects(var1, var2, var3, var4);
                    }
                }

            }
        }

        protected void triggerLavaMixEffects(World var1, int var2, int var3, int var4)
        {
            var1.playSoundEffect((double)((float)var2 + 0.5F), (double)((float)var3 + 0.5F), (double)((float)var4 + 0.5F), "random.fizz", 0.5F, 2.6F + (var1.rand.nextFloat() - var1.rand.nextFloat()) * 0.8F);

            for (int var5 = 0; var5 < 8; ++var5)
            {
                var1.addParticle("largesmoke", (double)var2 + java.lang.Math.random(), (double)var3 + 1.2D, (double)var4 + java.lang.Math.random(), 0.0D, 0.0D, 0.0D);
            }

        }

        private static Vector3D<double> Normalize(Vector3D<double> vec)
        {
            double var1 = (double)MathHelper.sqrt_double(vec.X * vec.X + vec.Y * vec.Y + vec.Z * vec.Z);
            return var1 < 1.0E-4D ? new(0.0) : new(vec.X / var1, vec.Y / var1, vec.Z / var1);
        }
    }

}