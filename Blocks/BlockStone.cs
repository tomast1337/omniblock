using betareborn.Materials;

namespace betareborn.Blocks
{
    public class BlockStone : Block
    {
        public BlockStone(int var1, int var2) : base(var1, var2, Material.STONE)
        {
        }

        public override int getDroppedItemId(int var1, java.util.Random var2)
        {
            return Block.COBBLESTONE.id;
        }
    }

}