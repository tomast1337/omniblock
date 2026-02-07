using betareborn.Materials;

namespace betareborn.Blocks
{
    public class BlockSandStone : Block
    {
        public BlockSandStone(int var1) : base(var1, 192, Material.STONE)
        {
        }

        public override int getBlockTextureFromSide(int var1)
        {
            return var1 == 1 ? blockIndexInTexture - 16 : (var1 == 0 ? blockIndexInTexture + 16 : blockIndexInTexture);
        }
    }

}