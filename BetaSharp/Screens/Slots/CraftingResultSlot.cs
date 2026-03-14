using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Inventorys;
using BetaSharp.Items;

namespace BetaSharp.Screens.Slots;

internal class CraftingResultSlot : Slot
{

    private readonly IInventory craftMatrix;
    private EntityPlayer thePlayer;

    public CraftingResultSlot(EntityPlayer player, IInventory craftMatrix, IInventory resultInventory, int slotIndex, int x, int y) : base(resultInventory, slotIndex, x, y)
    {
        thePlayer = player;
        this.craftMatrix = craftMatrix;
    }

    public override bool canInsert(ItemStack stack)
    {
        return false;
    }

    public override void onTakeItem(ItemStack stack)
    {
        stack.onCraft(thePlayer.world, thePlayer);
        if (stack.itemId == Block.CraftingTable.id)
        {
            thePlayer.increaseStat(Achievements.BuildWorkbench, 1);
        }
        else if (stack.itemId == Item.WoodenPickaxe.id)
        {
            thePlayer.increaseStat(Achievements.BuildPickaxe, 1);
        }
        else if (stack.itemId == Block.Furnace.id)
        {
            thePlayer.increaseStat(Achievements.BuildFurnace, 1);
        }
        else if (stack.itemId == Item.WoodenHoe.id)
        {
            thePlayer.increaseStat(Achievements.BuildHoe, 1);
        }
        else if (stack.itemId == Item.Bread.id)
        {
            thePlayer.increaseStat(Achievements.MakeBread, 1);
        }
        else if (stack.itemId == Item.Cake.id)
        {
            thePlayer.increaseStat(Achievements.MakeCake, 1);
        }
        else if (stack.itemId == Item.StonePickaxe.id)
        {
            thePlayer.increaseStat(Achievements.CraftStonePickaxe, 1);
        }
        else if (stack.itemId == Item.WoodenSword.id)
        {
            thePlayer.increaseStat(Achievements.CraftSword, 1);
        }

        for (int slotIndex = 0; slotIndex < craftMatrix.size(); ++slotIndex)
        {
            ItemStack ingredientStack = craftMatrix.getStack(slotIndex);
            if (ingredientStack != null)
            {
                craftMatrix.removeStack(slotIndex, 1);
                if (ingredientStack.getItem().hasContainerItem())
                {
                    craftMatrix.setStack(slotIndex, new ItemStack(ingredientStack.getItem().getContainerItem()));
                }
            }
        }

    }
}
