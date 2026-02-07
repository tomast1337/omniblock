using betareborn.Blocks;
using betareborn.Items;
using java.lang;

namespace betareborn.Recipes
{
    public class RecipesWeapons
    {
        private string[][] recipePatterns = [["X", "X", "#"]];
        private object[][] recipeItems = [[Block.PLANKS, Block.COBBLESTONE, Item.ingotIron, Item.diamond, Item.ingotGold], [Item.swordWood, Item.swordStone, Item.swordSteel, Item.swordDiamond, Item.swordGold]];

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

            var1.addRecipe(new ItemStack(Item.bow, 1), [" #X", "# X", " #X", Character.valueOf('X'), Item.silk, Character.valueOf('#'), Item.stick]);
            var1.addRecipe(new ItemStack(Item.arrow, 4), ["X", "#", "Y", Character.valueOf('Y'), Item.feather, Character.valueOf('X'), Item.flint, Character.valueOf('#'), Item.stick]);
        }
    }

}