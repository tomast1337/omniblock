using betareborn.Items;

namespace betareborn.Blocks
{
    public class BlockGravel : BlockSand
    {
        public BlockGravel(int i, int j) : base(i, j)
        {
        }

        public override int getDroppedItemId(int blockMeta, java.util.Random random)
        {
            return random.nextInt(10) == 0 ? Item.FLINT.id : id;
        }
    }

}