using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Items;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockDoor : Block
    {
        public BlockDoor(int id, Material material) : base(id, material)
        {
            textureId = 97;
            if (material == Material.METAL)
            {
                ++textureId;
            }

            float var3 = 0.5F;
            float var4 = 1.0F;
            setBoundingBox(0.5F - var3, 0.0F, 0.5F - var3, 0.5F + var3, var4, 0.5F + var3);
        }

        public override int getTexture(int side, int meta)
        {
            if (side != 0 && side != 1)
            {
                int var3 = setOpen(meta);
                if ((var3 == 0 || var3 == 2) ^ side <= 3)
                {
                    return textureId;
                }
                else
                {
                    int var4 = var3 / 2 + (side & 1 ^ var3);
                    var4 += (meta & 4) / 4;
                    int var5 = textureId - (meta & 8) * 2;
                    if ((var4 & 1) != 0)
                    {
                        var5 = -var5;
                    }

                    return var5;
                }
            }
            else
            {
                return textureId;
            }
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
            return 7;
        }

        public override Box getBoundingBox(World world, int x, int y, int z)
        {
            updateBoundingBox(world, x, y, z);
            return base.getBoundingBox(world, x, y, z);
        }

        public override Box getCollisionShape(World world, int x, int y, int z)
        {
            updateBoundingBox(world, x, y, z);
            return base.getCollisionShape(world, x, y, z);
        }

        public override void updateBoundingBox(BlockView blockView, int x, int y, int z)
        {
            rotate(setOpen(blockView.getBlockMeta(x, y, z)));
        }

        public void rotate(int meta)
        {
            float var2 = 3.0F / 16.0F;
            setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 2.0F, 1.0F);
            if (meta == 0)
            {
                setBoundingBox(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, var2);
            }

            if (meta == 1)
            {
                setBoundingBox(1.0F - var2, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
            }

            if (meta == 2)
            {
                setBoundingBox(0.0F, 0.0F, 1.0F - var2, 1.0F, 1.0F, 1.0F);
            }

            if (meta == 3)
            {
                setBoundingBox(0.0F, 0.0F, 0.0F, var2, 1.0F, 1.0F);
            }

        }

        public override void onBlockBreakStart(World world, int x, int y, int z, EntityPlayer var5)
        {
            onUse(world, x, y, z, var5);
        }

        public override bool onUse(World var1, int var2, int var3, int var4, EntityPlayer var5)
        {
            if (material == Material.METAL)
            {
                return true;
            }
            else
            {
                int var6 = var1.getBlockMeta(var2, var3, var4);
                if ((var6 & 8) != 0)
                {
                    if (var1.getBlockId(var2, var3 - 1, var4) == id)
                    {
                        onUse(var1, var2, var3 - 1, var4, var5);
                    }

                    return true;
                }
                else
                {
                    if (var1.getBlockId(var2, var3 + 1, var4) == id)
                    {
                        var1.setBlockMeta(var2, var3 + 1, var4, (var6 ^ 4) + 8);
                    }

                    var1.setBlockMeta(var2, var3, var4, var6 ^ 4);
                    var1.setBlocksDirty(var2, var3 - 1, var4, var2, var3, var4);
                    var1.worldEvent(var5, 1003, var2, var3, var4, 0);
                    return true;
                }
            }
        }

        public void setOpen(World world, int x, int y, int z, bool open)
        {
            int var6 = world.getBlockMeta(x, y, z);
            if ((var6 & 8) != 0)
            {
                if (world.getBlockId(x, y - 1, z) == id)
                {
                    setOpen(world, x, y - 1, z, open);
                }

            }
            else
            {
                bool var7 = (world.getBlockMeta(x, y, z) & 4) > 0;
                if (var7 != open)
                {
                    if (world.getBlockId(x, y + 1, z) == id)
                    {
                        world.setBlockMeta(x, y + 1, z, (var6 ^ 4) + 8);
                    }

                    world.setBlockMeta(x, y, z, var6 ^ 4);
                    world.setBlocksDirty(x, y - 1, z, x, y, z);
                    world.worldEvent((EntityPlayer)null, 1003, x, y, z, 0);
                }
            }
        }

        public override void neighborUpdate(World world, int x, int y, int z, int id)
        {
            int var6 = world.getBlockMeta(x, y, z);
            if ((var6 & 8) != 0)
            {
                if (world.getBlockId(x, y - 1, z) != base.id)
                {
                    world.setBlockWithNotify(x, y, z, 0);
                }

                if (id > 0 && Block.BLOCKS[id].canEmitRedstonePower())
                {
                    neighborUpdate(world, x, y - 1, z, id);
                }
            }
            else
            {
                bool var7 = false;
                if (world.getBlockId(x, y + 1, z) != base.id)
                {
                    world.setBlockWithNotify(x, y, z, 0);
                    var7 = true;
                }

                if (!world.shouldSuffocate(x, y - 1, z))
                {
                    world.setBlockWithNotify(x, y, z, 0);
                    var7 = true;
                    if (world.getBlockId(x, y + 1, z) == base.id)
                    {
                        world.setBlockWithNotify(x, y + 1, z, 0);
                    }
                }

                if (var7)
                {
                    if (!world.isRemote)
                    {
                        dropStacks(world, x, y, z, var6);
                    }
                }
                else if (id > 0 && Block.BLOCKS[id].canEmitRedstonePower())
                {
                    bool var8 = world.isPowered(x, y, z) || world.isPowered(x, y + 1, z);
                    setOpen(world, x, y, z, var8);
                }
            }

        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return (blockMeta & 8) != 0 ? 0 : (material == Material.METAL ? Item.doorSteel.id : Item.doorWood.id);
        }

        public override HitResult raycast(World world, int x, int y, int z, Vec3D startPos, Vec3D endPos)
        {
            updateBoundingBox(world, x, y, z);
            return base.raycast(world, x, y, z, startPos, endPos);
        }

        public int setOpen(int meta)
        {
            return (meta & 4) == 0 ? meta - 1 & 3 : meta & 3;
        }

        public override bool canPlaceAt(World world, int x, int y, int z)
        {
            return y >= 127 ? false : world.shouldSuffocate(x, y - 1, z) && base.canPlaceAt(world, x, y, z) && base.canPlaceAt(world, x, y + 1, z);
        }

        public static bool isOpen(int meta)
        {
            return (meta & 4) != 0;
        }

        public override int getPistonBehavior()
        {
            return 1;
        }
    }

}