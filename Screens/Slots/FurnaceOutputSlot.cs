using betareborn.Entities;
using betareborn.Inventorys;
using betareborn.Items;

namespace betareborn.Screens.Slots
{
    public class FurnaceOutputSlot : Slot
    {

        private EntityPlayer thePlayer;

        public FurnaceOutputSlot(EntityPlayer var1, IInventory var2, int var3, int var4, int var5) : base(var2, var3, var4, var5)
        {
            thePlayer = var1;
        }

        public override bool canInsert(ItemStack var1)
        {
            return false;
        }

        public override void onTakeItem(ItemStack var1)
        {
            var1.onCraft(thePlayer.world, thePlayer);
            if (var1.itemID == Item.IRON_INGOT.id)
            {
                thePlayer.increaseStat(Achievements.ACQUIRE_IRON, 1);
            }

            if (var1.itemID == Item.COOKED_FISH.id)
            {
                thePlayer.increaseStat(Achievements.COOK_FISH, 1);
            }

            base.onTakeItem(var1);
        }
    }

}