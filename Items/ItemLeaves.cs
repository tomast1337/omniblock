using betareborn.Blocks;

namespace betareborn.Items
{
    public class ItemLeaves : ItemBlock
    {

        public ItemLeaves(int var1) : base(var1)
        {
            setMaxDamage(0);
            setHasSubtypes(true);
        }

        public override int getPlacedBlockMetadata(int var1)
        {
            return var1 | 8;
        }

        public override int getIconFromDamage(int var1)
        {
            return Block.LEAVES.getTexture(0, var1);
        }

        public override int getColorFromDamage(int var1)
        {
            return (var1 & 1) == 1 ? ColorizerFoliage.getFoliageColorPine() : ((var1 & 2) == 2 ? ColorizerFoliage.getFoliageColorBirch() : ColorizerFoliage.func_31073_c());
        }
    }

}