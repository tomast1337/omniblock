using betareborn.Items;
using betareborn.Materials;

namespace betareborn.Blocks
{
    public class BlockClay : Block
    {

        public BlockClay(int var1, int var2) : base(var1, var2, Material.CLAY)
        {
        }

        public override int getDroppedItemId(int var1, java.util.Random var2)
        {
            return Item.clay.id;
        }

        public override int getDroppedItemCount(java.util.Random var1)
        {
            return 4;
        }
    }

}