using betareborn.Items;

namespace betareborn.Blocks
{
    public class BlockGravel : BlockSand
    {
        public BlockGravel(int var1, int var2) : base(var1, var2)
        {
        }

        public override int idDropped(int var1, java.util.Random var2)
        {
            return var2.nextInt(10) == 0 ? Item.flint.id : blockID;
        }
    }

}