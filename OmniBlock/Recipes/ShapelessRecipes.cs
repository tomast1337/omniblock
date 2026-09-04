using OmniBlock.Inventories;
using OmniBlock.Items;

namespace OmniBlock.Recipes;

internal class ShapelessRecipes(ItemStack output, List<ItemStack> items) : IRecipe
{
    public ItemStack GetRecipeOutput() => output;

    public bool Matches(InventoryCrafting craftingInventory)
    {
        List<ItemStack> remainingIngredients = [.. items];

        for (var row = 0; row < 3; ++row)
        {
            for (var col = 0; col < 3; ++col)
            {
                var gridStack = craftingInventory.GetStackAt(col, row);
                if (gridStack == null)
                {
                    continue;
                }

                var foundMatch = false;
                var iterator = remainingIngredients.GetEnumerator();

                while (iterator.MoveNext())
                {
                    var recipeItem = iterator.Current;
                    if (gridStack.ItemId != recipeItem.ItemId || (recipeItem.GetDamage() != -1 && gridStack.GetDamage() != recipeItem.GetDamage()))
                    {
                        continue;
                    }

                    foundMatch = true;
                    remainingIngredients.Remove(recipeItem);
                    break;
                }

                if (!foundMatch)
                {
                    return false;
                }
            }
        }

        return remainingIngredients.Count == 0;
    }

    public ItemStack GetCraftingResult(InventoryCrafting craftingInventory) => output.Copy();

    public int GetRecipeSize() => items.Count;

    public override int GetHashCode()
    {
        var hash = 0;
        for (var i = 0; i < items.Count; i++)
        {
            hash += (items[i].ItemId + (items[i].GetDamage() << 8)) * (i + 1);
        }

        return hash + (output.ItemId << 12) + (output.GetDamage() << 20) + output.Count;
    }
}
