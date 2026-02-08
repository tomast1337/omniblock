using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Items;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockLeaves : BlockLeavesBase
    {
        private int spriteIndex;
        int[] decayRegion;

        public BlockLeaves(int id, int textureId) : base(id, textureId, Material.LEAVES, false)
        {
            spriteIndex = textureId;
            setTickRandomly(true);
        }

        public override int getColor(int meta)
        {
            return (meta & 1) == 1 ? FoliageColors.getSpruceColor() : ((meta & 2) == 2 ? FoliageColors.getBirchColor() : FoliageColors.getDefaultColor());
        }

        public override int getColorMultiplier(BlockView blockView, int x, int y, int z)
        {
            int var5 = blockView.getBlockMeta(x, y, z);
            if ((var5 & 1) == 1)
            {
                return FoliageColors.getSpruceColor();
            }
            else if ((var5 & 2) == 2)
            {
                return FoliageColors.getBirchColor();
            }
            else
            {
                blockView.getBiomeSource().getBiomesInArea(x, z, 1, 1);
                double var6 = blockView.getBiomeSource().temperatureMap[0];
                double var8 = blockView.getBiomeSource().downfallMap[0];
                return FoliageColors.getFoliageColor(var6, var8);
            }
        }

        public override void onBreak(World world, int x, int y, int z)
        {
            sbyte var5 = 1;
            int var6 = var5 + 1;
            if (world.checkChunksExist(x - var6, y - var6, z - var6, x + var6, y + var6, z + var6))
            {
                for (int var7 = -var5; var7 <= var5; ++var7)
                {
                    for (int var8 = -var5; var8 <= var5; ++var8)
                    {
                        for (int var9 = -var5; var9 <= var5; ++var9)
                        {
                            int var10 = world.getBlockId(x + var7, y + var8, z + var9);
                            if (var10 == Block.LEAVES.id)
                            {
                                int var11 = world.getBlockMeta(x + var7, y + var8, z + var9);
                                world.setBlockMetadata(x + var7, y + var8, z + var9, var11 | 8);
                            }
                        }
                    }
                }
            }

        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (!world.isRemote)
            {
                int var6 = world.getBlockMeta(x, y, z);
                if ((var6 & 8) != 0)
                {
                    sbyte var7 = 4;
                    int var8 = var7 + 1;
                    sbyte var9 = 32;
                    int var10 = var9 * var9;
                    int var11 = var9 / 2;
                    if (decayRegion == null)
                    {
                        decayRegion = new int[var9 * var9 * var9];
                    }

                    int var12;
                    if (world.checkChunksExist(x - var8, y - var8, z - var8, x + var8, y + var8, z + var8))
                    {
                        var12 = -var7;

                        while (var12 <= var7)
                        {
                            int var13;
                            int var14;
                            int var15;

                            for (var13 = -var7; var13 <= var7; ++var13)
                            {
                                for (var14 = -var7; var14 <= var7; ++var14)
                                {
                                    var15 = world.getBlockId(x + var12, y + var13, z + var14);
                                    if (var15 == Block.LOG.id)
                                    {
                                        decayRegion[(var12 + var11) * var10 + (var13 + var11) * var9 + var14 + var11] = 0;
                                    }
                                    else if (var15 == Block.LEAVES.id)
                                    {
                                        decayRegion[(var12 + var11) * var10 + (var13 + var11) * var9 + var14 + var11] = -2;
                                    }
                                    else
                                    {
                                        decayRegion[(var12 + var11) * var10 + (var13 + var11) * var9 + var14 + var11] = -1;
                                    }
                                }
                            }

                            ++var12;
                        }

                        for (var12 = 1; var12 <= 4; ++var12)
                        {
                            int var13;
                            int var14;
                            int var15;

                            for (var13 = -var7; var13 <= var7; ++var13)
                            {
                                for (var14 = -var7; var14 <= var7; ++var14)
                                {
                                    for (var15 = -var7; var15 <= var7; ++var15)
                                    {
                                        if (decayRegion[(var13 + var11) * var10 + (var14 + var11) * var9 + var15 + var11] == var12 - 1)
                                        {
                                            if (decayRegion[(var13 + var11 - 1) * var10 + (var14 + var11) * var9 + var15 + var11] == -2)
                                            {
                                                decayRegion[(var13 + var11 - 1) * var10 + (var14 + var11) * var9 + var15 + var11] = var12;
                                            }

                                            if (decayRegion[(var13 + var11 + 1) * var10 + (var14 + var11) * var9 + var15 + var11] == -2)
                                            {
                                                decayRegion[(var13 + var11 + 1) * var10 + (var14 + var11) * var9 + var15 + var11] = var12;
                                            }

                                            if (decayRegion[(var13 + var11) * var10 + (var14 + var11 - 1) * var9 + var15 + var11] == -2)
                                            {
                                                decayRegion[(var13 + var11) * var10 + (var14 + var11 - 1) * var9 + var15 + var11] = var12;
                                            }

                                            if (decayRegion[(var13 + var11) * var10 + (var14 + var11 + 1) * var9 + var15 + var11] == -2)
                                            {
                                                decayRegion[(var13 + var11) * var10 + (var14 + var11 + 1) * var9 + var15 + var11] = var12;
                                            }

                                            if (decayRegion[(var13 + var11) * var10 + (var14 + var11) * var9 + (var15 + var11 - 1)] == -2)
                                            {
                                                decayRegion[(var13 + var11) * var10 + (var14 + var11) * var9 + (var15 + var11 - 1)] = var12;
                                            }

                                            if (decayRegion[(var13 + var11) * var10 + (var14 + var11) * var9 + var15 + var11 + 1] == -2)
                                            {
                                                decayRegion[(var13 + var11) * var10 + (var14 + var11) * var9 + var15 + var11 + 1] = var12;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    var12 = decayRegion[var11 * var10 + var11 * var9 + var11];
                    if (var12 >= 0)
                    {
                        world.setBlockMetadata(x, y, z, var6 & -9);
                    }
                    else
                    {
                        breakLeaves(world, x, y, z);
                    }
                }

            }
        }

        private void breakLeaves(World world, int x, int y, int z)
        {
            dropStacks(world, x, y, z, world.getBlockMeta(x, y, z));
            world.setBlockWithNotify(x, y, z, 0);
        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return random.nextInt(20) == 0 ? 1 : 0;
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Block.SAPLING.id;
        }

        public override void afterBreak(World world, EntityPlayer player, int x, int y, int z, int meta)
        {
            if (!world.isRemote && player.getCurrentEquippedItem() != null && player.getCurrentEquippedItem().itemID == Item.shears.id)
            {
                player.increaseStat(Stats.Stats.mineBlockStatArray[id], 1);
                dropStack(world, x, y, z, new ItemStack(Block.LEAVES.id, 1, meta & 3));
            }
            else
            {
                base.afterBreak(world, player, x, y, z, meta);
            }

        }

        protected override int getDroppedItemMeta(int blockMeta)
        {
            return blockMeta & 3;
        }

        public override bool isOpaque()
        {
            return !graphicsLevel;
        }

        public override int getTexture(int side, int meta)
        {
            return (meta & 3) == 1 ? textureId + 80 : textureId;
        }

        public void setGraphicsLevel(bool bl)
        {
            graphicsLevel = bl;
            textureId = spriteIndex + (bl ? 0 : 1);
        }

        public override void onSteppedOn(World world, int x, int y, int z, Entity entity)
        {
            base.onSteppedOn(world, x, y, z, entity);
        }
    }

}