using betareborn.Entities;
using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockLog : Block
    {
        public BlockLog(int var1) : base(var1, Material.WOOD)
        {
            textureId = 20;
        }

        public override int quantityDropped(java.util.Random var1)
        {
            return 1;
        }

        public override int getDroppedItemId(int var1, java.util.Random var2)
        {
            return Block.LOG.id;
        }

        public override void harvestBlock(World var1, EntityPlayer var2, int var3, int var4, int var5, int var6)
        {
            base.harvestBlock(var1, var2, var3, var4, var5, var6);
        }

        public override void onBlockRemoval(World var1, int var2, int var3, int var4)
        {
            sbyte var5 = 4;
            int var6 = var5 + 1;
            if (var1.checkChunksExist(var2 - var6, var3 - var6, var4 - var6, var2 + var6, var3 + var6, var4 + var6))
            {
                for (int var7 = -var5; var7 <= var5; ++var7)
                {
                    for (int var8 = -var5; var8 <= var5; ++var8)
                    {
                        for (int var9 = -var5; var9 <= var5; ++var9)
                        {
                            int var10 = var1.getBlockId(var2 + var7, var3 + var8, var4 + var9);
                            if (var10 == Block.LEAVES.id)
                            {
                                int var11 = var1.getBlockMeta(var2 + var7, var3 + var8, var4 + var9);
                                if ((var11 & 8) == 0)
                                {
                                    var1.setBlockMetadata(var2 + var7, var3 + var8, var4 + var9, var11 | 8);
                                }
                            }
                        }
                    }
                }
            }

        }

        public override int getTexture(int var1, int var2)
        {
            return var1 == 1 ? 21 : (var1 == 0 ? 21 : (var2 == 1 ? 116 : (var2 == 2 ? 117 : 20)));
        }

        protected override int damageDropped(int var1)
        {
            return var1;
        }
    }

}