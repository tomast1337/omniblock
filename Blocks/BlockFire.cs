using betareborn.Blocks.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockFire : Block
    {
        private int[] burnChances = new int[256];
        private int[] spreadChances = new int[256];

        public BlockFire(int id, int textureId) : base(id, textureId, Material.FIRE)
        {
            setTickRandomly(true);
        }

        protected override void init()
        {
            registerFlammableBlock(Block.PLANKS.id, 5, 20);
            registerFlammableBlock(Block.FENCE.id, 5, 20);
            registerFlammableBlock(Block.WOODEN_STAIRS.id, 5, 20);
            registerFlammableBlock(Block.LOG.id, 5, 5);
            registerFlammableBlock(Block.LEAVES.id, 30, 60);
            registerFlammableBlock(Block.BOOKSHELF.id, 30, 20);
            registerFlammableBlock(Block.TNT.id, 15, 100);
            registerFlammableBlock(Block.GRASS.id, 60, 100);
            registerFlammableBlock(Block.WOOL.id, 30, 60);
        }

        private void registerFlammableBlock(int block, int burnChange, int spreadChance)
        {
            burnChances[block] = burnChange;
            spreadChances[block] = spreadChance;
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            return null;
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
            return 3;
        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return 0;
        }

        public override int getTickRate()
        {
            return 40;
        }

        public override void onTick(World world, int x, int y, int z, java.util.Random random)
        {
            bool var6 = world.getBlockId(x, y - 1, z) == Block.NETHERRACK.id;
            if (!canPlaceAt(world, x, y, z))
            {
                world.setBlockWithNotify(x, y, z, 0);
            }

            if (var6 || !world.isRaining() || !world.isRaining(x, y, z) && !world.isRaining(x - 1, y, z) && !world.isRaining(x + 1, y, z) && !world.isRaining(x, y, z - 1) && !world.isRaining(x, y, z + 1))
            {
                int var7 = world.getBlockMeta(x, y, z);
                if (var7 < 15)
                {
                    world.setBlockMetadata(x, y, z, var7 + random.nextInt(3) / 2);
                }

                world.scheduleBlockUpdate(x, y, z, id, getTickRate());
                if (!var6 && !areBlocksAroundFlammable(world, x, y, z))
                {
                    if (!world.shouldSuffocate(x, y - 1, z) || var7 > 3)
                    {
                        world.setBlockWithNotify(x, y, z, 0);
                    }

                }
                else if (!var6 && !isFlammable(world, x, y - 1, z) && var7 == 15 && random.nextInt(4) == 0)
                {
                    world.setBlockWithNotify(x, y, z, 0);
                }
                else
                {
                    trySpreadingFire(world, x + 1, y, z, 300, random, var7);
                    trySpreadingFire(world, x - 1, y, z, 300, random, var7);
                    trySpreadingFire(world, x, y - 1, z, 250, random, var7);
                    trySpreadingFire(world, x, y + 1, z, 250, random, var7);
                    trySpreadingFire(world, x, y, z - 1, 300, random, var7);
                    trySpreadingFire(world, x, y, z + 1, 300, random, var7);

                    for (int var8 = x - 1; var8 <= x + 1; ++var8)
                    {
                        for (int var9 = z - 1; var9 <= z + 1; ++var9)
                        {
                            for (int var10 = y - 1; var10 <= y + 4; ++var10)
                            {
                                if (var8 != x || var10 != y || var9 != z)
                                {
                                    int var11 = 100;
                                    if (var10 > y + 1)
                                    {
                                        var11 += (var10 - (y + 1)) * 100;
                                    }

                                    int var12 = getBurnChance(world, var8, var10, var9);
                                    if (var12 > 0)
                                    {
                                        int var13 = (var12 + 40) / (var7 + 30);
                                        if (var13 > 0 && random.nextInt(var11) <= var13 && (!world.isRaining() || !world.isRaining(var8, var10, var9)) && !world.isRaining(var8 - 1, var10, z) && !world.isRaining(var8 + 1, var10, var9) && !world.isRaining(var8, var10, var9 - 1) && !world.isRaining(var8, var10, var9 + 1))
                                        {
                                            int var14 = var7 + random.nextInt(5) / 4;
                                            if (var14 > 15)
                                            {
                                                var14 = 15;
                                            }

                                            world.setBlockAndMetadataWithNotify(var8, var10, var9, id, var14);
                                        }
                                    }
                                }
                            }
                        }
                    }

                }
            }
            else
            {
                world.setBlockWithNotify(x, y, z, 0);
            }
        }

        private void trySpreadingFire(World world, int x, int y, int z, int spreadFactor, java.util.Random random, int currentAge)
        {
            int var8 = spreadChances[world.getBlockId(x, y, z)];
            if (random.nextInt(spreadFactor) < var8)
            {
                bool var9 = world.getBlockId(x, y, z) == Block.TNT.id;
                if (random.nextInt(currentAge + 10) < 5 && !world.isRaining(x, y, z))
                {
                    int var10 = currentAge + random.nextInt(5) / 4;
                    if (var10 > 15)
                    {
                        var10 = 15;
                    }

                    world.setBlockAndMetadataWithNotify(x, y, z, id, var10);
                }
                else
                {
                    world.setBlockWithNotify(x, y, z, 0);
                }

                if (var9)
                {
                    Block.TNT.onMetadataChange(world, x, y, z, 1);
                }
            }

        }

        private bool areBlocksAroundFlammable(World world, int x, int y, int z)
        {
            return isFlammable(world, x + 1, y, z) ? true : (isFlammable(world, x - 1, y, z) ? true : (isFlammable(world, x, y - 1, z) ? true : (isFlammable(world, x, y + 1, z) ? true : (isFlammable(world, x, y, z - 1) ? true : isFlammable(world, x, y, z + 1)))));
        }

        private int getBurnChance(World world, int x, int y, int z)
        {
            sbyte var5 = 0;
            if (!world.isAir(x, y, z))
            {
                return 0;
            }
            else
            {
                int var6 = getBurnChance(world, x + 1, y, z, var5);
                var6 = getBurnChance(world, x - 1, y, z, var6);
                var6 = getBurnChance(world, x, y - 1, z, var6);
                var6 = getBurnChance(world, x, y + 1, z, var6);
                var6 = getBurnChance(world, x, y, z - 1, var6);
                var6 = getBurnChance(world, x, y, z + 1, var6);
                return var6;
            }
        }

        public override bool hasCollision()
        {
            return false;
        }

        public bool isFlammable(BlockView blockView, int x, int y, int z)
        {
            return burnChances[blockView.getBlockId(x, y, z)] > 0;
        }

        public int getBurnChance(World world, int x, int y, int z, int currentChance)
        {
            int var6 = burnChances[world.getBlockId(x, y, z)];
            return var6 > currentChance ? var6 : currentChance;
        }

        public override bool canPlaceAt(World world, int x, int y, int z)
        {
            return world.shouldSuffocate(x, y - 1, z) || areBlocksAroundFlammable(world, x, y, z);
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            if (!world.shouldSuffocate(x, y - 1, z) && !areBlocksAroundFlammable(world, x, y, z))
            {
                world.setBlockWithNotify(x, y, z, 0);
            }
        }

        public override void onPlaced(World world, int x, int y, int z)
        {
            if (world.getBlockId(x, y - 1, z) != Block.OBSIDIAN.id || !Block.NETHER_PORTAL.create(world, x, y, z))
            {
                if (!world.shouldSuffocate(x, y - 1, z) && !areBlocksAroundFlammable(world, x, y, z))
                {
                    world.setBlockWithNotify(x, y, z, 0);
                }
                else
                {
                    world.scheduleBlockUpdate(x, y, z, id, getTickRate());
                }
            }
        }

        public override void randomDisplayTick(World world, int x, int y, int z, java.util.Random random)
        {
            if (random.nextInt(24) == 0)
            {
                world.playSound((double)((float)x + 0.5F), (double)((float)y + 0.5F), (double)((float)z + 0.5F), "fire.fire", 1.0F + random.nextFloat(), random.nextFloat() * 0.7F + 0.3F);
            }

            int var6;
            float var7;
            float var8;
            float var9;
            if (!world.shouldSuffocate(x, y - 1, z) && !Block.FIRE.isFlammable(world, x, y - 1, z))
            {
                if (Block.FIRE.isFlammable(world, x - 1, y, z))
                {
                    for (var6 = 0; var6 < 2; ++var6)
                    {
                        var7 = (float)x + random.nextFloat() * 0.1F;
                        var8 = (float)y + random.nextFloat();
                        var9 = (float)z + random.nextFloat();
                        world.addParticle("largesmoke", (double)var7, (double)var8, (double)var9, 0.0D, 0.0D, 0.0D);
                    }
                }

                if (Block.FIRE.isFlammable(world, x + 1, y, z))
                {
                    for (var6 = 0; var6 < 2; ++var6)
                    {
                        var7 = (float)(x + 1) - random.nextFloat() * 0.1F;
                        var8 = (float)y + random.nextFloat();
                        var9 = (float)z + random.nextFloat();
                        world.addParticle("largesmoke", (double)var7, (double)var8, (double)var9, 0.0D, 0.0D, 0.0D);
                    }
                }

                if (Block.FIRE.isFlammable(world, x, y, z - 1))
                {
                    for (var6 = 0; var6 < 2; ++var6)
                    {
                        var7 = (float)x + random.nextFloat();
                        var8 = (float)y + random.nextFloat();
                        var9 = (float)z + random.nextFloat() * 0.1F;
                        world.addParticle("largesmoke", (double)var7, (double)var8, (double)var9, 0.0D, 0.0D, 0.0D);
                    }
                }

                if (Block.FIRE.isFlammable(world, x, y, z + 1))
                {
                    for (var6 = 0; var6 < 2; ++var6)
                    {
                        var7 = (float)x + random.nextFloat();
                        var8 = (float)y + random.nextFloat();
                        var9 = (float)(z + 1) - random.nextFloat() * 0.1F;
                        world.addParticle("largesmoke", (double)var7, (double)var8, (double)var9, 0.0D, 0.0D, 0.0D);
                    }
                }

                if (Block.FIRE.isFlammable(world, x, y + 1, z))
                {
                    for (var6 = 0; var6 < 2; ++var6)
                    {
                        var7 = (float)x + random.nextFloat();
                        var8 = (float)(y + 1) - random.nextFloat() * 0.1F;
                        var9 = (float)z + random.nextFloat();
                        world.addParticle("largesmoke", (double)var7, (double)var8, (double)var9, 0.0D, 0.0D, 0.0D);
                    }
                }
            }
            else
            {
                for (var6 = 0; var6 < 3; ++var6)
                {
                    var7 = (float)x + random.nextFloat();
                    var8 = (float)y + random.nextFloat() * 0.5F + 0.5F;
                    var9 = (float)z + random.nextFloat();
                    world.addParticle("largesmoke", (double)var7, (double)var8, (double)var9, 0.0D, 0.0D, 0.0D);
                }
            }

        }
    }

}