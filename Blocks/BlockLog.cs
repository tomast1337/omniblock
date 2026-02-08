using betareborn.Blocks.Materials;
using betareborn.Entities;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockLog : Block
    {
        public BlockLog(int id) : base(id, Material.WOOD)
        {
            textureId = 20;
        }

        public override int getDroppedItemCount(java.util.Random random)
        {
            return 1;
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return Block.LOG.id;
        }

        public override void afterBreak(World world, EntityPlayer player, int x, int y, int z, int meta)
        {
            base.afterBreak(world, player, x, y, z, meta);
        }

        public override void onBreak(World world, int x, int y, int z)
        {
            sbyte var5 = 4;
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
                                if ((var11 & 8) == 0)
                                {
                                    world.setBlockMetadata(x + var7, y + var8, z + var9, var11 | 8);
                                }
                            }
                        }
                    }
                }
            }

        }

        public override int getTexture(int side, int meta)
        {
            return side == 1 ? 21 : (side == 0 ? 21 : (meta == 1 ? 116 : (meta == 2 ? 117 : 20)));
        }

        protected override int getDroppedItemMeta(int blockMeta)
        {
            return blockMeta;
        }
    }

}