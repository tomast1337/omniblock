using OmniBlock.Inventories;
using OmniBlock.Items;

namespace OmniBlock.Screens.Slots;

public class Slot
{
    private readonly IInventory inventory;
    private readonly int slotIndex;
    public int id;
    public int xDisplayPosition;
    public int yDisplayPosition;

    public Slot(IInventory inv, int index, int x, int y)
    {
        inventory = inv;
        slotIndex = index;
        xDisplayPosition = x;
        yDisplayPosition = y;
    }

    public virtual void onTakeItem(ItemStack stack) => markDirty();

    public virtual bool canInsert(ItemStack stack) => true;

    public ItemStack? getStack() => inventory.GetStack(slotIndex);

    public bool hasStack() => getStack() != null;

    public void setStack(ItemStack? stack)
    {
        inventory.SetStack(slotIndex, stack);
        markDirty();
    }

    public void markDirty() => inventory.MarkDirty();

    public virtual int getMaxItemCount() => inventory.MaxCountPerStack;

    public static int getBackgroundTextureId() => -1;

    public ItemStack? takeStack(int amount) => inventory.RemoveStack(slotIndex, amount);

    public bool Equals(IInventory inventory, int index) => inventory == this.inventory && index == slotIndex;
}
