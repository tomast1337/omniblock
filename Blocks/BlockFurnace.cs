using betareborn.Entities;
using betareborn.Items;
using betareborn.Materials;
using betareborn.TileEntities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockFurnace : BlockContainer
    {

        private java.util.Random random = new();
        private readonly bool lit;
        private static bool ignoreBlockRemoval = false;

        public BlockFurnace(int id, bool lit) : base(id, Material.STONE)
        {
            this.lit = lit;
            textureId = 45;
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Block.FURNACE.id;
        }

        public override void onPlaced(World world, int x, int y, int z)
        {
            base.onPlaced(world, x, y, z);
            updateDirection(world, x, y, z);
        }

        private void updateDirection(World world, int x, int y, int z)
        {
            if (!world.isRemote)
            {
                int var5 = world.getBlockId(x, y, z - 1);
                int var6 = world.getBlockId(x, y, z + 1);
                int var7 = world.getBlockId(x - 1, y, z);
                int var8 = world.getBlockId(x + 1, y, z);
                sbyte var9 = 3;
                if (Block.BLOCKS_OPAQUE[var5] && !Block.BLOCKS_OPAQUE[var6])
                {
                    var9 = 3;
                }

                if (Block.BLOCKS_OPAQUE[var6] && !Block.BLOCKS_OPAQUE[var5])
                {
                    var9 = 2;
                }

                if (Block.BLOCKS_OPAQUE[var7] && !Block.BLOCKS_OPAQUE[var8])
                {
                    var9 = 5;
                }

                if (Block.BLOCKS_OPAQUE[var8] && !Block.BLOCKS_OPAQUE[var7])
                {
                    var9 = 4;
                }

                world.setBlockMeta(x, y, z, var9);
            }
        }

        public override int getTextureId(BlockView blockView, int x, int y, int z, int side)
        {
            if (side == 1)
            {
                return textureId + 17;
            }
            else if (side == 0)
            {
                return textureId + 17;
            }
            else
            {
                int var6 = blockView.getBlockMeta(x, y, z);
                return side != var6 ? textureId : (lit ? textureId + 16 : textureId - 1);
            }
        }

        public override void randomDisplayTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (lit)
            {
                int var6 = world.getBlockMeta(x, y, z);
                float var7 = (float)x + 0.5F;
                float var8 = (float)y + 0.0F + random.nextFloat() * 6.0F / 16.0F;
                float var9 = (float)z + 0.5F;
                float var10 = 0.52F;
                float var11 = random.nextFloat() * 0.6F - 0.3F;
                if (var6 == 4)
                {
                    world.addParticle("smoke", (double)(var7 - var10), (double)var8, (double)(var9 + var11), 0.0D, 0.0D, 0.0D);
                    world.addParticle("flame", (double)(var7 - var10), (double)var8, (double)(var9 + var11), 0.0D, 0.0D, 0.0D);
                }
                else if (var6 == 5)
                {
                    world.addParticle("smoke", (double)(var7 + var10), (double)var8, (double)(var9 + var11), 0.0D, 0.0D, 0.0D);
                    world.addParticle("flame", (double)(var7 + var10), (double)var8, (double)(var9 + var11), 0.0D, 0.0D, 0.0D);
                }
                else if (var6 == 2)
                {
                    world.addParticle("smoke", (double)(var7 + var11), (double)var8, (double)(var9 - var10), 0.0D, 0.0D, 0.0D);
                    world.addParticle("flame", (double)(var7 + var11), (double)var8, (double)(var9 - var10), 0.0D, 0.0D, 0.0D);
                }
                else if (var6 == 3)
                {
                    world.addParticle("smoke", (double)(var7 + var11), (double)var8, (double)(var9 + var10), 0.0D, 0.0D, 0.0D);
                    world.addParticle("flame", (double)(var7 + var11), (double)var8, (double)(var9 + var10), 0.0D, 0.0D, 0.0D);
                }

            }
        }

        public override int getTexture(int side)
        {
            return side == 1 ? textureId + 17 : (side == 0 ? textureId + 17 : (side == 3 ? textureId - 1 : textureId));
        }

        public override bool onUse(World world, int x, int y, int z, EntityPlayer player)
        {
            if (world.isRemote)
            {
                return true;
            }
            else
            {
                TileEntityFurnace var6 = (TileEntityFurnace)world.getBlockTileEntity(x, y, z);
                player.displayGUIFurnace(var6);
                return true;
            }
        }

        public static void updateLitState(bool lit, World world, int x, int y, int z)
        {
            int var5 = world.getBlockMeta(x, y, z);
            TileEntity var6 = world.getBlockTileEntity(x, y, z);
            ignoreBlockRemoval = true;
            if (lit)
            {
                world.setBlockWithNotify(x, y, z, Block.LIT_FURNACE.id);
            }
            else
            {
                world.setBlockWithNotify(x, y, z, Block.FURNACE.id);
            }

            ignoreBlockRemoval = false;
            world.setBlockMeta(x, y, z, var5);
            var6.cancelRemoval();
            world.setBlockTileEntity(x, y, z, var6);
        }

        protected override TileEntity getBlockEntity()
        {
            return new TileEntityFurnace();
        }

        public override void onPlaced(World world, int x, int y, int z, EntityLiving placer)
        {
            int var6 = MathHelper.floor_double((double)(placer.rotationYaw * 4.0F / 360.0F) + 0.5D) & 3;
            if (var6 == 0)
            {
                world.setBlockMeta(x, y, z, 2);
            }

            if (var6 == 1)
            {
                world.setBlockMeta(x, y, z, 5);
            }

            if (var6 == 2)
            {
                world.setBlockMeta(x, y, z, 3);
            }

            if (var6 == 3)
            {
                world.setBlockMeta(x, y, z, 4);
            }

        }

        public override void onBreak(World world, int x, int y, int z)
        {
            if (!ignoreBlockRemoval)
            {
                TileEntityFurnace var5 = (TileEntityFurnace)world.getBlockTileEntity(x, y, z);

                for (int var6 = 0; var6 < var5.size(); ++var6)
                {
                    ItemStack var7 = var5.getStack(var6);
                    if (var7 != null)
                    {
                        float var8 = random.nextFloat() * 0.8F + 0.1F;
                        float var9 = random.nextFloat() * 0.8F + 0.1F;
                        float var10 = random.nextFloat() * 0.8F + 0.1F;

                        while (var7.count > 0)
                        {
                            int var11 = random.nextInt(21) + 10;
                            if (var11 > var7.count)
                            {
                                var11 = var7.count;
                            }

                            var7.count -= var11;
                            EntityItem var12 = new EntityItem(world, (double)((float)x + var8), (double)((float)y + var9), (double)((float)z + var10), new ItemStack(var7.itemID, var11, var7.getItemDamage()));
                            float var13 = 0.05F;
                            var12.motionX = (double)((float)random.nextGaussian() * var13);
                            var12.motionY = (double)((float)random.nextGaussian() * var13 + 0.2F);
                            var12.motionZ = (double)((float)random.nextGaussian() * var13);
                            world.spawnEntity(var12);
                        }
                    }
                }
            }

            base.onBreak(world, x, y, z);
        }
    }
}