using betareborn.Entities;
using betareborn.Items;
using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockDoor : Block
    {
        public BlockDoor(int var1, Material var2) : base(var1, var2)
        {
            blockIndexInTexture = 97;
            if (var2 == Material.iron)
            {
                ++blockIndexInTexture;
            }

            float var3 = 0.5F;
            float var4 = 1.0F;
            setBlockBounds(0.5F - var3, 0.0F, 0.5F - var3, 0.5F + var3, var4, 0.5F + var3);
        }

        public override int getBlockTextureFromSideAndMetadata(int var1, int var2)
        {
            if (var1 != 0 && var1 != 1)
            {
                int var3 = getState(var2);
                if ((var3 == 0 || var3 == 2) ^ var1 <= 3)
                {
                    return blockIndexInTexture;
                }
                else
                {
                    int var4 = var3 / 2 + (var1 & 1 ^ var3);
                    var4 += (var2 & 4) / 4;
                    int var5 = blockIndexInTexture - (var2 & 8) * 2;
                    if ((var4 & 1) != 0)
                    {
                        var5 = -var5;
                    }

                    return var5;
                }
            }
            else
            {
                return blockIndexInTexture;
            }
        }

        public override bool isOpaqueCube()
        {
            return false;
        }

        public override bool renderAsNormalBlock()
        {
            return false;
        }

        public override int getRenderType()
        {
            return 7;
        }

        public override Box getSelectedBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            setBlockBoundsBasedOnState(var1, var2, var3, var4);
            return base.getSelectedBoundingBoxFromPool(var1, var2, var3, var4);
        }

        public override Box getCollisionBoundingBoxFromPool(World var1, int var2, int var3, int var4)
        {
            setBlockBoundsBasedOnState(var1, var2, var3, var4);
            return base.getCollisionBoundingBoxFromPool(var1, var2, var3, var4);
        }

        public override void setBlockBoundsBasedOnState(IBlockAccess var1, int var2, int var3, int var4)
        {
            setDoorRotation(getState(var1.getBlockMetadata(var2, var3, var4)));
        }

        public void setDoorRotation(int var1)
        {
            float var2 = 3.0F / 16.0F;
            setBlockBounds(0.0F, 0.0F, 0.0F, 1.0F, 2.0F, 1.0F);
            if (var1 == 0)
            {
                setBlockBounds(0.0F, 0.0F, 0.0F, 1.0F, 1.0F, var2);
            }

            if (var1 == 1)
            {
                setBlockBounds(1.0F - var2, 0.0F, 0.0F, 1.0F, 1.0F, 1.0F);
            }

            if (var1 == 2)
            {
                setBlockBounds(0.0F, 0.0F, 1.0F - var2, 1.0F, 1.0F, 1.0F);
            }

            if (var1 == 3)
            {
                setBlockBounds(0.0F, 0.0F, 0.0F, var2, 1.0F, 1.0F);
            }

        }

        public override void onBlockClicked(World var1, int var2, int var3, int var4, EntityPlayer var5)
        {
            blockActivated(var1, var2, var3, var4, var5);
        }

        public override bool blockActivated(World var1, int var2, int var3, int var4, EntityPlayer var5)
        {
            if (blockMaterial == Material.iron)
            {
                return true;
            }
            else
            {
                int var6 = var1.getBlockMetadata(var2, var3, var4);
                if ((var6 & 8) != 0)
                {
                    if (var1.getBlockId(var2, var3 - 1, var4) == blockID)
                    {
                        blockActivated(var1, var2, var3 - 1, var4, var5);
                    }

                    return true;
                }
                else
                {
                    if (var1.getBlockId(var2, var3 + 1, var4) == blockID)
                    {
                        var1.setBlockMetadataWithNotify(var2, var3 + 1, var4, (var6 ^ 4) + 8);
                    }

                    var1.setBlockMetadataWithNotify(var2, var3, var4, var6 ^ 4);
                    var1.markBlocksDirty(var2, var3 - 1, var4, var2, var3, var4);
                    var1.func_28107_a(var5, 1003, var2, var3, var4, 0);
                    return true;
                }
            }
        }

        public void onPoweredBlockChange(World var1, int var2, int var3, int var4, bool var5)
        {
            int var6 = var1.getBlockMetadata(var2, var3, var4);
            if ((var6 & 8) != 0)
            {
                if (var1.getBlockId(var2, var3 - 1, var4) == blockID)
                {
                    onPoweredBlockChange(var1, var2, var3 - 1, var4, var5);
                }

            }
            else
            {
                bool var7 = (var1.getBlockMetadata(var2, var3, var4) & 4) > 0;
                if (var7 != var5)
                {
                    if (var1.getBlockId(var2, var3 + 1, var4) == blockID)
                    {
                        var1.setBlockMetadataWithNotify(var2, var3 + 1, var4, (var6 ^ 4) + 8);
                    }

                    var1.setBlockMetadataWithNotify(var2, var3, var4, var6 ^ 4);
                    var1.markBlocksDirty(var2, var3 - 1, var4, var2, var3, var4);
                    var1.func_28107_a((EntityPlayer)null, 1003, var2, var3, var4, 0);
                }
            }
        }

        public override void onNeighborBlockChange(World var1, int var2, int var3, int var4, int var5)
        {
            int var6 = var1.getBlockMetadata(var2, var3, var4);
            if ((var6 & 8) != 0)
            {
                if (var1.getBlockId(var2, var3 - 1, var4) != blockID)
                {
                    var1.setBlockWithNotify(var2, var3, var4, 0);
                }

                if (var5 > 0 && Block.blocksList[var5].canProvidePower())
                {
                    onNeighborBlockChange(var1, var2, var3 - 1, var4, var5);
                }
            }
            else
            {
                bool var7 = false;
                if (var1.getBlockId(var2, var3 + 1, var4) != blockID)
                {
                    var1.setBlockWithNotify(var2, var3, var4, 0);
                    var7 = true;
                }

                if (!var1.isBlockNormalCube(var2, var3 - 1, var4))
                {
                    var1.setBlockWithNotify(var2, var3, var4, 0);
                    var7 = true;
                    if (var1.getBlockId(var2, var3 + 1, var4) == blockID)
                    {
                        var1.setBlockWithNotify(var2, var3 + 1, var4, 0);
                    }
                }

                if (var7)
                {
                    if (!var1.multiplayerWorld)
                    {
                        dropBlockAsItem(var1, var2, var3, var4, var6);
                    }
                }
                else if (var5 > 0 && Block.blocksList[var5].canProvidePower())
                {
                    bool var8 = var1.isBlockIndirectlyGettingPowered(var2, var3, var4) || var1.isBlockIndirectlyGettingPowered(var2, var3 + 1, var4);
                    onPoweredBlockChange(var1, var2, var3, var4, var8);
                }
            }

        }

        public override int idDropped(int var1, java.util.Random var2)
        {
            return (var1 & 8) != 0 ? 0 : (blockMaterial == Material.iron ? Item.doorSteel.shiftedIndex : Item.doorWood.shiftedIndex);
        }

        public override MovingObjectPosition collisionRayTrace(World var1, int var2, int var3, int var4, Vec3D var5, Vec3D var6)
        {
            setBlockBoundsBasedOnState(var1, var2, var3, var4);
            return base.collisionRayTrace(var1, var2, var3, var4, var5, var6);
        }

        public int getState(int var1)
        {
            return (var1 & 4) == 0 ? var1 - 1 & 3 : var1 & 3;
        }

        public override bool canPlaceBlockAt(World var1, int var2, int var3, int var4)
        {
            return var3 >= 127 ? false : var1.isBlockNormalCube(var2, var3 - 1, var4) && base.canPlaceBlockAt(var1, var2, var3, var4) && base.canPlaceBlockAt(var1, var2, var3 + 1, var4);
        }

        public static bool isOpen(int var0)
        {
            return (var0 & 4) != 0;
        }

        public override int getMobilityFlag()
        {
            return 1;
        }
    }

}