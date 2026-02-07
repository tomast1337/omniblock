using betareborn.Materials;
using betareborn.Worlds;

namespace betareborn.Blocks
{
    public class BlockLockedChest : Block
    {

        public BlockLockedChest(int var1) : base(var1, Material.WOOD)
        {
            blockIndexInTexture = 26;
        }

        public override int getBlockTexture(IBlockAccess var1, int var2, int var3, int var4, int var5)
        {
            if (var5 == 1)
            {
                return blockIndexInTexture - 1;
            }
            else if (var5 == 0)
            {
                return blockIndexInTexture - 1;
            }
            else
            {
                int var6 = var1.getBlockId(var2, var3, var4 - 1);
                int var7 = var1.getBlockId(var2, var3, var4 + 1);
                int var8 = var1.getBlockId(var2 - 1, var3, var4);
                int var9 = var1.getBlockId(var2 + 1, var3, var4);
                sbyte var10 = 3;
                if (Block.opaqueCubeLookup[var6] && !Block.opaqueCubeLookup[var7])
                {
                    var10 = 3;
                }

                if (Block.opaqueCubeLookup[var7] && !Block.opaqueCubeLookup[var6])
                {
                    var10 = 2;
                }

                if (Block.opaqueCubeLookup[var8] && !Block.opaqueCubeLookup[var9])
                {
                    var10 = 5;
                }

                if (Block.opaqueCubeLookup[var9] && !Block.opaqueCubeLookup[var8])
                {
                    var10 = 4;
                }

                return var5 == var10 ? blockIndexInTexture + 1 : blockIndexInTexture;
            }
        }

        public override int getBlockTextureFromSide(int var1)
        {
            return var1 == 1 ? blockIndexInTexture - 1 : (var1 == 0 ? blockIndexInTexture - 1 : (var1 == 3 ? blockIndexInTexture + 1 : blockIndexInTexture));
        }

        public override bool canPlaceBlockAt(World var1, int var2, int var3, int var4)
        {
            return true;
        }

        public override void updateTick(World var1, int var2, int var3, int var4, java.util.Random var5)
        {
            var1.setBlockWithNotify(var2, var3, var4, 0);
        }
    }

}