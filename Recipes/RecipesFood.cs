using betareborn.Blocks;
using betareborn.Items;
using java.lang;

namespace betareborn.Recipes
{
    public class RecipesFood
    {
        public void addRecipes(CraftingManager var1)
        {
            var1.addRecipe(new ItemStack(Item.bowlSoup), new object[] { "Y", "X", "#", Character.valueOf('X'), Block.BROWN_MUSHROOM, Character.valueOf('Y'), Block.RED_MUSHROOM, Character.valueOf('#'), Item.bowlEmpty });
            var1.addRecipe(new ItemStack(Item.bowlSoup), new object[] { "Y", "X", "#", Character.valueOf('X'), Block.RED_MUSHROOM, Character.valueOf('Y'), Block.BROWN_MUSHROOM, Character.valueOf('#'), Item.bowlEmpty });
            var1.addRecipe(new ItemStack(Item.cookie, 8), ["#X#", Character.valueOf('X'), new ItemStack(Item.dyePowder, 1, 3), Character.valueOf('#'), Item.wheat]);
        }
    }

}