using OmniBlock.Inventories;
using OmniBlock.Items;
using OmniBlock.Items.Behaviors;
using OmniBlock.Screens;
using OmniBlock.Screens.Slots;

namespace OmniBlock;

internal class SlotArmor : Slot
{
    private readonly int _pumpkinId;
    private readonly int armorType;
    private readonly PlayerScreenHandler inventory;

    public SlotArmor(PlayerScreenHandler screenHandler, IInventory inventory, int slotIndex, int x, int y, int armorType, int pumpkinId) : base(inventory, slotIndex, x, y)
    {
        this.inventory = screenHandler;
        this.armorType = armorType;
        _pumpkinId = pumpkinId;
    }


    public override int getMaxItemCount() => 1;

    public override bool canInsert(ItemStack stack)
    {
        var armor = stack.GetItem().GetBehavior<ArmorBehavior>();
        return armor != null
            ? armor.ArmorType == armorType
            : stack.GetItem().Id == _pumpkinId && armorType == 0;
    }
}
