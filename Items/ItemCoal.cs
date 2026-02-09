namespace betareborn.Items
{
    public class ItemCoal : Item
    {

        public ItemCoal(int var1) : base(var1)
        {
            setHasSubtypes(true);
            setMaxDamage(0);
        }

        public override String getItemNameIS(ItemStack var1)
        {
            return var1.getDamage() == 1 ? "item.charcoal" : "item.coal";
        }
    }

}