using betareborn.Items;
using betareborn.Materials;

namespace betareborn.Blocks
{
    public class BlockGlowStone : Block
    {

        public BlockGlowStone(int var1, int var2, Material var3) : base(var1, var2, var3)
        {
        }

        public override int quantityDropped(java.util.Random var1)
        {
            return 2 + var1.nextInt(3);
        }

        public override int idDropped(int var1, java.util.Random var2)
        {
            return Item.lightStoneDust.id;
        }
    }

}