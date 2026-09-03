using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;

namespace OmniBlock.Screens.Slots;

internal class FurnaceOutputSlot : Slot
{

    private EntityPlayer thePlayer;

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
        stack.OnCraft(thePlayer.World, thePlayer);
        if (stack.ItemId == thePlayer.World.Content.Items.Get("omniblock:ingot_iron").Id)
        {
            thePlayer.IncreaseStat(Achievements.AcquireIron, 1);
        }

        if (stack.ItemId == thePlayer.World.Content.Items.Get("omniblock:fish_cooked").Id)
        {
            thePlayer.IncreaseStat(Achievements.CookFish, 1);
        }

        base.onTakeItem(stack);
    }
}
