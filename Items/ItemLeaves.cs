using betareborn.Blocks;
using betareborn.Client.Colors;

namespace betareborn.Items
{
    public class ItemLeaves : ItemBlock
    {

        public ItemLeaves(int var1) : base(var1)
        {
            setMaxDamage(0);
            setHasSubtypes(true);
        }

        public override int getPlacementMetadata(int var1)
        {
            return var1 | 8;
        }

        public override int getTextureId(int var1)
        {
            return Block.LEAVES.getTexture(0, var1);
        }

        public override int getColorMultiplier(int var1)
        {
            return (var1 & 1) == 1 ? FoliageColors.getSpruceColor() : ((var1 & 2) == 2 ? FoliageColors.getBirchColor() : FoliageColors.getDefaultColor());
        }
    }

}