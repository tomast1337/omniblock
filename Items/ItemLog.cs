using betareborn.Blocks;

namespace betareborn.Items
{
    public class ItemLog : ItemBlock
    {

        public ItemLog(int var1) : base(var1)
        {
            setMaxDamage(0);
            setHasSubtypes(true);
        }

        public override int getTextureId(int var1)
        {
            return Block.LOG.getTexture(2, var1);
        }

        public override int getPlacementMetadata(int var1)
        {
            return var1;
        }
    }

}