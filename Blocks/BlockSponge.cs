using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockSponge : Block
    {
        public BlockSponge(int var1) : base(var1, Material.SPONGE)
        {
            blockIndexInTexture = 48;
        }

        public override void onBlockAdded(World var1, int var2, int var3, int var4)
        {
            sbyte var5 = 2;

            for (int var6 = var2 - var5; var6 <= var2 + var5; ++var6)
            {
                for (int var7 = var3 - var5; var7 <= var3 + var5; ++var7)
                {
                    for (int var8 = var4 - var5; var8 <= var4 + var5; ++var8)
                    {
                        if (var1.getMaterial(var6, var7, var8) == Material.WATER)
                        {
                        }
                    }
                }
            }

        }

        public override void onBlockRemoval(World var1, int var2, int var3, int var4)
        {
            sbyte var5 = 2;

            for (int var6 = var2 - var5; var6 <= var2 + var5; ++var6)
            {
                for (int var7 = var3 - var5; var7 <= var3 + var5; ++var7)
                {
                    for (int var8 = var4 - var5; var8 <= var4 + var5; ++var8)
                    {
                        var1.notifyBlocksOfNeighborChange(var6, var7, var8, var1.getBlockId(var6, var7, var8));
                    }
                }
            }

        }
    }

}