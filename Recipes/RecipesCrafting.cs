using betareborn.Blocks;
using betareborn.Items;
using java.lang;

namespace betareborn.Recipes
{
    public class RecipesCrafting
    {
        public void addRecipes(CraftingManager var1)
        {
            var1.addRecipe(new ItemStack(Block.chest), ["###", "# #", "###", Character.valueOf('#'), Block.PLANKS]);
            var1.addRecipe(new ItemStack(Block.stoneOvenIdle), ["###", "# #", "###", Character.valueOf('#'), Block.COBBLESTONE]);
            var1.addRecipe(new ItemStack(Block.workbench), ["##", "##", Character.valueOf('#'), Block.PLANKS]);
            var1.addRecipe(new ItemStack(Block.SANDSTONE), ["##", "##", Character.valueOf('#'), Block.SAND]);
        }
    }

}