using betareborn.Entities;
using betareborn.Items;
using betareborn.Materials;
using betareborn.TileEntities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockDispenser : BlockContainer
    {
        private java.util.Random random = new();

        public BlockDispenser(int id) : base(id, Material.STONE)
        {
            textureId = 45;
        }

        public override int getTickRate()
        {
            return 4;
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Block.DISPENSER.id;
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
                return side != var6 ? textureId : textureId + 1;
            }
        }

        public override int getTexture(int side)
        {
            return side == 1 ? textureId + 17 : (side == 0 ? textureId + 17 : (side == 3 ? textureId + 1 : textureId));
        }

        public override bool onUse(World world, int x, int y, int z, EntityPlayer player)
        {
            if (world.isRemote)
            {
                return true;
            }
            else
            {
                TileEntityDispenser var6 = (TileEntityDispenser)world.getBlockTileEntity(x, y, z);
                player.displayGUIDispenser(var6);
                return true;
            }
        }

        private void dispense(World world, int x, int y, int z, java.util.Random random)
        {
            int var6 = world.getBlockMeta(x, y, z);
            int var9 = 0;
            int var10 = 0;
            if (var6 == 3)
            {
                var10 = 1;
            }
            else if (var6 == 2)
            {
                var10 = -1;
            }
            else if (var6 == 5)
            {
                var9 = 1;
            }
            else
            {
                var9 = -1;
            }

            TileEntityDispenser var11 = (TileEntityDispenser)world.getBlockTileEntity(x, y, z);
            ItemStack var12 = var11.getItemToDispose();
            double var13 = (double)x + (double)var9 * 0.6D + 0.5D;
            double var15 = (double)y + 0.5D;
            double var17 = (double)z + (double)var10 * 0.6D + 0.5D;
            if (var12 == null)
            {
                world.worldEvent(1001, x, y, z, 0);
            }
            else
            {
                if (var12.itemID == Item.arrow.id)
                {
                    EntityArrow var19 = new EntityArrow(world, var13, var15, var17);
                    var19.setArrowHeading((double)var9, (double)0.1F, (double)var10, 1.1F, 6.0F);
                    var19.doesArrowBelongToPlayer = true;
                    world.spawnEntity(var19);
                    world.worldEvent(1002, x, y, z, 0);
                }
                else if (var12.itemID == Item.egg.id)
                {
                    EntityEgg var22 = new EntityEgg(world, var13, var15, var17);
                    var22.setEggHeading((double)var9, (double)0.1F, (double)var10, 1.1F, 6.0F);
                    world.spawnEntity(var22);
                    world.worldEvent(1002, x, y, z, 0);
                }
                else if (var12.itemID == Item.snowball.id)
                {
                    EntitySnowball var23 = new EntitySnowball(world, var13, var15, var17);
                    var23.setSnowballHeading((double)var9, (double)0.1F, (double)var10, 1.1F, 6.0F);
                    world.spawnEntity(var23);
                    world.worldEvent(1002, x, y, z, 0);
                }
                else
                {
                    EntityItem var24 = new EntityItem(world, var13, var15 - 0.3D, var17, var12);
                    double var20 = random.nextDouble() * 0.1D + 0.2D;
                    var24.motionX = (double)var9 * var20;
                    var24.motionY = (double)0.2F;
                    var24.motionZ = (double)var10 * var20;
                    var24.motionX += random.nextGaussian() * (double)0.0075F * 6.0D;
                    var24.motionY += random.nextGaussian() * (double)0.0075F * 6.0D;
                    var24.motionZ += random.nextGaussian() * (double)0.0075F * 6.0D;
                    world.spawnEntity(var24);
                    world.worldEvent(1000, x, y, z, 0);
                }

                world.worldEvent(2000, x, y, z, var9 + 1 + (var10 + 1) * 3);
            }

        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            if (id > 0 && Block.BLOCKS[id].canEmitRedstonePower())
            {
                bool var6 = world.isPowered(x, y, z) || world.isPowered(x, y + 1, z);
                if (var6)
                {
                    world.scheduleBlockUpdate(x, y, z, base.id, getTickRate());
                }
            }

        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (world.isPowered(x, y, z) || world.isPowered(x, y + 1, z))
            {
                dispense(world, x, y, z, random);
            }

        }

        protected override TileEntity getBlockEntity()
        {
            return new TileEntityDispenser();
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
            TileEntityDispenser var5 = (TileEntityDispenser)world.getBlockTileEntity(x, y, z);

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

            base.onBreak(world, x, y, z);
        }
    }

}