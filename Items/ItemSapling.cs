using betareborn.Blocks;

namespace betareborn.Items
{
    public class ItemSapling : ItemBlock
    {

        public ItemSapling(int var1) : base(var1)
        {
            setMaxDamage(0);
            setHasSubtypes(true);
        }

        public override int getPlacementMetadata(int var1)
        {
            return var1;
        }

        public override int getTextureId(int var1)
        {
            return Block.SAPLING.getTexture(0, var1);
        }
    }

}