using betareborn.Materials;

namespace betareborn.Blocks
{
    public class BlockOreStorage : Block
    {

        public BlockOreStorage(int var1, int var2) : base(var1, Material.METAL)
        {
            blockIndexInTexture = var2;
        }

        public override int getBlockTextureFromSide(int var1)
        {
            return blockIndexInTexture;
        }
    }

}