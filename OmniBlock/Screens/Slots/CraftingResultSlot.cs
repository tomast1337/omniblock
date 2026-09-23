using OmniBlock.Entities;
using OmniBlock.Inventories;
using OmniBlock.Items;

namespace OmniBlock.Screens.Slots;

internal class CraftingResultSlot : Slot
{
    private readonly IInventory craftMatrix;
    private readonly EntityPlayer thePlayer;

    public CraftingResultSlot(EntityPlayer player, IInventory craftMatrix, IInventory resultInventory, int slotIndex, int x, int y) : base(resultInventory, slotIndex, x, y)
    {
        thePlayer = player;
        this.craftMatrix = craftMatrix;
    }

    public override bool canInsert(ItemStack stack) => false;

    public override void onTakeItem(ItemStack stack)
    {
        stack.OnCraft(thePlayer.World, thePlayer);
        if (stack.ItemId == thePlayer.World.Content.Blocks.Get("omniblock:crafting_table").Id)
        {
            thePlayer.IncreaseStat(Achievements.BuildWorkbench, 1);
        }
        else if (stack.ItemId == thePlayer.World.Content.Items.Get("omniblock:pickaxe_wood").Id)
        {
            thePlayer.IncreaseStat(Achievements.BuildPickaxe, 1);
        }
        else if (stack.ItemId == thePlayer.World.Content.Blocks.Get("omniblock:furnace").Id)
        {
            thePlayer.IncreaseStat(Achievements.BuildFurnace, 1);
        }
        else if (stack.ItemId == thePlayer.World.Content.Items.Get("omniblock:hoe_wood").Id)
        {
            thePlayer.IncreaseStat(Achievements.BuildHoe, 1);
        }
        else if (stack.ItemId == thePlayer.World.Content.Items.Get("omniblock:bread").Id)
        {
            thePlayer.IncreaseStat(Achievements.MakeBread, 1);
        }
        else if (stack.ItemId == thePlayer.World.Content.Items.Get("omniblock:cake").Id)
        {
            thePlayer.IncreaseStat(Achievements.MakeCake, 1);
        }
        else if (stack.ItemId == thePlayer.World.Content.Items.Get("omniblock:pickaxe_stone").Id)
        {
            thePlayer.IncreaseStat(Achievements.CraftStonePickaxe, 1);
        }
        else if (stack.ItemId == thePlayer.World.Content.Items.Get("omniblock:sword_wood").Id)
        {
            thePlayer.IncreaseStat(Achievements.CraftSword, 1);
        }

        for (var slotIndex = 0; slotIndex < craftMatrix.Size; ++slotIndex)
        {
            var ingredientStack = craftMatrix.GetStack(slotIndex);
            if (ingredientStack != null)
            {
                craftMatrix.RemoveStack(slotIndex, 1);
                if (ingredientStack.GetItem().GetContainerItem() is { } containerItem)
                {
                    craftMatrix.SetStack(slotIndex, new ItemStack(containerItem));
                }
            }
        }
    }
}
