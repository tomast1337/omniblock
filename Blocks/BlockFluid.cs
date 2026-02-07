using betareborn.Entities;
using betareborn.Materials;
using betareborn.Worlds;
using Silk.NET.Maths;

namespace betareborn.Blocks
{
    public abstract class BlockFluid : Block
    {
        protected BlockFluid(int id, Material material) : base(id, (material == Material.LAVA ? 14 : 12) * 16 + 13, material)
        {
            float var3 = 0.0F;
            float var4 = 0.0F;
            setBoundingBox(0.0F + var4, 0.0F + var3, 0.0F + var4, 1.0F + var4, 1.0F + var3, 1.0F + var4);
            setTickRandomly(true);
        }

        public override int getColorMultiplier(BlockView blockView, int x, int y, int z)
        {
            return 16777215;
        }

        public static float getFluidHeightFromMeta(int meta)
        {
            if (meta >= 8)
            {
                meta = 0;
            }

            float var1 = (float)(meta + 1) / 9.0F;
            return var1;
        }

        public override int getTexture(int side)
        {
            return side != 0 && side != 1 ? textureId + 1 : textureId;
        }

        protected int getLiquidState(World world, int x, int y, int z)
        {
            return world.getMaterial(x, y, z) != material ? -1 : world.getBlockMeta(x, y, z);
        }

        protected int getLiquidDepth(BlockView blockView, int x, int y, int z)
        {
            if (blockView.getMaterial(x, y, z) != material)
            {
                return -1;
            }
            else
            {
                int var5 = blockView.getBlockMeta(x, y, z);
                if (var5 >= 8)
                {
                    var5 = 0;
                }

                return var5;
            }
        }

        public override bool isFullCube()
        {
            return false;
        }

        public override bool isOpaque()
        {
            return false;
        }

        public override bool hasCollision(int meta, bool allowLiquids)
        {
            return allowLiquids && meta == 0;
        }

        public override bool isSolidFace(BlockView blockView, int x, int y, int z, int face)
        {
            Material var6 = blockView.getMaterial(x, y, z);
            return var6 == material ? false : (var6 == Material.ICE ? false : (face == 1 ? true : base.isSolidFace(blockView, x, y, z, face)));
        }

        public override bool isSideVisible(BlockView blockView, int x, int y, int z, int side)
        {
            Material var6 = blockView.getMaterial(x, y, z);
            return var6 == material ? false : (var6 == Material.ICE ? false : (side == 1 ? true : base.isSideVisible(blockView, x, y, z, side)));
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            return null;
        }

        public override int getRenderType()
        {
            return 4;
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return 0;
        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return 0;
        }

        private Vector3D<double> getFlow(BlockView blockView, int x, int y, int z)
        {
            Vector3D<double> var5 = new(0.0);
            int var6 = getLiquidDepth(blockView, x, y, z);

            for (int var7 = 0; var7 < 4; ++var7)
            {
                int var8 = x;
                int var10 = z;
                if (var7 == 0)
                {
                    var8 = x - 1;
                }

                if (var7 == 1)
                {
                    var10 = z - 1;
                }

                if (var7 == 2)
                {
                    ++var8;
                }

                if (var7 == 3)
                {
                    ++var10;
                }

                int var11 = getLiquidDepth(blockView, var8, y, var10);
                int var12;
                if (var11 < 0)
                {
                    if (!blockView.getMaterial(var8, y, var10).blocksMovement())
                    {
                        var11 = getLiquidDepth(blockView, var8, y - 1, var10);
                        if (var11 >= 0)
                        {
                            var12 = var11 - (var6 - 8);
                            var5 += new Vector3D<double>((double)((var8 - x) * var12), (double)((y - y) * var12), (double)((var10 - z) * var12));
                        }
                    }
                }
                else if (var11 >= 0)
                {
                    var12 = var11 - var6;
                    var5 += new Vector3D<double>((double)((var8 - x) * var12), (double)((y - y) * var12), (double)((var10 - z) * var12));
                }
            }

            if (blockView.getBlockMeta(x, y, z) >= 8)
            {
                bool var13 = false;
                if (var13 || isSolidFace(blockView, x, y, z - 1, 2))
                {
                    var13 = true;
                }

                if (var13 || isSolidFace(blockView, x, y, z + 1, 3))
                {
                    var13 = true;
                }

                if (var13 || isSolidFace(blockView, x - 1, y, z, 4))
                {
                    var13 = true;
                }

                if (var13 || isSolidFace(blockView, x + 1, y, z, 5))
                {
                    var13 = true;
                }

                if (var13 || isSolidFace(blockView, x, y + 1, z - 1, 2))
                {
                    var13 = true;
                }

                if (var13 || isSolidFace(blockView, x, y + 1, z + 1, 3))
                {
                    var13 = true;
                }

                if (var13 || isSolidFace(blockView, x - 1, y + 1, z, 4))
                {
                    var13 = true;
                }

                if (var13 || isSolidFace(blockView, x + 1, y + 1, z, 5))
                {
                    var13 = true;
                }

                if (var13)
                {
                    var5 = Normalize(var5) + new Vector3D<double>(0.0, -0.6, 0.0);
                }
            }

            var5 = Normalize(var5);
            return var5;
        }

