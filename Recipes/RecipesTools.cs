using betareborn.Blocks;
using betareborn.Items;
using java.lang;

namespace betareborn.Recipes
{
    public class RecipesTools : java.lang.Object
    {
        private string[][] recipePatterns = [["XXX", " # ", " # "], ["X", "#", "#"], ["XX", "X#", " #"], ["XX", " #", " #"]];
        private object[][] recipeItems = [[Block.PLANKS, Block.COBBLESTONE, Item.ingotIron, Item.diamond, Item.ingotGold], [Item.pickaxeWood, Item.pickaxeStone, Item.pickaxeSteel, Item.pickaxeDiamond, Item.pickaxeGold], [Item.shovelWood, Item.shovelStone, Item.shovelSteel, Item.shovelDiamond, Item.shovelGold], [Item.axeWood, Item.axeStone, Item.axeSteel, Item.axeDiamond, Item.axeGold], [Item.hoeWood, Item.hoeStone, Item.hoeSteel, Item.hoeDiamond, Item.hoeGold]];

        public void addRecipes(CraftingManager var1)
        {
            for (int var2 = 0; var2 < recipeItems[0].Length; ++var2)
            {
                object var3 = recipeItems[0][var2];

                for (int var4 = 0; var4 < recipeItems.Length - 1; ++var4)
                {
                    Item var5 = (Item)recipeItems[var4 + 1][var2];
                    var1.addRecipe(new ItemStack(var5), [recipePatterns[var4], Character.valueOf('#'), Item.stick, Character.valueOf('X'), var3]);
                }
            }

            var1.addRecipe(new ItemStack(Item.shears), [" #", "# ", Character.valueOf('#'), Item.ingotIron]);
        }
    }

}