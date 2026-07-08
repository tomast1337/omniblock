using BetaSharp.Entities;
using BetaSharp.Inventorys;
using BetaSharp.Items;

namespace BetaSharp.Screens.Slots;

internal class FurnaceOutputSlot : Slot
{

    private EntityPlayer thePlayer;

    private static readonly Item s_ingotIron = Item.ByName("ingot_iron");
    private static readonly Item s_fishCooked = Item.ByName("fish_cooked");

    public FurnaceOutputSlot(EntityPlayer player, IInventory inventory, int slotIndex, int x, int y) : base(inventory, slotIndex, x, y)
    {
        thePlayer = player;
    }

    public override bool canInsert(ItemStack stack)
    {
        return false;
    }

    public override void onTakeItem(ItemStack stack)
    {
        stack.onCraft(thePlayer.World, thePlayer);
        if (stack.ItemId == s_ingotIron.Id)
        {
            thePlayer.IncreaseStat(Achievements.AcquireIron, 1);
        }

        if (stack.ItemId == s_fishCooked.Id)
        {
            thePlayer.IncreaseStat(Achievements.CookFish, 1);
        }

        base.onTakeItem(stack);
    }
}
