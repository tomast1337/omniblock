using betareborn.Blocks;

namespace betareborn.Items
{
    public class ItemSpade : ItemTool
    {

        private static Block[] blocksEffectiveAgainst = new Block[] { Block.GRASS_BLOCK, Block.DIRT, Block.SAND, Block.GRAVEL, Block.snow, Block.blockSnow, Block.blockClay, Block.tilledField };

        public ItemSpade(int var1, EnumToolMaterial var2) : base(var1, 1, var2, blocksEffectiveAgainst)
        {
        }

        public override bool canHarvestBlock(Block var1)
        {
            return var1 == Block.snow ? true : var1 == Block.blockSnow;
        }
    }

}