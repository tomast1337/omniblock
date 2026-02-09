using betareborn.Blocks;
using betareborn.Entities;
using betareborn.Inventorys;
using betareborn.Items;

namespace betareborn.Screens.Slots
{
    public class CraftingResultSlot : Slot
    {

        private readonly IInventory craftMatrix;
        private EntityPlayer thePlayer;

        public CraftingResultSlot(EntityPlayer var1, IInventory var2, IInventory var3, int var4, int var5, int var6) : base(var3, var4, var5, var6)
        {
            thePlayer = var1;
            craftMatrix = var2;
        }

        public override bool canInsert(ItemStack var1)
        {
            return false;
        }

        public override void onTakeItem(ItemStack var1)
        {
            var1.onCraft(thePlayer.world, thePlayer);
            if (var1.itemID == Block.CRAFTING_TABLE.id)
            {
                thePlayer.increaseStat(Achievements.BUILD_WORKBENCH, 1);
            }
            else if (var1.itemID == Item.WOODEN_PICKAXE.id)
            {
                thePlayer.increaseStat(Achievements.BUILD_PICKAXE, 1);
            }
            else if (var1.itemID == Block.FURNACE.id)
            {
                thePlayer.increaseStat(Achievements.BUILD_FURNACE, 1);
            }
            else if (var1.itemID == Item.WOODEN_HOE.id)
            {
                thePlayer.increaseStat(Achievements.BUILD_HOE, 1);
            }
            else if (var1.itemID == Item.BREAD.id)
            {
                thePlayer.increaseStat(Achievements.MAKE_BREAD, 1);
            }
            else if (var1.itemID == Item.CAKE.id)
            {
                thePlayer.increaseStat(Achievements.BAKE_CAKE, 1);
            }
            else if (var1.itemID == Item.STONE_PICKAXE.id)
            {
                thePlayer.increaseStat(Achievements.CRAFT_STONE_PICKAXE, 1);
            }
            else if (var1.itemID == Item.WOODEN_SWORD.id)
            {
                thePlayer.increaseStat(Achievements.CRAFT_SWORD, 1);
            }

            for (int var2 = 0; var2 < craftMatrix.size(); ++var2)
            {
                ItemStack var3 = craftMatrix.getStack(var2);
                if (var3 != null)
                {
                    craftMatrix.removeStack(var2, 1);
                    if (var3.getItem().hasContainerItem())
                    {
                        craftMatrix.setStack(var2, new ItemStack(var3.getItem().getContainerItem()));
                    }
                }
            }

        }
    }

}