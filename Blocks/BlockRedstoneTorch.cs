using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockRedstoneTorch : BlockTorch
    {

        private bool lit = false;
        private static List<RedstoneUpdateInfo> torchUpdates = [];

        public override int getTexture(int side, int meta)
        {
            return side == 1 ? Block.REDSTONE_WIRE.getTexture(side, meta) : base.getTexture(side, meta);
        }

        private bool isBurnedOut(World var1, int var2, int var3, int var4, bool var5)
        {
            if (var5)
            {
                torchUpdates.Add(new RedstoneUpdateInfo(var2, var3, var4, var1.getTime()));
            }

            int var6 = 0;

            for (int var7 = 0; var7 < torchUpdates.Capacity; ++var7)
            {
                RedstoneUpdateInfo var8 = torchUpdates[var7];
                if (var8.x == var2 && var8.y == var3 && var8.z == var4)
                {
                    ++var6;
                    if (var6 >= 8)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public BlockRedstoneTorch(int id, int textureId, bool lit) : base(id, textureId)
        {
            this.lit = lit;
            setTickRandomly(true);
        }

        public override int getTickRate()
        {
            return 2;
        }

        public override void onPlaced(World world, int x, int y, int z)
        {
            if (world.getBlockMeta(x, y, z) == 0)
            {
                base.onPlaced(world, x, y, z);
            }

            if (lit)
            {
                world.notifyNeighbors(x, y - 1, z, id);
                world.notifyNeighbors(x, y + 1, z, id);
                world.notifyNeighbors(x - 1, y, z, id);
                world.notifyNeighbors(x + 1, y, z, id);
                world.notifyNeighbors(x, y, z - 1, id);
                world.notifyNeighbors(x, y, z + 1, id);
            }

        }

        public override void onBreak(World world, int x, int y, int z)
        {
            if (lit)
            {
                world.notifyNeighbors(x, y - 1, z, id);
                world.notifyNeighbors(x, y + 1, z, id);
                world.notifyNeighbors(x - 1, y, z, id);
                world.notifyNeighbors(x + 1, y, z, id);
                world.notifyNeighbors(x, y, z - 1, id);
                world.notifyNeighbors(x, y, z + 1, id);
            }

        }

        public override bool isPoweringSide(BlockView blockView, int x, int y, int z, int side)
        {
            if (!lit)
            {
                return false;
            }
            else
            {
                int var6 = blockView.getBlockMeta(x, y, z);
                return var6 == 5 && side == 1 ? false : (var6 == 3 && side == 3 ? false : (var6 == 4 && side == 2 ? false : (var6 == 1 && side == 5 ? false : var6 != 2 || side != 4)));
            }
        }

        private bool shouldUnpower(World world, int x, int y, int z)
        {
            int var5 = world.getBlockMeta(x, y, z);
            return var5 == 5 && world.isPoweringSide(x, y - 1, z, 0) ? true : (var5 == 3 && world.isPoweringSide(x, y, z - 1, 2) ? true : (var5 == 4 && world.isPoweringSide(x, y, z + 1, 3) ? true : (var5 == 1 && world.isPoweringSide(x - 1, y, z, 4) ? true : var5 == 2 && world.isPoweringSide(x + 1, y, z, 5))));
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            bool var6 = shouldUnpower(world, x, y, z);

            while (torchUpdates.Count > 0 && world.getTime() - torchUpdates[0].updateTime > 100L)
            {
                torchUpdates.RemoveAt(0);
            }

            if (lit)
            {
                if (var6)
                {
                    world.setBlock(x, y, z, Block.REDSTONE_TORCH.id, world.getBlockMeta(x, y, z));
                    if (isBurnedOut(world, x, y, z, true))
                    {
                        world.playSound((double)((float)x + 0.5F), (double)((float)y + 0.5F), (double)((float)z + 0.5F), "random.fizz", 0.5F, 2.6F + (world.random.nextFloat() - world.random.nextFloat()) * 0.8F);

                        for (int var7 = 0; var7 < 5; ++var7)
                        {
                            double var8 = (double)x + random.nextDouble() * 0.6D + 0.2D;
                            double var10 = (double)y + random.nextDouble() * 0.6D + 0.2D;
                            double var12 = (double)z + random.nextDouble() * 0.6D + 0.2D;
                            world.addParticle("smoke", var8, var10, var12, 0.0D, 0.0D, 0.0D);
                        }
                    }
                }
            }
            else if (!var6 && !isBurnedOut(world, x, y, z, false))
            {
                world.setBlock(x, y, z, Block.LIT_REDSTONE_TORCH.id, world.getBlockMeta(x, y, z));
            }

        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            base.neighborUpdate(world, x, y, z, id);
            world.scheduleBlockUpdate(x, y, z, base.id, getTickRate());
        }

        public override bool isStrongPoweringSide(World world, int x, int y, int z, int side)
        {
            return side == 0 ? isPoweringSide(world, x, y, z, side) : false;
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Block.LIT_REDSTONE_TORCH.id;
        }

        public override bool canEmitRedstonePower()
        {
            return true;
        }

        public override void randomDisplayTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (lit)
            {
                int var6 = world.getBlockMeta(x, y, z);
                double var7 = (double)((float)x + 0.5F) + (double)(random.nextFloat() - 0.5F) * 0.2D;
                double var9 = (double)((float)y + 0.7F) + (double)(random.nextFloat() - 0.5F) * 0.2D;
                double var11 = (double)((float)z + 0.5F) + (double)(random.nextFloat() - 0.5F) * 0.2D;
                double var13 = (double)0.22F;
                double var15 = (double)0.27F;
                if (var6 == 1)
                {
                    world.addParticle("reddust", var7 - var15, var9 + var13, var11, 0.0D, 0.0D, 0.0D);
                }
                else if (var6 == 2)
                {
                    world.addParticle("reddust", var7 + var15, var9 + var13, var11, 0.0D, 0.0D, 0.0D);
                }
                else if (var6 == 3)
                {
                    world.addParticle("reddust", var7, var9 + var13, var11 - var15, 0.0D, 0.0D, 0.0D);
                }
                else if (var6 == 4)
                {
                    world.addParticle("reddust", var7, var9 + var13, var11 + var15, 0.0D, 0.0D, 0.0D);
                }
                else
                {
                    world.addParticle("reddust", var7, var9, var11, 0.0D, 0.0D, 0.0D);
                }

            }
        }
    }

}