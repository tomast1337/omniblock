using betareborn.Blocks;
using betareborn.Items;
using java.lang;
using java.util;

namespace betareborn
{
    public class SmeltingRecipeManager
    {
        private static readonly SmeltingRecipeManager smeltingBase = new();
        private Map smeltingList = new HashMap();

        public static SmeltingRecipeManager getInstance()
        {
            return smeltingBase;
        }

        private SmeltingRecipeManager()
        {
            addSmelting(Block.oreIron.id, new ItemStack(Item.ingotIron));
            addSmelting(Block.oreGold.id, new ItemStack(Item.ingotGold));
            addSmelting(Block.oreDiamond.id, new ItemStack(Item.diamond));
            addSmelting(Block.sand.id, new ItemStack(Block.glass));
            addSmelting(Item.porkRaw.id, new ItemStack(Item.porkCooked));
            addSmelting(Item.fishRaw.id, new ItemStack(Item.fishCooked));
            addSmelting(Block.cobblestone.id, new ItemStack(Block.stone));
            addSmelting(Item.clay.id, new ItemStack(Item.brick));
            addSmelting(Block.cactus.id, new ItemStack(Item.dyePowder, 1, 2));
            addSmelting(Block.wood.id, new ItemStack(Item.coal, 1, 1));
        }

        public void addSmelting(int var1, ItemStack var2)
        {
            smeltingList.put(Integer.valueOf(var1), var2);
        }

        public ItemStack craft(int var1)
        {
            return (ItemStack)smeltingList.get(Integer.valueOf(var1));
        }

        public Map getSmeltingList()
        {
            return smeltingList;
        }
    }

}