        public override void applyVelocity(World world, int x, int y, int z, Entity entity, Vec3D velocity)
        {
            Vector3D<double> var7 = getFlow(world, x, y, z);
            velocity.xCoord += var7.X;
            velocity.yCoord += var7.Y;
            velocity.zCoord += var7.Z;
        }

        public override int getTickRate()
        {
            return material == Material.WATER ? 5 : (material == Material.LAVA ? 30 : 0);
        }

        public override float getLuminance(BlockView blockView, int x, int y, int z)
        {
            float var5 = blockView.getLuminance(x, y, z);
            float var6 = blockView.getLuminance(x, y + 1, z);
            return var5 > var6 ? var5 : var6;
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            base.onTick(world, x, y, z, random);
        }

        public override int getRenderLayer()
        {
            return material == Material.WATER ? 1 : 0;
        }

        public override void randomDisplayTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (material == Material.WATER && random.nextInt(64) == 0)
            {
                int var6 = world.getBlockMeta(x, y, z);
                if (var6 > 0 && var6 < 8)
                {
                    world.playSound((double)((float)x + 0.5F), (double)((float)y + 0.5F), (double)((float)z + 0.5F), "liquid.water", random.nextFloat() * 0.25F + 12.0F / 16.0F, random.nextFloat() * 1.0F + 0.5F);
                }
            }

            if (material == Material.LAVA && world.getMaterial(x, y + 1, z) == Material.AIR && !world.isOpaque(x, y + 1, z) && random.nextInt(100) == 0)
            {
                double var12 = (double)((float)x + random.nextFloat());
                double var8 = (double)y + maxY;
                double var10 = (double)((float)z + random.nextFloat());
                world.addParticle("lava", var12, var8, var10, 0.0D, 0.0D, 0.0D);
            }

        }

        public static double getFlowingAngle(BlockView blockView, int x, int y, int z, Material material)
        {
            Vector3D<double> var5 = new(0.0);
            if (material == Material.WATER)
            {
                var5 = ((BlockFluid)FLOWING_WATER).getFlow(blockView, x, y, z);
            }

            if (material == Material.LAVA)
            {
                var5 = ((BlockFluid)FLOWING_LAVA).getFlow(blockView, x, y, z);
            }

            return var5.X == 0.0D && var5.Z == 0.0D ? -1000.0D : java.lang.Math.atan2(var5.Z, var5.X) - Math.PI * 0.5D;
        }

        public override void onPlaced(World world, int x, int y, int z)
        {
            checkBlockCollisions(world, x, y, z);
        }

        public override void neighborUpdate(World world, int x, int y, int z, int var5)
        {
            checkBlockCollisions(world, x, y, z);
        }

        private void checkBlockCollisions(World world, int x, int y, int z)
        {
            if (world.getBlockId(x, y, z) == id)
            {
                if (material == Material.LAVA)
                {
                    bool var5 = false;
                    if (var5 || world.getMaterial(x, y, z - 1) == Material.WATER)
                    {
                        var5 = true;
                    }

                    if (var5 || world.getMaterial(x, y, z + 1) == Material.WATER)
                    {
                        var5 = true;
                    }

                    if (var5 || world.getMaterial(x - 1, y, z) == Material.WATER)
                    {
                        var5 = true;
                    }

                    if (var5 || world.getMaterial(x + 1, y, z) == Material.WATER)
                    {
                        var5 = true;
                    }

                    if (var5 || world.getMaterial(x, y + 1, z) == Material.WATER)
                    {
                        var5 = true;
                    }

                    if (var5)
                    {
                        int var6 = world.getBlockMeta(x, y, z);
                        if (var6 == 0)
                        {
                            world.setBlockWithNotify(x, y, z, Block.OBSIDIAN.id);
                        }
                        else if (var6 <= 4)
                        {
                            world.setBlockWithNotify(x, y, z, Block.COBBLESTONE.id);
                        }

                        fizz(world, x, y, z);
                    }
                }

            }
        }

        protected void fizz(World world, int x, int y, int z)
        {
            world.playSound((double)((float)x + 0.5F), (double)((float)y + 0.5F), (double)((float)z + 0.5F), "random.fizz", 0.5F, 2.6F + (world.random.nextFloat() - world.random.nextFloat()) * 0.8F);

            for (int var5 = 0; var5 < 8; ++var5)
            {
                world.addParticle("largesmoke", (double)x + java.lang.Math.random(), (double)y + 1.2D, (double)z + java.lang.Math.random(), 0.0D, 0.0D, 0.0D);
            }

        }

        private static Vector3D<double> Normalize(Vector3D<double> vec)
        {
            double var1 = (double)MathHelper.sqrt_double(vec.X * vec.X + vec.Y * vec.Y + vec.Z * vec.Z);
            return var1 < 1.0E-4D ? new(0.0) : new(vec.X / var1, vec.Y / var1, vec.Z / var1);
        }
    }

}