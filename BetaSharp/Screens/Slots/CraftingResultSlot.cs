using BetaSharp.Blocks;
using BetaSharp.Entities;
using BetaSharp.Inventorys;
using BetaSharp.Items;

namespace BetaSharp.Screens.Slots;

internal class CraftingResultSlot : Slot
{

    private readonly IInventory craftMatrix;
    private EntityPlayer thePlayer;

    private static readonly Item s_pickaxeWood = Item.ByName("pickaxe_wood");
    private static readonly Item s_hoeWood = Item.ByName("hoe_wood");
    private static readonly Item s_bread = Item.ByName("bread");
    private static readonly Item s_cake = Item.ByName("cake");
    private static readonly Item s_pickaxeStone = Item.ByName("pickaxe_stone");
    private static readonly Item s_swordWood = Item.ByName("sword_wood");

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
        stack.onCraft(thePlayer.World, thePlayer);
        if (stack.ItemId == Block.CraftingTable.id)
        {
            thePlayer.IncreaseStat(Achievements.BuildWorkbench, 1);
        }
        else if (stack.ItemId == s_pickaxeWood.Id)
        {
            thePlayer.IncreaseStat(Achievements.BuildPickaxe, 1);
        }
        else if (stack.ItemId == Block.Furnace.id)
        {
            thePlayer.IncreaseStat(Achievements.BuildFurnace, 1);
        }
        else if (stack.ItemId == s_hoeWood.Id)
        {
            thePlayer.IncreaseStat(Achievements.BuildHoe, 1);
        }
        else if (stack.ItemId == s_bread.Id)
        {
            thePlayer.IncreaseStat(Achievements.MakeBread, 1);
        }
        else if (stack.ItemId == s_cake.Id)
        {
            thePlayer.IncreaseStat(Achievements.MakeCake, 1);
        }
        else if (stack.ItemId == s_pickaxeStone.Id)
        {
            thePlayer.IncreaseStat(Achievements.CraftStonePickaxe, 1);
        }
        else if (stack.ItemId == s_swordWood.Id)
        {
            thePlayer.IncreaseStat(Achievements.CraftSword, 1);
        }

        for (int slotIndex = 0; slotIndex < craftMatrix.Size; ++slotIndex)
        {
            ItemStack? ingredientStack = craftMatrix.GetStack(slotIndex);
            if (ingredientStack != null)
            {
                craftMatrix.RemoveStack(slotIndex, 1);
                if (ingredientStack.getItem().hasContainerItem())
                {
                    craftMatrix.SetStack(slotIndex, new ItemStack(ingredientStack.getItem().getContainerItem()));
                }
            }
        }

    }
}
