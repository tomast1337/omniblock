namespace betareborn.Items
{
    public class ItemPiston : ItemBlock
    {

        public ItemPiston(int var1) : base(var1)
        {
        }

        public override int getPlacementMetadata(int var1)
        {
            return 7;
        }
    }

}